using Roadhog.Core.Common;

namespace Roadhog.Core.Input;

public interface IKeyboardInput
{
    Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default);

    Task<OperationResult> KeyDownAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<OperationResult> KeyUpAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MouseDownAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MouseUpAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MoveMouseRelativeAsync(
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default);
}

public interface IInputStateReset
{
    Task<OperationResult> ReleaseAllAsync(
        CancellationToken cancellationToken = default);
}

public enum RoadhogMouseButton
{
    Left,
    Right,
    Middle,
    Side1,
    Side2
}
