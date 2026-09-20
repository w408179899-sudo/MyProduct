using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application.Workers;

public sealed record CleanupRequest(ScriptSettings Settings, bool Manual, bool ResetsCooldown = true);

/// <summary>One pending or executing request per worker session. Never survives Stop/Start.</summary>
public sealed class CleanupRequestMailbox
{
    private readonly object sync = new();
    private CleanupRequest? request;
    public CleanupRequest? Current { get { lock (sync) return request; } }
    public OperationResult Request(ScriptSettings settings, bool manual, bool resetsCooldown = true)
    {
        lock (sync)
        {
            if (request != null) return OperationResult.Fail("清包流程已在等待或执行，请勿重复启动。");
            var copy = settings.Clone();
            copy.Maintenance.CleanupWorkflow = copy.Maintenance.CleanupWorkflow.ForTrigger(manual);
            if (copy.Maintenance.CleanupWorkflow.Describe().Length == 0) return OperationResult.Fail("请先在清包页选择执行项目。");
            request = new(copy, manual, resetsCooldown); return OperationResult.Ok();
        }
    }
    public void Complete() { lock (sync) request = null; }
}
