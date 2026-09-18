using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SampleProject.Bootstrap;
using SampleProject.Desktop.Services;
using SampleProject.Desktop.ViewModels;
using Smart.Hosting;
using Smart.Hosting.Windows;

namespace SampleProject.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smoke = e.Args.Contains("--smoke", StringComparer.Ordinal);
        using var bindingErrors = new StringWriter();
        using var bindingListener = new TextWriterTraceListener(bindingErrors);
        if (smoke)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingListener);
        }
        var data = Path.Combine(AppContext.BaseDirectory, smoke ? "smoke-" + Guid.NewGuid().ToString("N") : "data");
        var path = Path.Combine(data, "accounts.json");
        var logs = new JsonLineEventSink(Path.Combine(data, "logs"));
        var workspace = new AccountWorkspace(new JsonConfigStore<HostSettings>(path, 1, x => x.Validate()),
            new ProjectHost(logs, leaseDirectory: smoke ? Path.Combine(data, "leases") : null), logs);
        var model = new ShellViewModel(workspace, new HardwareDiagnostics(), new DesktopDialogs(), path,
            ProjectDesktopComposition.CreatePages(), ProjectDesktopComposition.CreateProfileVerifier());
        var window = new MainWindow(model, logs); MainWindow = window;
        if (smoke) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.ShowInTaskbar = false; window.ShowActivated = false; }
        window.Show();
        try
        {
            await model.InitializeAsync();
            if (!smoke) return;
            await model.StartCommand.ExecuteAsync(null);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WaitForRunningAsync(1, deadline.Token);
            await model.PauseCommand.ExecuteAsync(null);
            if (workspace.Accounts.Single().Status.State != SessionState.Paused) throw new InvalidOperationException("Pause failed.");
            await model.StartCommand.ExecuteAsync(null);
            await WaitForRunningAsync(2, deadline.Token);
            model.Refresh();
            var index = Array.IndexOf(e.Args, "--screenshot");
            if (index >= 0 && index + 1 < e.Args.Length)
            {
                var file = Path.GetFullPath(e.Args[index + 1]);
                foreach (var page in model.Pages)
                {
                    model.SelectedPage = page;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var surface = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var target = page.Id == "home" ? file : Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-" + page.Id + ".png");
                    using var output = File.Create(target); encoder.Save(output);
                }
            }
            if (bindingErrors.GetStringBuilder().Length != 0) throw new InvalidOperationException("WPF binding errors: " + bindingErrors);
            await model.StopAllCommand.ExecuteAsync(null);
            if (workspace.Accounts.Any(x => x.Status.State != SessionState.Stopped)) throw new InvalidOperationException("Stop failed.");
            window.Close();
        }
        catch (Exception ex)
        {
            if (smoke) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(data, "failure.txt"), ex.ToString()); }
            else MessageBox.Show(window, ex.Message, "启动未完成");
            window.Close();
        }
        finally { if (smoke) PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingListener); }

        async Task WaitForRunningAsync(long generation, CancellationToken token)
        {
            while (true)
            {
                var status = workspace.Accounts.Single().Status;
                if (status.State == SessionState.Faulted) throw new InvalidOperationException(status.Error ?? "Mock session failed.");
                if (status.State == SessionState.Running && status.Generation >= generation) return;
                await Task.Delay(10, token);
            }
        }
    }
}
