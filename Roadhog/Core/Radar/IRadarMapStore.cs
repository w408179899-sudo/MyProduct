using Roadhog.Core.Common;

namespace Roadhog.Core.Radar;

public interface IRadarMapStore
{
    string DirectoryPath { get; }

    Task<OperationResult<RadarMapLoadResult>> LoadAsync(
        uint mapId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        RadarMapDocument document,
        CancellationToken cancellationToken = default);
}
