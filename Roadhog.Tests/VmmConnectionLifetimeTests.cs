using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Vmm;

internal static class VmmConnectionLifetimeTests
{
    public static Task PartialDisposeAsync()
    {
        // Reproduce the state left by the failing vendor constructor without opening any hardware.
        var original = (Vmmsharp.Vmm)RuntimeHelpers.GetUninitializedObject(typeof(Vmmsharp.Vmm));
        GC.SuppressFinalize(original);
        try { original.Dispose(); throw new InvalidOperationException("vendor failure was not reproduced"); }
        catch (NullReferenceException) { }
        var safe = NewPartial();
        safe.Dispose();
        safe.Dispose();
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SafeMemProcVmm NewPartial() =>
        (SafeMemProcVmm)RuntimeHelpers.GetUninitializedObject(typeof(SafeMemProcVmm));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonPartial() => new(NewPartial());

    public static void RunFinalizerProbe()
    {
        var references = Enumerable.Range(0, 100).Select(_ => AbandonPartial()).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (references.Any(r => r.IsAlive)) throw new InvalidOperationException("partial objects did not finish finalization");
        Console.WriteLine("FINALIZERS_SURVIVED");
    }

    public static async Task FinalizerProcessAsync()
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--vmm-lifetime-finalizer-probe");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("could not start finalizer regression child");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(deadline.Token); }
        finally { if (!child.HasExited) child.Kill(); }
        if (child.ExitCode != 0 || !(await output).Contains("FINALIZERS_SURVIVED"))
            throw new InvalidOperationException("finalizer regression crashed: " + await error);
    }

    public static async Task RetirementAsync()
    {
        var connection = new VmmConnection("fake", "", NewPartial());
        using var reading = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        using var retirementStarted = new ManualResetEventSlim();
        var read = Task.Run(() =>
        {
            lock (connection.SyncRoot)
            {
                _ = connection.Vmm;
                reading.Set();
                if (!releaseRead.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                _ = connection.Vmm;
            }
        });
        if (!reading.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        var retire = Task.Run(() => { retirementStarted.Set(); connection.Dispose(); });
        try
        {
            if (!retirementStarted.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            await Task.Delay(50);
            if (retire.IsCompleted) throw new InvalidOperationException("connection disposed during an active read");
        }
        finally { releaseRead.Set(); }
        await Task.WhenAll(read, retire);
        connection.Dispose();
        try { _ = connection.Vmm; throw new InvalidOperationException("retired connection allowed a queued read"); }
        catch (ObjectDisposedException) { }
    }

    public static async Task AllChannelsRespectReconnectAsync()
    {
        var api = new AionVmmGameApi(new AionVmmGameApiOptions(), NoOpRoadhogLogger.Instance);
        var context = new GameApiReadContext("retry-test", 0, "Aion.bin", "fake-device");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(AionVmmGameApi).GetMethod("DelayConnectionRetry", flags)!.Invoke(api, new object[] { "fake-device", TimeSpan.FromMinutes(1) });
        var channel = await api.ReadChannelAsync(context);
        if (channel.Success || channel.Error?.Contains("cooling down") != true)
            throw new InvalidOperationException("channel read bypassed reconnect delay: " + channel.Error);
        var player = await api.ReadPlayerAsync(context);
        if (player.Success || player.Error?.Contains("cooling down") != true)
            throw new InvalidOperationException("player read bypassed reconnect delay");
        var scene = await api.ReadChannelTransitionAsync(context);
        if (scene.Success || scene.Error?.Contains("cooling down") != true)
            throw new InvalidOperationException("transition read bypassed reconnect delay");
        var retiring = (HashSet<string>)typeof(AionVmmGameApi).GetField("_connectionsRetiring", flags)!.GetValue(api)!;
        retiring.Add("fake-device|");
        channel = await api.ReadChannelAsync(context);
        if (channel.Success || channel.Error?.Contains("retirement") != true)
            throw new InvalidOperationException("channel read attempted to reopen a retiring connection");
        scene = await api.ReadChannelTransitionAsync(context);
        if (scene.Success || scene.Error?.Contains("retirement") != true)
            throw new InvalidOperationException("transition read attempted to reopen a retiring connection");
    }
}
