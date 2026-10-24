using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Login;

// One physical action attempt. A changed page/focus or an interrupted input is not a
// failed login: release the device, then let the next official scene choose the action.
internal sealed class LoginInputRecovery(string account, IRoadhogLogger? logger = null)
{
    private DateTimeOffset _nextLog;

    internal async Task<bool> ExecuteAsync(Func<Task<bool>> action,
        Func<CancellationToken, Task<OperationResult>> release, CancellationToken token,Func<bool>? awaitingOutcome=null)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Exception exception) when (LoginReadErrors.IsRetryable(exception))
        {
            Log(exception);
            // Do not send another action while a previous key/button may still be down.
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // A final launch/login/entry press may already have reached the client.
                    // After releasing it, wait for the page outcome instead of sending it twice.
                    if ((await release(token).ConfigureAwait(false)).Success) return awaitingOutcome?.Invoke()??false;
                }
                catch (Exception failure) when (LoginReadErrors.IsRetryable(failure)) { Log(failure); }
                await Task.Delay(300, token).ConfigureAwait(false);
            }
        }
    }

    private void Log(Exception exception)
    {
        if (DateTimeOffset.UtcNow < _nextLog) return;
        _nextLog = DateTimeOffset.UtcNow.AddSeconds(10);
        logger?.Warn("login.input_retry", new Dictionary<string, object?>
        { ["account"] = account, ["exceptionType"] = exception.GetType().FullName, ["error"] = exception.Message });
    }
}
