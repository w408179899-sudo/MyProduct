using Roadhog.Core.Common;

namespace Roadhog.Core.Input;

public interface IKeyboardInput
{
    Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default);
}
