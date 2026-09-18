namespace SampleProject.Desktop.ViewModels;

public sealed record DesktopPage(string Id, string Title, string Symbol, object Content);
public sealed record HomePage(ShellViewModel Shell);
public sealed record SettingsPage(ShellViewModel Shell);
public sealed record DiagnosticsPage(ShellViewModel Shell);
