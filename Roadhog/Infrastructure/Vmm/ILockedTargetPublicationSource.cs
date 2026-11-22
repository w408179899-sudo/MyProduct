using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal interface ILockedTargetPublicationSource
{
    Task<OperationResult<PublishedGameSnapshot<LockedTargetSnapshot>>> ReadLockedTargetPublicationAsync(
        GameApiReadContext context, long afterVersion, CancellationToken cancellationToken);

    Task<OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>> ReadLockedCombatTargetPublicationAsync(
        GameApiReadContext context, CancellationToken cancellationToken);
}
