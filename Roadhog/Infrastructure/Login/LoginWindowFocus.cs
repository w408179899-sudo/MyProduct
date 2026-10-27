namespace Roadhog.Infrastructure.Login;

// Input adapter policy. Every switch rebinds the current window before sending keys;
// foreground confirmation still gates all credential and button input.
internal static class LoginWindowFocus
{
    internal static Task EnsureAsync(uint targetPid,Func<uint> foreground,Func<Task> validate,
        Func<Keys[],CancellationToken,Task> switchWindow,Func<CancellationToken,Task> settle,
        Action<int,uint,string>? trace,CancellationToken token) =>
        EnsureAsync(targetPid,()=>Task.FromResult(foreground()),validate,switchWindow,settle,trace,token);

    internal static async Task EnsureAsync(uint targetPid,Func<Task<uint>> foreground,Func<Task> validate,
        Func<Keys[],CancellationToken,Task> switchWindow,Func<CancellationToken,Task> settle,
        Action<int,uint,string>? trace,CancellationToken token)
    {
        for(int attempt=0;attempt<=20;attempt++)
        {
            token.ThrowIfCancellationRequested();
            uint actual=await foreground().ConfigureAwait(false);
            if(actual==targetPid)return;
            if(attempt==20)
                throw new InvalidOperationException($"无法将当前登录窗口切到前台，已停止输入。目标进程 {targetPid}，前台进程 {actual}。");
            await validate().ConfigureAwait(false);
            // Alt+Esc can leave the minimized UU as foreground. Alt+Tab restores a
            // switchable window; Alt+Esc then visits windows beyond the last two.
            bool restore=attempt%4==0;
            trace?.Invoke(attempt+1,actual,restore?"Alt+Tab":"Alt+Esc");
            await switchWindow([Keys.Menu,restore?Keys.Tab:Keys.Escape],token).ConfigureAwait(false);
            await settle(token).ConfigureAwait(false);
        }
    }
}
