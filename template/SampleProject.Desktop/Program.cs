namespace SampleProject.Desktop;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new MainForm(args.Contains("--smoke", StringComparer.Ordinal)));
    }
}
