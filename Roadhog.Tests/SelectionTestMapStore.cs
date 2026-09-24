using Roadhog.Core.Common;
using Roadhog.Core.Radar;

internal sealed class SelectionTestMapStore(
    Func<uint, CancellationToken, Task<OperationResult<RadarMapLoadResult>>> load) : IRadarMapStore
{
    public string DirectoryPath => "selection-test";
    public Task<OperationResult<RadarMapLoadResult>> LoadAsync(uint mapId, CancellationToken token = default) => load(mapId, token);
    public Task<OperationResult> SaveAsync(RadarMapDocument document, CancellationToken token = default) =>
        throw new NotSupportedException();
}
