namespace Roadhog
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--account-worker")
            {
                try
                {
                    var spec = System.Text.Json.JsonSerializer.Deserialize<Infrastructure.WorkerProcesses.WorkerLaunchSpec>(
                        File.ReadAllText(args[1]), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidDataException("后台启动配置为空。");
                    Environment.ExitCode = new Infrastructure.WorkerProcesses.WorkerProcessHost().RunAsync(spec).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "worker.json.error"), ex.Message); }
                    catch { }
                    Environment.ExitCode = 12;
                }
                return;
            }
            if (args.Length > 0 && args[0] == "--account-worker") { Environment.ExitCode = 2; return; }
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            Infrastructure.Composition.MultiAccountWorkspace? workspace = null;
            Mutex? singleManager = null;
            var ownsManager = false;
            try
            {
                ApplicationConfiguration.Initialize();
                // An unexpected event-handler failure closes this UI instead of continuing with partially mutated state.
                System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                var options = Infrastructure.Composition.RoadhogServiceOptions.FromEnvironment();
                var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(options.AccountConfigPath).ToUpperInvariant())));
                singleManager = new Mutex(true, @"Local\Roadhog.Manager." + identity, out ownsManager);
                if (!ownsManager)
                {
                    MessageBox.Show("这个配置目录的主界面已打开，请从托盘恢复窗口。", "Roadhog");
                    return;
                }
                if (args.Contains("--legacy-ui"))
                {
                    using var legacy = new Form1();
                    System.Windows.Forms.Application.Run(legacy);
                }
                else
                {
                    workspace = new Infrastructure.Composition.MultiAccountWorkspace(options);
                    using var form = new MultiAccountForm(workspace);
                    System.Windows.Forms.Application.Run(form);
                }
            }
            catch (Exception exception)
            {
                Environment.ExitCode = 1;
                ReportManagerFailure(exception, workspace);
            }
            finally
            {
                // Detach only: a UI failure must not stop otherwise healthy account processes.
                if (workspace is not null)
                {
                    try { workspace.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
                    catch (Exception exception) { WriteManagerFailure(exception, workspace); }
                }
                if (ownsManager) singleManager?.ReleaseMutex();
                singleManager?.Dispose();
            }
        }

        private static void ReportManagerFailure(Exception exception, Infrastructure.Composition.MultiAccountWorkspace? workspace)
        {
            WriteManagerFailure(exception, workspace);
            MessageBox.Show("主界面因错误已关闭。账号后台进程保持独立，可重新打开主界面接管。\n\n" + exception.Message,
                "Roadhog 主界面错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void WriteManagerFailure(Exception exception, Infrastructure.Composition.MultiAccountWorkspace? workspace)
        {
            try
            {
                if (workspace is not null) workspace.Logger.Error("manager.unhandled_failure", exception);
                else
                {
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roadhog");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "manager-startup-errors.log"), DateTimeOffset.Now + " " + exception + Environment.NewLine);
                }
            }
            catch { /* An unavailable log directory must not prevent the failure boundary from closing the UI. */ }
        }
    }
}
