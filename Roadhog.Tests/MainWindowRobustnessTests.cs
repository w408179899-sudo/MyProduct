using System.Reflection;
using Roadhog;
using Roadhog.Application.Shell;
using Roadhog.Infrastructure.Composition;

internal static class MainWindowRobustnessTests
{
    public static async Task ShutdownCancelsAndDrainsInitializationAsync()
    {
        using var operations = new MainWindowOperations();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = operations.Token;
        var initialization = operations.RunAsync(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); await cleanup.Task; }
        });
        operations.BeginShutdown();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var drain = operations.DrainAsync();
        Require(!drain.IsCompleted, "shutdown must wait for initialization cleanup before disposing the workspace");
        var forbiddenRan = false;
        await ExpectCancelled(operations.RunAsync(() => { forbiddenRan = true; return Task.CompletedTask; }));
        Require(!forbiddenRan, "closing must reject late UI work");
        cleanup.TrySetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(2));
        await ExpectCancelled(initialization);
    }

    public static async Task FailedExitCanResumeWithNewCancellationScopeAsync()
    {
        using var operations = new MainWindowOperations();
        var original = operations.Token;
        operations.BeginShutdown();
        await operations.DrainAsync();
        operations.Resume();
        Require(original.IsCancellationRequested && !operations.Token.IsCancellationRequested && !operations.IsClosing,
            "retrying after a stop failure needs a fresh UI token while old work remains cancelled");
        var ran = false;
        await operations.RunAsync(() => { ran = true; return Task.CompletedTask; });
        Require(ran, "a failed exit must not permanently disable all account actions");
    }

    public static async Task OperationFailureIsObservedDuringShutdownAsync()
    {
        using var operations = new MainWindowOperations();
        var operation = operations.RunAsync(() => throw new InvalidOperationException("fixture action failed"));
        try { await operation; throw new Exception("expected action failure"); }
        catch (InvalidOperationException exception) when (exception.Message == "fixture action failed") { }
        operations.BeginShutdown();
        await operations.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    public static Task NonFiniteLicenseTimeoutsUseDefaultsAsync()
    {
        var variables = new[]
        {
            RoadhogServiceOptions.LicenseHeartbeatSecondsEnvironmentVariable,
            RoadhogServiceOptions.LicenseHeartbeatRetryDelaySecondsEnvironmentVariable,
            RoadhogServiceOptions.LicenseRequestTimeoutSecondsEnvironmentVariable
        };
        var original = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity" })
            {
                foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, invalid);
                var options = new RoadhogServiceOptions();
                var heartbeat = options.LicenseHeartbeatInterval;
                var retry = options.LicenseHeartbeatRetryDelay;
                var timeout = options.LicenseRequestTimeout;
                options.ApplyEnvironmentOverrides();
                Require(options.LicenseHeartbeatInterval == heartbeat && options.LicenseHeartbeatRetryDelay == retry
                    && options.LicenseRequestTimeout == timeout, "non-finite environment durations must use finite defaults");
            }
        }
        finally { foreach (var pair in original) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
        return Task.CompletedTask;
    }

    public static Task ConfigurationDialogsAndImportCannotOverlapAsync() => RunStaAsync(async () =>
    {
        await using var fixture = new WindowFixture();
        var enter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = InvokeTask(fixture.Form, "RunConfigurationActionAsync", (Func<Task>)(async () =>
        { enter.TrySetResult(); await release.Task; }));
        await enter.Task;
        var secondRan = false;
        try
        {
            await InvokeTask(fixture.Form, "RunConfigurationActionAsync", (Func<Task>)(() =>
            { secondRan = true; return Task.CompletedTask; }));
            throw new Exception("expected overlapping configuration edit rejection");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("正在修改账号配置", StringComparison.Ordinal)) { }
        Require(!secondRan, "an import cannot overwrite settings from a simultaneously open editor");
        release.TrySetResult();
        await first;
        await InvokeTask(fixture.Form, "RunConfigurationActionAsync", (Func<Task>)(() => Task.CompletedTask));
    });

    public static Task MainWindowExitWaitsForTrackedStartupAsync() => RunStaAsync(async () =>
    {
        await using var fixture = new WindowFixture();
        using var hardware = HiddenForm();
        using var authorization = HiddenForm();
        fixture.Form.AddOwnedForm(hardware);
        hardware.AddOwnedForm(authorization);
        var closedWindows = new List<string>();
        var parentClosureWasBlocked = false;
        authorization.FormClosed += (_, _) => closedWindows.Add("authorization");
        hardware.FormClosing += (_, args) =>
        {
            // A hardware page stays busy until its nested authorization window has actually closed.
            if (!authorization.IsDisposed) { parentClosureWasBlocked = true; args.Cancel = true; }
        };
        hardware.FormClosed += (_, _) => closedWindows.Add("hardware");
        hardware.Show();
        authorization.Show();
        var operations = (MainWindowOperations)typeof(MultiAccountForm).GetField("_operations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Form)!;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = operations.Token;
        var cleanupFinished = false;
        var initialization = operations.RunAsync(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); await release.Task; cleanupFinished = true; }
        });
        typeof(MultiAccountForm).GetField("_initializationTask", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Form, initialization);
        var exit = InvokeTask(fixture.Form, "ExitAsync");
        await cancelled.Task;
        var waitedForStartup = !exit.IsCompleted && !cleanupFinished;
        release.TrySetResult();
        await exit;
        await ExpectCancelled(initialization);
        Require(waitedForStartup, "main-window exit must not race past unfinished startup cleanup");
        Require(cleanupFinished, "startup cleanup must finish before the form completes exit");
        Require(!parentClosureWasBlocked && closedWindows.SequenceEqual(new[] { "authorization", "hardware" }),
            "exit must close nested authorization before its busy hardware owner without requiring another user close");
    });

    private static System.Windows.Forms.Form HiddenForm() => new()
    {
        ShowInTaskbar = false, Opacity = 0,
        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
        Location = new System.Drawing.Point(-30000, -30000)
    };

    private sealed class WindowFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "roadhog-window-tests-" + Guid.NewGuid().ToString("N"));
        private readonly MultiAccountWorkspace _workspace;
        public MultiAccountForm Form { get; }
        public WindowFixture()
        {
            _workspace = new(new RoadhogServiceOptions
            {
                AccountConfigPath = Path.Combine(_root, "config", "accounts.json"),
                PathLibraryDirectory = Path.Combine(_root, "paths"), ProfileLibraryDirectory = Path.Combine(_root, "profiles"),
                RadarMapDirectory = Path.Combine(_root, "radar"), LogDirectory = Path.Combine(_root, "logs")
            });
            Form = new MultiAccountForm(_workspace) { ShowInTaskbar = false };
            ((System.Windows.Forms.NotifyIcon)typeof(MultiAccountForm).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Form)!).Visible = false;
        }
        public async ValueTask DisposeAsync()
        {
            Form.Dispose();
            await _workspace.DisposeAsync();
            var root = Path.GetFullPath(_root);
            Require(root.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "roadhog-window-tests-"), StringComparison.OrdinalIgnoreCase), "cleanup must remain in its temporary test root");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static Task InvokeTask(object instance, string name, params object?[] arguments) =>
        (Task)instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments)!;

    private static Task RunStaAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = true;
                var task = test();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (!task.IsCompleted)
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Window robustness test timed out.");
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(1);
                }
                task.GetAwaiter().GetResult();
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task ExpectCancelled(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("expected cancellation");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
