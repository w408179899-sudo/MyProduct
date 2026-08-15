using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Core.Api;

/// <summary>
/// Creates the only business-facing view of externally read game data.
/// Raw providers stay behind the infrastructure boundary.
/// </summary>
public interface IRoadhogSnapshotReaderFactory
{
    IRoadhogSnapshotReader Create(
        AccountConfig config,
        IRoadhogLogger logger,
        CancellationToken cancellationToken = default);
}

#if DEBUG
internal interface IRoadhogSnapshotDiagnostics
{
    Task<OperationResult<IReadOnlyList<GameApiAddressProbeResult>>> ProbeProviderAsync(
        AccountConfig config,
        CancellationToken cancellationToken = default);
}
#endif
