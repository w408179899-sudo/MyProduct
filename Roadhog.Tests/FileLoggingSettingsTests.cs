using System.Reflection;
using System.Text.Json;
using Roadhog;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Profiles;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class FileLoggingSettingsTests
{
    public static async Task DefaultsAndPersistenceAsync()
    {
        var root = NewRoot();
        try
        {
            var accountPath = Path.Combine(root, "accounts.json");
            await File.WriteAllTextAsync(accountPath, "{\"Accounts\":[{\"AccountName\":\"account1\",\"ScriptSettings\":{}}]}");
            var accounts = new JsonAccountConfigStore(accountPath);
            var account = (await accounts.LoadAllAsync()).Value!.Single();
            Require(account.ScriptSettings!.GenerateLogFiles, "old account JSON must default to logging enabled");
            var profiles = new JsonScriptProfileStore(Path.Combine(root, "profiles"));
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            await File.WriteAllTextAsync(Path.Combine(root, "profiles", "old.json"), "{\"Name\":\"old\",\"Settings\":{}}");
            Require((await profiles.LoadAsync("old")).Value!.Settings.GenerateLogFiles,
                "old profiles must default to logging enabled");
            account.ScriptSettings.GenerateLogFiles = false;
            Require((await accounts.UpsertAsync(account)).Success, "disabled setting saves to account");
            Require((await profiles.SaveAsync(new ScriptProfileDocument
                { Name = "disabled", Settings = account.ScriptSettings.Clone() })).Success, "disabled setting saves to profile");
            Require(!(await accounts.LoadAllAsync()).Value!.Single().ScriptSettings!.GenerateLogFiles,
                "disabled setting survives account save, load and cloning");
            Require(!(await profiles.LoadAsync("disabled")).Value!.Settings.GenerateLogFiles,
                "disabled setting survives profile save, load and cloning");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static Task FileWritesAndIsolationAsync()
    {
        var root = NewRoot();
        try
        {
            var directory = Path.Combine(root, "account1");
            var logger = new FileRoadhogLogger(directory, enabled: false);
            WriteAllLevels(logger);
            Require(!Directory.Exists(directory), "disabled logging must not create a directory or any log file");
            logger.SetFileLoggingEnabled(true);
            logger.Info("vmm.player.read");
            var before = Snapshot(directory);
            Require(before.Count == 2, "enabled logging must write dated and latest logs");
            logger.SetFileLoggingEnabled(false);
            WriteAllLevels(logger);
            Require(before.SequenceEqual(Snapshot(directory)), "disabled logging preserves all existing file contents");
            var other = new FileRoadhogLogger(Path.Combine(root, "account2"));
            other.Info("other.account");
            Require(Directory.Exists(Path.Combine(root, "account2")), "disabling one account must not stop another logger");
            logger.SetFileLoggingEnabled(true);
            logger.Info("vmm.player.read");
            var latest = File.ReadAllText(Path.Combine(directory, "latest.log"));
            Require(latest.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length == 2,
                "reenabling logging must resume immediately even for a sampled event");
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    public static Task StartupAndRuntimeAsync()
    {
        var root = NewRoot();
        try
        {
            foreach (var useOverride in new[] { true, false })
            {
                var directory = Path.Combine(root, useOverride ? "worker" : "legacy");
                Directory.CreateDirectory(directory);
                var account = new AccountConfig { AccountName = "account1", ScriptSettings = new() { GenerateLogFiles = false } };
                var accountPath = Path.Combine(directory, "accounts.json");
                Require(new JsonAccountConfigStore(accountPath).UpsertAsync(account).GetAwaiter().GetResult().Success,
                    "startup account fixture saves");
                var options = new RoadhogServiceOptions
                {
                    UseMockGameApi = true, AccountOverride = useOverride ? account : null,
                    AccountConfigPath = accountPath, LogDirectory = Path.Combine(directory, "logs"),
                    KmBoxNetConfigPath = Path.Combine(directory, "kmbox.json"),
                    LicenseCredentialPath = Path.Combine(directory, "license.dat"),
                    OwnerLicenseGrantPath = Path.Combine(directory, "owner.json"),
                    PathLibraryDirectory = Path.Combine(directory, "paths"),
                    ProfileLibraryDirectory = Path.Combine(directory, "profiles"),
                    RadarMapDirectory = Path.Combine(directory, "maps")
                };
                using var services = RoadhogServices.Create(options);
                services.Logger.Warn("startup.disabled");
                Require(!Directory.Exists(options.LogDirectory), "disabled startup config suppresses even composition logs");
                var dispatcher = new RuntimeRpcDispatcher(services.Runtime, "account1", "");
                ApplyRemote(false);
                ApplyRemote(true);
                services.Logger.Info("runtime.enabled");
                var before = Snapshot(options.LogDirectory);
                ApplyRemote(false);
                services.Logger.Error("runtime.disabled", new Exception("test"));
                Require(before.SequenceEqual(Snapshot(options.LogDirectory)), "runtime notification must stop file writes");

                void ApplyRemote(bool enabled) => dispatcher.InvokeAsync(nameof(IRoadhogRuntime.ApplyFileLoggingSettings),
                    new[] { JsonSerializer.SerializeToElement("account1"), JsonSerializer.SerializeToElement(enabled) },
                    new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    public static Task SummaryUiAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = NewRoot();
            try
            {
                var config = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "account1" });
                var profiles = new InMemoryScriptProfileStore();
                var logger = new FileRoadhogLogger(Path.Combine(root, "logs"));
                var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
                using var form = CreateForm();
                var checkbox = Checkbox(form);
                Require(checkbox.Checked && checkbox.Text == "生成日志文件", "summary checkbox must be checked by default");
                Require(checkbox.Bottom <= checkbox.Parent!.ClientSize.Height, "checkbox must fit in its section");
                logger.Info("before.save");
                var before = Snapshot(Path.Combine(root, "logs"));
                checkbox.Checked = false;
                Save(form);
                logger.Warn("after.disabled.save");
                Require(before.SequenceEqual(Snapshot(Path.Combine(root, "logs"))), "successful UI save disables live file writes");
                var account = config.LoadAllAsync().GetAwaiter().GetResult().Value!.Single();
                Require(!account.ScriptSettings!.GenerateLogFiles, "UI must persist the disabled account setting");
                Require(!profiles.LoadAsync(account.ProfileName).GetAwaiter().GetResult().Value!.Settings.GenerateLogFiles,
                    "UI must persist the disabled profile setting");
                using var reopened = CreateForm();
                Require(!Checkbox(reopened).Checked, "reopening settings must keep the saved unchecked state");
                Checkbox(reopened).Checked = true;
                Save(reopened);
                logger.Info("after.enabled.save");
                Require(File.ReadAllText(Path.Combine(root, "logs", "latest.log")).Contains("after.enabled.save"),
                    "successful UI save reenables live file writes");
                reopened.ShowInTaskbar = false;
                reopened.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                reopened.Location = new System.Drawing.Point(-20000, -20000);
                reopened.Show();
                System.Windows.Forms.Application.DoEvents();
                Require(Checkbox(reopened).Visible, "logging checkbox must be visible on the summary page");
                using var bitmap = new System.Drawing.Bitmap(reopened.Width, reopened.Height);
                reopened.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, reopened.Size));
                var preview = Environment.GetEnvironmentVariable("ROADHOG_LOGGING_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(preview)) bitmap.Save(preview);

                AccountSettingsForm CreateForm() => new("account1", runtime, config, new InMemorySharedPathStore(),
                    profiles, new RecordingFolderLauncher(), Path.Combine(root, "paths"));
            }
            catch (Exception exception) { failure = exception; }
            finally { Directory.Delete(root, recursive: true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }

    private static RoundedCheckBox Checkbox(AccountSettingsForm form) =>
        (RoundedCheckBox)typeof(AccountSettingsForm).GetField("generateLogFilesCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void Save(AccountSettingsForm form)
    {
        var args = new object?[] { null };
        var success = (bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args)!;
        Require(success, "UI save failed: " + args[0]);
    }

    private static void WriteAllLevels(FileRoadhogLogger logger)
    {
        logger.Info("disabled.info");
        logger.Warn("disabled.warn");
        logger.Error("disabled.error", new InvalidOperationException("test"));
    }

    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*.log")
        .OrderBy(path => path, StringComparer.Ordinal).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoadhogFileLogging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
