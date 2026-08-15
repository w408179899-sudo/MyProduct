using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Vmm;

internal sealed class RoadhogSnapshotReaderFactory : IRoadhogSnapshotReaderFactory
#if DEBUG
    , IRoadhogSnapshotDiagnostics
#endif
{
    private readonly IRoadhogGameApi _provider;

    public RoadhogSnapshotReaderFactory(IRoadhogGameApi provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IRoadhogSnapshotReader Create(
        AccountConfig config,
        IRoadhogLogger logger,
        CancellationToken cancellationToken = default)
    {
        return new RoadhogSnapshotReader(config, _provider, logger, cancellationToken);
    }

#if DEBUG
    public Task<OperationResult<IReadOnlyList<GameApiAddressProbeResult>>> ProbeProviderAsync(
        AccountConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return _provider is IRoadhogApiAddressProbe probe
            ? probe.ProbeAddressesAsync(
                new GameApiReadContext(
                    config.AccountName,
                    config.ProcessId,
                    config.TargetProcessName,
                    config.VmmDeviceName),
                cancellationToken)
            : Task.FromResult(
                OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Fail(
                    "Address probe provider is unavailable."));
    }
#endif
}
