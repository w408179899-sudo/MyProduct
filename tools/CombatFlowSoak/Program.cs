using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.WorkerProcesses;

return await SoakProgram.RunAsync(args);

internal static class SoakProgram
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions CompactJson = new(Json) { WriteIndented = false };
    private const string MarkerName = "combat-flow-soak-isolated.json";
    private const string ReportName = "combat-flow-soak-status.json";
    private const string StopName = "combat-flow-soak-stop.json";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("CombatFlowSoak --root <isolated client root> --account <UUID> [--status | --stop | --run --version Legacy|CoordinatedV2 (--minutes N | --continuous)] [--rollback-on-completion] [--poll-seconds 5] [--startup-seconds 120] [--stop-seconds 60]");
                Console.WriteLine($"Requires {MarkerName}: {{\"Purpose\":\"CombatFlowSoak\",\"InstanceId\":\"UUID\"}}. No command defaults to read-only --status.");
                return 0;
            }
            var options = Options.Parse(args);
            await ValidateMarkerAsync(options);
            if (options.Command == Command.Status)
            {
                var report = await ReadReportAsync(options.Root);
                Emit("status", new { account = options.AccountId, hasReport = report is not null, harnessAlive = report is not null && IsHarnessAlive(report), report });
                return 0;
            }
            if (options.Command == Command.Stop)
            {
                var report = await ReadReportAsync(options.Root)
                    ?? throw new HarnessException("No isolated harness session report exists. This command does not stop ordinary client workers.");
                if (!IsHarnessAlive(report) || report.CompletedAtUtc is not null)
                    throw new HarnessException("The recorded harness session is not running. No stop request was sent.");
                await WriteJsonAsync(Path.Combine(options.Root, StopName), new StopRequest(report.SessionId, options.AccountId));
                Emit("stop_requested", new { report.SessionId, account = options.AccountId });
                return 0;
            }
            return await RunSessionAsync(options, await ValidateRootAsync(options));
        }
        catch (Exception error)
        {
            // Do not serialize launch specs, credentials, RPC tokens, or arbitrary backend error text.
            Emit("harness_failed", new { errorType = error.GetType().Name, message = SafeMessage(error) });
            return 2;
        }
    }

    private static async Task<int> RunSessionAsync(Options options, AccountConfig account)
    {
        var configPath = Path.Combine(options.Root, "config", "accounts.json");
        using var ownership = new ManagerOwnership(configPath);
        var oldReport = await ReadReportAsync(options.Root);
        if (oldReport is not null && IsHarnessAlive(oldReport))
            throw new HarnessException("Another harness session is already running.");

        // Save the requested version before manager initialization can recover persisted intent.
        var policy = await new JsonCombatFlowPolicyStore(JsonCombatFlowPolicyStore.PathFor(configPath)).LoadAsync();
        if (!policy.Success || policy.Value is null)
            throw new HarnessException("Cannot read the isolated rollback policy; no worker was started.");
        if (policy.Value.Resolve(options.Version!.Value) != options.Version.Value)
            throw new HarnessException("ForceLegacy is active in this isolated root. Explicitly change that isolated policy before requesting V2.");
        account.CombatFlowVersion = options.Version.Value;
        var save = await new JsonAccountConfigStore(configPath).SaveAllAsync([account]);
        if (!save.Success) throw new HarnessException("Cannot save the isolated account version; no worker was started.");

        using var process = Process.GetCurrentProcess();
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        var reportPublisher = new ReportFilePublisher();
        var report = new SessionReport
        {
            SessionId = Guid.NewGuid().ToString("N"), InstanceId = options.AccountId,
            HarnessPid = process.Id, HarnessStartedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime()),
            StartedAtUtc = DateTimeOffset.UtcNow, RequestedVersion = options.Version.Value.ToString(),
            RequestedDurationSeconds = options.Continuous ? null : options.Duration.TotalSeconds,
            Continuous = options.Continuous,
            ReportPublishing = reportPublisher.Status,
            RollbackRequested = options.RollbackOnCompletion
        };
        var service = new RoadhogServiceOptions
        {
            AccountConfigPath = configPath,
            PathLibraryDirectory = Path.Combine(options.Root, "config", "paths"),
            ProfileLibraryDirectory = Path.Combine(options.Root, "config", "profiles"),
            RadarMapDirectory = Path.Combine(options.Root, "config", "radar-maps"),
            KmBoxNetConfigPath = Path.Combine(options.Root, "config", "kmbox-net.json"),
            LicenseCredentialPath = Path.Combine(options.Root, "config", "license.dat"),
            OwnerLicenseGrantPath = Path.Combine(options.Root, "config", "owner-license.json"),
            LogDirectory = Path.Combine(options.Root, "logs"),
            EnableLogging = true
        };
        var launch = new WorkerProcessLaunchOptions
        {
            ExecutablePath = Path.Combine(options.Root, "Roadhog.exe"),
            PollInterval = TimeSpan.FromSeconds(1)
            // Keep production DMA leases and authorization checks. Never use a private lease file.
        };
        MultiAccountWorkspace? workspace = null;
        var started = Stopwatch.StartNew();
        var workerPids = new HashSet<int>();
        var previousKills = new Dictionary<int, int>();
        var exitCode = 0;
        using var stopWatcher = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        Task? watching = null;
        try
        {
            await WriteReportAsync(options.Root, report);
            Emit("session_start", new { report.SessionId, account = options.AccountId, report.RequestedVersion, report.RequestedDurationSeconds, report.Continuous });
            watching = WatchStopRequestAsync(options, report, cancel, stopWatcher.Token);
            workspace = new MultiAccountWorkspace(service, launch);
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
            {
                startup.CancelAfter(options.StartupTimeout);
                await workspace.InitializeAsync(startup.Token).WaitAsync(startup.Token);
                var result = await workspace.Processes.StartAsync(options.AccountId, cancellationToken: startup.Token).WaitAsync(startup.Token);
                if (!result.Success) throw new HarnessException("The account manager rejected startup. Inspect the isolated manager log for details.");
            }
            var observation = Stopwatch.StartNew();
            while (options.Continuous || observation.Elapsed < options.Duration)
            {
                cancel.Token.ThrowIfCancellationRequested();
                var view = workspace.Processes.Snapshot().Single(v => SameId(v.Config.InstanceId, options.AccountId));
                Observe(view, report, workerPids, previousKills);
                await PublishReportAsync(options.Root, report, reportPublisher);
                Emit("sample", report.Sample);
                if (view.Worker?.IsRunning == true && view.Worker.ActiveCombatFlowVersion is { } active && active != options.Version)
                    throw new HarnessException("Actual worker version differs from the requested version; stopping the isolated run.");
                var remaining = options.Continuous ? options.PollInterval : options.Duration - observation.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining < options.PollInterval ? remaining : options.PollInterval, cancel.Token);
            }
            Observe(workspace.Processes.Snapshot().Single(v => SameId(v.Config.InstanceId, options.AccountId)),
                report, workerPids, previousKills);
            await PublishReportAsync(options.Root, report, reportPublisher);
            Emit("final_sample", report.Sample);
            var confirmed = report.SawRunningWorker && report.SawRequestedVersion && report.LastSampleRunningAndFresh;
            report.Outcome = confirmed ? "duration_completed" : "running_version_not_confirmed_at_end";
            if (!confirmed) exitCode = 3;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            report.Outcome = "cancelled";
        }
        catch (Exception error)
        {
            report.Outcome = "failed";
            report.FailureType = error.GetType().Name;
            report.FailureMessage = SafeMessage(error);
            exitCode = 3;
        }
        finally
        {
            stopWatcher.Cancel();
            if (watching is not null)
            {
                try { await watching; }
                catch (OperationCanceledException) { }
                catch (Exception error) { report.StopWatcherFailureType = error.GetType().Name; exitCode = 3; }
            }
            if (workspace is not null)
            {
                if (options.RollbackOnCompletion && report.Outcome == "duration_completed" && exitCode == 0 && !cancel.IsCancellationRequested)
                {
                    // Give rollback and fallback Stop separate budgets. A failed rollback must never skip cleanup.
                    using var rollbackDeadline = new CancellationTokenSource(options.StopTimeout);
                    Task? rollbackTask = null;
                    report.RollbackAttempted = true;
                    try
                    {
                        var views = workspace.Processes.Snapshot();
                        if (views.Count != 1 || !SameId(views[0].Config.InstanceId, options.AccountId))
                            throw new HarnessException("Rollback requires exactly the isolated account in the manager.");
                        foreach (var view in views)
                            if (view.WorkerProcessId is { } pid) workerPids.Add(pid);
                        var rollback = workspace.Processes.SetForceLegacyCombatFlowAsync(true, rollbackDeadline.Token);
                        rollbackTask = rollback;
                        var results = await rollback.WaitAsync(rollbackDeadline.Token);
                        report.RollbackStopResultCount = results.Count;
                        report.RollbackAccepted = results.Count == 1 && results.TryGetValue(account.AccountName, out var result) && result.Success;
                        var persisted = await workspace.Processes.CombatFlowPolicy.LoadAsync(rollbackDeadline.Token).WaitAsync(rollbackDeadline.Token);
                        report.RollbackPolicyPersisted = persisted.Success && persisted.Value is { ForceLegacy: true };
                        while (workerPids.Any(pid => IsWorkerAlive(pid, launch.ExecutablePath)))
                            await Task.Delay(100, rollbackDeadline.Token);
                        report.RollbackWorkersExited = workspace.Processes.Snapshot().All(v => !v.DesiredRunning && v.WorkerProcessId is null);
                        if (!report.RollbackAccepted || !report.RollbackPolicyPersisted || !report.RollbackWorkersExited)
                            throw new HarnessException("Rollback did not confirm the isolated account stop and durable ForceLegacy policy.");
                    }
                    catch (Exception error)
                    {
                        report.Outcome = "rollback_failed";
                        report.RollbackFailureType = error.GetType().Name;
                        exitCode = 3;
                    }
                    finally
                    {
                        // WaitAsync bounds observation; a backend stop may still finish during fallback Stop.
                        if (rollbackTask is not null)
                            _ = rollbackTask.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
                using var stopDeadline = new CancellationTokenSource(options.StopTimeout);
                try
                {
                    var views = workspace.Processes.Snapshot();
                    foreach (var view in views)
                        if (view.WorkerProcessId is { } pid) workerPids.Add(pid);
                    // The manager's explicit Stop clears persisted desired intent before disposal.
                    if (views.Any(v => SameId(v.Config.InstanceId, options.AccountId)))
                    {
                        var stopped = await workspace.Processes.StopAsync(options.AccountId).WaitAsync(stopDeadline.Token);
                        report.StopAccepted = stopped.Success;
                    }
                    else report.StopAccepted = true; // Initialization failed before registering any worker.
                    while (workerPids.Any(pid => IsWorkerAlive(pid, launch.ExecutablePath)))
                        await Task.Delay(100, stopDeadline.Token);
                    report.WorkersExited = workspace.Processes.Snapshot().All(v => !v.DesiredRunning && v.WorkerProcessId is null);
                    if (!report.StopAccepted || !report.WorkersExited) exitCode = 4;
                }
                catch (Exception error)
                {
                    report.StopFailureType = error.GetType().Name;
                    report.WorkersExited = false;
                    exitCode = 4;
                }
                try { await workspace.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception error) { report.DisposeFailureType = error.GetType().Name; exitCode = 4; }
                report.RollbackVerified = report.RollbackAttempted && report.RollbackAccepted && report.RollbackPolicyPersisted &&
                    report.RollbackWorkersExited && report.StopAccepted && report.WorkersExited && report.DisposeFailureType is null;
            }
            else report.WorkersExited = true;
            report.CompletedAtUtc = DateTimeOffset.UtcNow;
            report.ElapsedSeconds = Math.Round(started.Elapsed.TotalSeconds, 2);
            report.ExitCode = exitCode;
            try { await PublishReportAsync(options.Root, report, reportPublisher); }
            finally { Console.CancelKeyPress -= handler; }
            Emit("session_finished", report);
        }
        return exitCode;
    }

    private static void Observe(AccountProcessView view, SessionReport report, HashSet<int> pids, Dictionary<int, int> previousKills)
    {
        var now = DateTimeOffset.UtcNow;
        report.PollCount++;
        report.SawRunningWorker |= view.Worker?.IsRunning == true;
        report.SawRequestedVersion |= view.Worker?.IsRunning == true &&
            view.Worker.ActiveCombatFlowVersion?.ToString() == report.RequestedVersion;
        report.LastSampleRunningAndFresh = view.State == "running" && view.Worker?.IsRunning == true &&
            view.Worker.ActiveCombatFlowVersion?.ToString() == report.RequestedVersion &&
            now - view.Worker.ReportedAtUtc < TimeSpan.FromSeconds(10);
        if (!report.LastSampleRunningAndFresh) report.UnhealthySamples++;
        if (view.WorkerProcessId is { } pid)
        {
            pids.Add(pid);
            if (view.Worker?.Snapshot is { } snapshot)
            {
                // An adopted worker may already have kills. Its first sample is the baseline,
                // not evidence of progress during this observation window.
                if (previousKills.TryGetValue(pid, out var old))
                    report.ObservedKills += Math.Max(0, snapshot.KillCount - old);
                previousKills[pid] = snapshot.KillCount;
            }
        }
        var log = new FileInfo(Path.Combine(view.LogDirectory, "latest.log"));
        report.Sample = new
        {
            timestampUtc = now, poll = report.PollCount, state = view.State, desiredRunning = view.DesiredRunning,
            workerPid = view.WorkerProcessId, isRunning = view.Worker?.IsRunning, authorized = view.Worker?.Authorized,
            actualVersion = view.Worker?.ActiveCombatFlowVersion?.ToString(),
            workerReportedAtUtc = view.Worker?.ReportedAtUtc,
            heartbeatAt = view.Worker?.Snapshot?.LastHeartbeatAt, lastKillAt = view.Worker?.Snapshot?.LastKillAt,
            currentWorkerKills = view.Worker?.Snapshot?.KillCount, observedKills = report.ObservedKills,
            distinctWorkers = pids.Count, managerErrorPresent = !string.IsNullOrWhiteSpace(view.Error),
            workerErrorPresent = !string.IsNullOrWhiteSpace(view.Worker?.Snapshot?.LastError),
            logBytes = log.Exists ? (long?)log.Length : null,
            logUpdatedAtUtc = log.Exists ? (DateTime?)log.LastWriteTimeUtc : null
        };
    }

    private static async Task WatchStopRequestAsync(Options options, SessionReport report, CancellationTokenSource cancel, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var path = Path.Combine(options.Root, StopName);
            if (File.Exists(path))
            {
                try
                {
                    var stop = await ReadJsonAsync<StopRequest>(path, token);
                    if (stop?.SessionId == report.SessionId && stop.InstanceId == options.AccountId) { cancel.Cancel(); return; }
                }
                catch (JsonException) { } // Atomic writers prevent this normally; incomplete signals cannot stop another session.
                catch (IOException) { }
            }
            await Task.Delay(250, token);
        }
    }

    private static async Task ValidateMarkerAsync(Options options)
    {
        var markerPath = Path.Combine(options.Root, MarkerName);
        if (!File.Exists(markerPath)) throw new HarnessException("An explicit isolated-root marker is required.");
        var marker = await ReadJsonAsync<RootMarker>(markerPath);
        if (marker?.Purpose != "CombatFlowSoak" || !SameId(marker.InstanceId, options.AccountId))
            throw new HarnessException("The isolated-root marker does not match the requested account.");
    }

    private static async Task<AccountConfig> ValidateRootAsync(Options options)
    {
        var configPath = Path.Combine(options.Root, "config", "accounts.json");
        var loaded = await new JsonAccountConfigStore(configPath).LoadAllAsync();
        if (!loaded.Success || loaded.Value is null || loaded.Value.Count != 1 || !SameId(loaded.Value[0].InstanceId, options.AccountId))
            throw new HarnessException("The isolated accounts file must contain exactly the requested account.");
        var account = loaded.Value[0];
        foreach (var path in new[] { account.LicenseCredentialPath, account.OwnerLicenseGrantPath, account.BagCleanupNameListPath, account.RadarMapDirectory })
            if (!string.IsNullOrWhiteSpace(path) && !IsInside(options.Root, Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(configPath)!, path)))
                throw new HarnessException("An account resource points outside the isolated root. Rebase copied resource paths before running.");
        if (options.Command == Command.Run && !File.Exists(Path.Combine(options.Root, "Roadhog.exe")))
            throw new HarnessException("The isolated root must contain its own Roadhog.exe.");
        return account;
    }

    private static bool IsInside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SameId(string left, string right) => Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b) && a == b;
    private static bool IsHarnessAlive(SessionReport report)
    {
        try { using var p = Process.GetProcessById(report.HarnessPid); return !p.HasExited && Math.Abs((p.StartTime.ToUniversalTime() - report.HarnessStartedAtUtc.UtcDateTime).TotalMilliseconds) < 100; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static bool IsWorkerAlive(int pid, string executable)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited && string.Equals(p.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // Access denial is not evidence of exit and must make shutdown verification fail visibly.
    }
    private static Task WriteReportAsync(string root, SessionReport report) => WriteJsonAsync(Path.Combine(root, ReportName), report);
    private static async Task PublishReportAsync(string root, SessionReport report, ReportFilePublisher publisher)
    {
        if (!await publisher.TryWriteAsync(Path.Combine(root, ReportName), JsonSerializer.Serialize(report, Json)))
            Emit("report_write_failed", new { report.SessionId, publication = publisher.Status });
    }
    private static Task WriteJsonAsync<T>(string path, T value) =>
        AtomicJsonFile.WriteAsync(path, JsonSerializer.Serialize(value, Json));
    private static async Task<SessionReport?> ReadReportAsync(string root)
    {
        var path = Path.Combine(root, ReportName);
        return File.Exists(path) ? await ReadJsonAsync<SessionReport>(path) : null;
    }
    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, token);
    }
    private static void Emit(string kind, object? value) => Console.WriteLine(JsonSerializer.Serialize(new { kind, value }, CompactJson));
    private static string SafeMessage(Exception error) => error is HarnessException or ArgumentException
        ? error.Message : "See the isolated manager/worker logs; exception details are not copied into the harness report.";

    private enum Command { Status, Stop, Run }
    private sealed class HarnessException(string message) : Exception(message);
    private sealed record RootMarker(string Purpose, string InstanceId);
    private sealed record StopRequest(string SessionId, string InstanceId);
    private sealed class SessionReport
    {
        public string SessionId { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public int HarnessPid { get; set; }
        public DateTimeOffset HarnessStartedAtUtc { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public string RequestedVersion { get; set; } = "";
        public double? RequestedDurationSeconds { get; set; }
        public bool Continuous { get; set; }
        public ReportPublicationStatus? ReportPublishing { get; set; }
        public double ElapsedSeconds { get; set; }
        public int PollCount { get; set; }
        public int ObservedKills { get; set; }
        public bool SawRunningWorker { get; set; }
        public bool SawRequestedVersion { get; set; }
        public bool LastSampleRunningAndFresh { get; set; }
        public int UnhealthySamples { get; set; }
        public bool RollbackRequested { get; set; }
        public bool RollbackAttempted { get; set; }
        public int? RollbackStopResultCount { get; set; }
        public bool RollbackAccepted { get; set; }
        public bool RollbackPolicyPersisted { get; set; }
        public bool RollbackWorkersExited { get; set; }
        public bool RollbackVerified { get; set; }
        public string? RollbackFailureType { get; set; }
        public bool StopAccepted { get; set; }
        public bool WorkersExited { get; set; }
        public string Outcome { get; set; } = "starting";
        public string? FailureType { get; set; }
        public string? FailureMessage { get; set; }
        public string? StopFailureType { get; set; }
        public string? StopWatcherFailureType { get; set; }
        public string? DisposeFailureType { get; set; }
        public int? ExitCode { get; set; }
        public object? Sample { get; set; }
    }

    private sealed record Options(string Root, string AccountId, Command Command, CombatFlowVersion? Version,
        TimeSpan Duration, TimeSpan PollInterval, TimeSpan StartupTimeout, TimeSpan StopTimeout, bool RollbackOnCompletion,
        bool Continuous)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            Command? command = null;
            var rollbackOnCompletion = false;
            var continuous = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--run" or "--stop" or "--status")
                {
                    if (command is not null) throw new ArgumentException("Specify exactly one command.");
                    command = args[i] == "--run" ? Command.Run : args[i] == "--stop" ? Command.Stop : Command.Status;
                }
                else if (args[i] == "--rollback-on-completion")
                {
                    if (rollbackOnCompletion) throw new ArgumentException("Duplicate option.");
                    rollbackOnCompletion = true;
                }
                else if (args[i] == "--continuous")
                {
                    if (continuous) throw new ArgumentException("Duplicate option.");
                    continuous = true;
                }
                else
                {
                    if (args[i] is not ("--root" or "--account" or "--version" or "--minutes" or "--poll-seconds" or "--startup-seconds" or "--stop-seconds") || i + 1 == args.Length)
                        throw new ArgumentException("Unknown option or missing value.");
                    if (!values.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate option.");
                }
            }
            if (!values.TryGetValue("--root", out var root) || !Path.IsPathFullyQualified(root)) throw new ArgumentException("--root must be an absolute isolated client directory.");
            if (!values.TryGetValue("--account", out var id) || !Guid.TryParse(id, out var uuid)) throw new ArgumentException("--account must be a UUID.");
            var selected = command ?? Command.Status;
            CombatFlowVersion? version = null;
            if (values.TryGetValue("--version", out var text))
            {
                if (text is not ("Legacy" or "CoordinatedV2")) throw new ArgumentException("--version must be Legacy or CoordinatedV2.");
                version = Enum.Parse<CombatFlowVersion>(text);
            }
            if (selected == Command.Run && (version is null || (!continuous && !values.ContainsKey("--minutes"))))
                throw new ArgumentException("--run requires explicit --version and either --minutes or --continuous.");
            if (continuous && values.ContainsKey("--minutes"))
                throw new ArgumentException("--continuous and --minutes cannot be combined.");
            if (selected != Command.Run && (version is not null || values.ContainsKey("--minutes") || continuous))
                throw new ArgumentException("Version and duration are valid only with --run.");
            if (continuous && rollbackOnCompletion)
                throw new ArgumentException("Continuous runs do not complete automatically; --rollback-on-completion is invalid.");
            if (rollbackOnCompletion && (selected != Command.Run || version != CombatFlowVersion.CoordinatedV2))
                throw new ArgumentException("--rollback-on-completion requires --run --version CoordinatedV2.");
            double Number(string key, double fallback, double minimum, double maximum)
            {
                if (!values.TryGetValue(key, out var raw)) return fallback;
                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result) || result < minimum || result > maximum)
                    throw new ArgumentException(key + " is outside its allowed range.");
                return result;
            }
            return new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), uuid.ToString("D"), selected, version,
                TimeSpan.FromMinutes(Number("--minutes", 0, .01, 1440)), TimeSpan.FromSeconds(Number("--poll-seconds", 5, .25, 60)),
                TimeSpan.FromSeconds(Number("--startup-seconds", 120, 5, 900)), TimeSpan.FromSeconds(Number("--stop-seconds", 60, 5, 180)), rollbackOnCompletion, continuous);
        }
    }

    // Windows mutex ownership is thread-affine. Keep it on a dedicated thread while async work changes threads.
    private sealed class ManagerOwnership : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        public ManagerOwnership(string configPath)
        {
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(configPath).ToUpperInvariant())));
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                try
                {
                    using var mutex = new Mutex(false, @"Local\Roadhog.Manager." + identity);
                    bool owned;
                    try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                    ready.TrySetResult(owned);
                    if (!owned) return;
                    try { _release.Wait(); } finally { mutex.ReleaseMutex(); }
                }
                catch (Exception e) { ready.TrySetException(e); }
            }) { IsBackground = true, Name = "CombatFlowSoak ownership" };
            _thread.Start();
            if (!ready.Task.GetAwaiter().GetResult()) throw new HarnessException("A client manager already owns this isolated configuration. Close it before running the harness.");
        }
        public void Dispose() { _release.Set(); _thread.Join(); _release.Dispose(); }
    }
}
