using Smart.Adapters.Dma;
using Smart.Hosting;
using SampleProject.Application;
using SampleProject.Infrastructure;
namespace SampleProject.Bootstrap;

// Called once per account session, including reconnect. Keep typed channel tokens in this local scope.
public static class ProjectComposition
{
    public static void ConfigureMock(SessionComposition composition) =>
        RegisterModules(composition, ProjectReaders.RegisterMock(composition.Channels));
    public static void ConfigureHardware(SessionComposition composition, DmaDispatcher dispatcher, ProcessBinding process) =>
        RegisterModules(composition, ProjectReaders.RegisterHardware(composition.Channels, dispatcher, process));
    private static void RegisterModules(SessionComposition composition, ProjectChannels channels)
    {
        // AddModule with typed settings, declared channel IDs, and Application factories. Shared by Mock and Hardware.
    }
}
