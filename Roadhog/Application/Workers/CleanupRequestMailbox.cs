using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application.Workers;

internal enum CleanupPreparationStage { None, ReturningToTown, Discarding }

public sealed record CleanupRequest(ScriptSettings Settings, bool Manual, bool ResetsCooldown = true, bool AllowNpcSell = true)
{
    // Execution progress belongs to this worker request, never to persisted settings.
    internal CleanupPreparationStage PreparationStage { get; set; }
    internal bool TownReturnCompleted { get; set; }
    internal bool FullCleanupStarted { get; set; }
    internal bool SharedConfigurationLoaded { get; set; }
}

/// <summary>One pending or executing request per worker session. Never survives Stop/Start.</summary>
public sealed class CleanupRequestMailbox
{
    private readonly object sync = new();
    private CleanupRequest? request;
    public CleanupRequest? Current { get { lock (sync) return request; } }
    public OperationResult Request(ScriptSettings settings, bool manual, bool resetsCooldown = true, bool allowNpcSell = true)
    {
        lock (sync)
        {
            if (request != null) return OperationResult.Fail("清包流程已在等待或执行，请勿重复启动。");
            var copy = settings.Clone();
            copy.Maintenance.CleanupWorkflow = copy.Maintenance.CleanupWorkflow.ForTrigger(manual);
            if (copy.Maintenance.CleanupWorkflow.Describe().Length == 0) return OperationResult.Fail("请先在清包页选择执行项目。");
            request = new(copy, manual, resetsCooldown, allowNpcSell); return OperationResult.Ok();
        }
    }
    public void Complete() { lock (sync) request = null; }
}
