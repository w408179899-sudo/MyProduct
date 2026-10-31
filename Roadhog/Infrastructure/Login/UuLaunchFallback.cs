using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login.Native;

namespace Roadhog.Infrastructure.Login;

// Explicit fixed-position recovery, owned by the adapter. It does not claim that
// failed control reads proved a launch button exists. Only independent window
// evidence may authorize this one attempt; ordinary readers still hold snapshots.
internal sealed class UuLaunchFallback(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private string? _session;
    private long? _failureStarted;
    private int _launchAttempted;
    internal const string Identity = "uu-fixed-launch:";

    internal static bool IsFallback(LoginSnapshot snapshot) =>
        snapshot.Action == LoginAction.LaunchGame && snapshot.ActionIdentity.StartsWith(Identity, StringComparison.Ordinal);

    internal LoginObservation Capture(string session, Func<LoginObservation> normal,
        Func<LoginObservation?> windowOnly, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_session != session) { _session = session; _failureStarted = null; }
        try
        {
            var observation = normal();
            token.ThrowIfCancellationRequested();
            _failureStarted = null;
            return observation;
        }
        catch (UuWindowReadException)
        {
            token.ThrowIfCancellationRequested();
            _failureStarted ??= _time.GetTimestamp();
            if (Volatile.Read(ref _launchAttempted) == 0 &&
                _time.GetElapsedTime(_failureStarted.Value) >= TimeSpan.FromSeconds(30))
            {
                var target = windowOnly();
                token.ThrowIfCancellationRequested();
                if (target is not null) return target;
            }
            throw;
        }
        catch
        {
            _failureStarted = null;
            throw;
        }
    }

    // Reserve before sending mouse-down, including an uncertain device response.
    // A process replacement or a later read cannot authorize a second blind click.
    internal bool TryReserveAttempt(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Interlocked.CompareExchange(ref _launchAttempted, 1, 0) == 0;
    }

    internal void SuppressAfterNormalLaunch() => Interlocked.Exchange(ref _launchAttempted, 1);
}
