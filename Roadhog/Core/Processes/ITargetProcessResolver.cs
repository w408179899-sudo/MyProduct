using Roadhog.Core.Common;

namespace Roadhog.Core.Processes;

public interface ITargetProcessResolver
{
    string ResolveTargetProcessName(string? overrideProcessName = null);

    IReadOnlyList<TargetProcessInfo> ListTargets(string? overrideProcessName = null);

    OperationResult<ProcessBinding> BindByPid(string accountName, int processId, string? overrideProcessName = null);

    OperationResult<ProcessBinding> TryAutoBind(string accountName, string? overrideProcessName = null);
}
