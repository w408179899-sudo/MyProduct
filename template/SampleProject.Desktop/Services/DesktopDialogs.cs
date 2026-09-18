using Microsoft.Win32;
using System.Windows;

namespace SampleProject.Desktop.Services;

public interface IDesktopDialogs
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string name);
    bool Confirm(string message);
}
public sealed class DesktopDialogs : IDesktopDialogs
{
    public string? OpenFile(string title, string filter)
    { var dialog = new OpenFileDialog { Title = title, Filter = filter }; return dialog.ShowDialog() == true ? dialog.FileName : null; }
    public string? SaveFile(string title, string filter, string name)
    { var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = name }; return dialog.ShowDialog() == true ? dialog.FileName : null; }
    public bool Confirm(string message) => MessageBox.Show(message, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
