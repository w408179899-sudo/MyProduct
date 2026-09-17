using SampleProject.Application;
using Smart.Adapters.Dma;
using Smart.Data;
namespace SampleProject.Infrastructure;

// Project-specific decoding and validation live here. Both modes publish the same immutable contracts.
public static class ProjectReaders
{
    public static ProjectChannels RegisterMock(SnapshotCatalog catalog) => new();
    public static ProjectChannels RegisterHardware(SnapshotCatalog catalog, DmaDispatcher dispatcher, ProcessBinding process) => new();
}
