using System.Reflection;
using Hardware.KmBox;
using Roadhog.Infrastructure.Input;

internal static class QuickbarChordTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static (KmBoxNetKeyboardInput Input, Device Device) Create()
    {
        var device = DispatchProxy.Create<IKmBoxDevice, Device>();
        return (new(new KmBoxNetKeyboardInputOptions(), device), (Device)device);
    }

    public static async Task SequenceAsync()
    {
        var (input, device) = Create(); using var cleanup = input;
        foreach (var key in new[] { "Alt+D1", "Ctrl+D0", "Alt+OemMinus", "Ctrl+OemPlus" })
        {
            device.Calls.Clear();
            Check((await input.PressKeyAsync(key, TimeSpan.FromMilliseconds(25))).Success, "chord accepted");
            var modifier = key.StartsWith("Alt+") ? 226 : 224;
            var primary = key.Split('+')[1] switch { "D1" => 30, "D0" => 39, "OemMinus" => 45, _ => 46 };
            Check(device.Calls.Where(c => !c.StartsWith("Connect")).SequenceEqual(new[]
                { $"KeyDownAsync:{modifier}", $"KeyDownAsync:{primary}", $"KeyUpAsync:{primary}", $"KeyUpAsync:{modifier}" }),
                "modifier report precedes primary; both release in reverse order");
        }
        device.Calls.Clear();
        Check((await input.PressKeyAsync("NumPad1", TimeSpan.FromMilliseconds(25))).Success, "legacy input accepted");
        Check(device.Calls.SequenceEqual(new[] { "PressKeyAsync:89:25" }), "legacy single key retains caller hold");
        foreach (var invalid in new[] { "Alt+X", "Ctrl+NumPad1", "Alt+Ctrl+D1", "Alt+", "Shift+D1" })
        {
            device.Calls.Clear();
            Check(!(await input.PressKeyAsync(invalid, TimeSpan.Zero)).Success && device.Calls.Count == 0,
                "invalid chord sends no input");
        }
    }

    public static async Task CleanupAsync()
    {
        foreach (var failedCall in new[] { "KeyDownAsync:226", "KeyDownAsync:30", "KeyUpAsync:30", "KeyUpAsync:226" })
        {
            var (input, device) = Create(); using var cleanup = input;
            device.OnCall = (call, _) => call == failedCall ? Task.FromException(new IOException("lost acknowledgement")) : Task.CompletedTask;
            Check(!(await input.PressKeyAsync("Alt+D1", TimeSpan.Zero)).Success, "failed chord cannot report success");
            Check(device.Calls.TakeLast(2).SequenceEqual(new[] { "KeyUpAsync:30", "KeyUpAsync:226" }),
                "even a failed modifier press or primary release attempts both releases");
            Check(device.ReleaseTokens.All(t => !t.CanBeCanceled), "cleanup is uncancellable");
            device.OnCall = null;
            Check((await input.PressKeyAsync("D2", TimeSpan.FromMilliseconds(25))).Success, "failure releases input gate for retry");
        }
        var (cancelInput, cancelDevice) = Create(); using var cancelCleanup = cancelInput;
        using var cancellation = new CancellationTokenSource();
        cancelDevice.OnCall = (call, _) => { if (call == "KeyDownAsync:31") cancellation.Cancel(); return Task.CompletedTask; };
        try
        {
            await cancelInput.PressKeyAsync("Ctrl+D2", TimeSpan.Zero, cancellation.Token);
            throw new Exception("cancelled chord must throw");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Check(cancelDevice.Calls.TakeLast(2).SequenceEqual(new[] { "KeyUpAsync:31", "KeyUpAsync:224" }), "cancellation releases both keys");
    }

    public static async Task SerializationAsync()
    {
        var (input, device) = Create(); using var cleanup = input;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.OnCall = (call, _) =>
        {
            if (call != "KeyDownAsync:226") return Task.CompletedTask;
            entered.TrySetResult(); return resume.Task;
        };
        var chord = input.PressKeyAsync("Alt+D1", TimeSpan.Zero);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var single = input.PressKeyAsync("D2", TimeSpan.FromMilliseconds(25));
        var reset = input.ReleaseAllAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelled = input.KeyDownAsync("W", cancellation.Token);
        cancellation.Cancel();
        try { await cancelled; throw new Exception("queued cancellation must throw"); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Check(!single.IsCompleted && !reset.IsCompleted && device.Calls.All(c => !c.StartsWith("Press")), "queued actions cannot split a chord");
        resume.SetResult();
        Check((await chord).Success && (await single).Success && (await reset).Success, "queued work completes after release");
        Check(device.Calls.IndexOf("PressKeyAsync:31:25") > device.Calls.IndexOf("KeyUpAsync:226") &&
            device.Calls.IndexOf("ReleaseAllAsync") > device.Calls.IndexOf("KeyUpAsync:226"), "ordinary input and reset wait for modifier release");
        Check(!device.Calls.Contains("KeyDownAsync:26"), "cancelled queued input is not sent");
    }

    public class Device : DispatchProxy
    {
        public List<string> Calls { get; } = new();
        public List<CancellationToken> ReleaseTokens { get; } = new();
        public Func<string, CancellationToken, Task>? OnCall { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            args ??= Array.Empty<object?>();
            var name = method!.Name;
            var token = args.LastOrDefault() is CancellationToken ct ? ct : CancellationToken.None;
            var call = name + (args.FirstOrDefault() is int code ? ":" + code : "");
            if (name == "PressKeyAsync") call += ":" + args[1];
            Calls.Add(call);
            if (name == "KeyUpAsync") ReleaseTokens.Add(token);
            if (name == "ConnectAsync") return Task.FromResult(true);
            return OnCall?.Invoke(call, token) ?? Task.CompletedTask;
        }
    }
}
