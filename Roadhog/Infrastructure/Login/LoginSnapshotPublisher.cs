using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.Login;

// Atomic scene: target geometry, action identity and stage must belong to the same process/window.
// They cannot be independently merged across a page transition. Optional diagnostic fields are not published.
internal sealed class LoginSnapshotPublisher(GameApiReadContext context, IRoadhogLogger? logger = null) : IDisposable
{
    private readonly DmaStableSnapshotStore _store=new(AionVmmSnapshotChannels.Registry);
    private readonly DmaSnapshotReadCoordinator _coordinator=new();
    private string? _session;
    private string? _lastFailure;
    private DateTimeOffset _nextFailureLog;

    internal LoginObservation? Capture(Func<Action<string>,LoginObservation> capture,
        Action<LoginObservation>? bindInputTarget = null) =>
        _coordinator.Execute(context.AccountName,AionVmmSnapshotChannels.Login,()=>
        {
            try
            {
                var value=capture(key=>
                {
                    if(key==_session)return;
                    if(_session is not null)_store.ClearSession(_session);
                    _session=key;
                });
                if(_session is null)throw new InvalidOperationException("Login source must establish its lifecycle before capture.");
                _store.Resolve(_session,AionVmmSnapshotChannels.Login,context,OperationResult<LoginObservation>.Ok(value),DateTimeOffset.UtcNow);
                _lastFailure=null;
                // Only the completed capture may bind a physical input target. Returning
                // the official held scene below must never authorize clicking old geometry.
                bindInputTarget?.Invoke(value);
                return value;
            }
            catch(Exception exception) when(LoginReadErrors.IsRetryable(exception))
            {
                // VmmPdb throws VmmException while its asynchronous symbol initialization is pending.
                // Keep it below the provider boundary, just like partial/failed native reads.
                var failure=exception.GetType().FullName+":"+exception.Message;
                var now=DateTimeOffset.UtcNow;
                if(failure!=_lastFailure||now>=_nextFailureLog)
                {
                    _lastFailure=failure;_nextFailureLog=now.AddSeconds(10);
                    logger?.Warn("vmm.login.read_retry",new Dictionary<string,object?>
                    { ["account"]=context.AccountName,["device"]=context.VmmDeviceName,
                        ["exceptionType"]=exception.GetType().FullName,["error"]=exception.Message });
                }
                if(_session is null)return null;
                var result=_store.Resolve(_session,AionVmmSnapshotChannels.Login,context,OperationResult<LoginObservation>.Fail(exception.GetType().Name),DateTimeOffset.UtcNow);
                return result.Result.Success?result.Result.Value:null;
            }
        });
    public void Dispose()=>_coordinator.Execute(context.AccountName,AionVmmSnapshotChannels.Login,()=>
    {if(_session is not null)_store.ClearSession(_session);_session=null;return true;});
}
