using SampleProject.Desktop.ViewModels;
namespace SampleProject.Desktop;

public static class ProjectDesktopComposition
{
    // Implement IProjectCharacterVerification in Bootstrap, reusing ProjectReaders' typed
    // channels. The empty template intentionally cannot approve a hardware profile.
    public static Smart.Hosting.IAccountProfileVerifier CreateProfileVerifier() => new Smart.Hosting.Windows.AccountProfileVerifier();
    // Register project pages here; add a matching WPF DataTemplate in App.xaml.
    // Page view models consume project services and official snapshots, never raw hardware readers.
    public static IEnumerable<DesktopPage> CreatePages() => [];
}
