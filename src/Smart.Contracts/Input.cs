using System.Collections.Immutable;
namespace Smart.Contracts;

public enum InputOperation { KeyDown, KeyUp, MouseDown, MouseUp, MoveRelative, Scroll, Delay, MoveAbsolute }
[Flags] public enum InputResource { None = 0, Keyboard = 1, Mouse = 2 }
public enum MouseButton { Left = 1, Right = 2, Middle = 3 }

public sealed record InputCommand(InputOperation Operation, int Code = 0, int X = 0, int Y = 0,
    TimeSpan Duration = default)
{
    public static InputCommand PressDown(int hidCode) => new(InputOperation.KeyDown, hidCode);
    public static InputCommand Release(int hidCode) => new(InputOperation.KeyUp, hidCode);
    public static InputCommand Wait(TimeSpan duration) => new(InputOperation.Delay, Duration: duration);
}

public enum InputRetention { ReleaseAfterAction, UntilOwnerChanges }
public sealed record ActionPlan(string Id, InputResource Resources,
    ImmutableArray<InputCommand> Commands, TimeSpan Timeout,
    InputRetention Retention = InputRetention.ReleaseAfterAction, string Owner = "",
    int Priority = 0, IActionPrecondition? Precondition = null, DateTimeOffset? ExpiresAt = null);
// Evaluates the action's current domain conditions using official snapshots captured by its module.
// This is action validation, never a raw-read quality or freshness check.
public interface IActionPrecondition
{
    ValueTask<bool> EvaluateAsync(CancellationToken cancellationToken);
}
public enum ActionState { Running, Succeeded, Cancelled, TimedOut, Failed, Rejected }
public enum ActionFailureKind { None, Precondition, Input }
public sealed record ActionFeedback(string ActionId, ActionState State, string? Detail = null, ActionFailureKind Failure = ActionFailureKind.None);

public interface IInputDevice : IAsyncDisposable
{
    string DeviceId { get; }
    ValueTask SendAsync(InputCommand command, CancellationToken cancellationToken);
    ValueTask ReleaseAllAsync(CancellationToken cancellationToken);
}

public interface IActionExecutor : IAsyncDisposable
{
    ValueTask<ActionFeedback> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken);
    ValueTask ReleaseOwnerAsync(string owner, CancellationToken cancellationToken);
}
