using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using SampleProject.Desktop.ViewModels;
using Smart.Hosting;

namespace SampleProject.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _model;
    private readonly JsonLineEventSink _logs;
    private readonly DispatcherTimer _timer;
    private bool _closing, _complete;
    public MainWindow(ShellViewModel model, JsonLineEventSink logs)
    {
        InitializeComponent(); _model = model; _logs = logs; DataContext = model;
        _timer = new(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        { if (IsVisible && WindowState != WindowState.Minimized) model.Refresh(); }, Dispatcher);
        Closing += OnClosing;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_complete) return;
        e.Cancel = true; if (_closing || !_model.ConfirmClose()) return;
        _closing = true; IsEnabled = false; _timer.Stop();
        try { await _model.ShutdownAsync(); await _logs.DisposeAsync(); _complete = true; Close(); }
        catch (Exception ex) { _closing = false; IsEnabled = true; MessageBox.Show(this, ex.Message, "停止尚未完成"); }
    }
}
