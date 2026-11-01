using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login.Native;

namespace Roadhog.Infrastructure.Login;

// Opening the configured shortcut needs desktop evidence, not a readable UU control tree.
internal sealed class UuDesktopOpen
{
    private int _attempted;
    internal static readonly Point Shortcut = new(35, 35);

    internal LoginObservation Capture(Func<LoginObservation> normal, Func<UuDesktopContext?> desktop,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var observation = normal();
            token.ThrowIfCancellationRequested();
            return observation;
        }
        catch (UuWindowReadException)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _attempted) != 0) throw;
            var context = desktop();
            token.ThrowIfCancellationRequested();
            if (context is null) throw;
            return new(new(context.Session, LoginStage.Desktop, LoginAction.OpenAccelerator,
                "open-uu-desktop", "从桌面图标打开 UU 主窗口"), 0, context.SessionPid, "", Shortcut, Rectangle.Empty);
        }
    }

    internal void SuppressAfterLaunch() => Interlocked.Exchange(ref _attempted, 1);

    internal async Task<bool> ExecuteAsync(LoginSnapshot expected,
        Func<CancellationToken, Task<UuDesktopContext?>> readDesktop,
        Func<CancellationToken, Task> showDesktop,
        Func<uint, Point, CancellationToken, Task> move,
        Func<CancellationToken, Task> click,
        Func<int, CancellationToken, Task> delay, Action attempted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _attempted) != 0) return false;
        if (expected.Stage != LoginStage.Desktop || expected.Action != LoginAction.OpenAccelerator)
            throw new InvalidOperationException("当前步骤不是打开 UU，未点击桌面图标。");
        uint? sessionPid = null;
        var initial = await ReadAsync(false).ConfigureAwait(false);
        sessionPid = initial.SessionPid;
        await showDesktop(token).ConfigureAwait(false);
        await delay(600, token).ConfigureAwait(false);
        await ReadAsync(true).ConfigureAwait(false);
        await move(initial.SessionPid, Shortcut, token).ConfigureAwait(false);
        await ReadAsync(true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Reserve before any mouse-down, even if its device acknowledgement is lost.
        if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0) return false;
        attempted();
        await click(token).ConfigureAwait(false);
        await delay(100, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await click(token).ConfigureAwait(false);
        await delay(2000, token).ConfigureAwait(false);
        return true;

        async Task<UuDesktopContext> ReadAsync(bool requireDesktop)
        {
            var context = await readDesktop(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (context is null) throw new InvalidOperationException("游戏或登录窗口已启动，未点击 UU 桌面图标。");
            if (context.Session != expected.Session || context.SessionPid == 0
                || sessionPid is uint original && context.SessionPid != original)
                throw new InvalidOperationException("登录会话已变化，未点击 UU 桌面图标。");
            if (requireDesktop && !context.ForegroundName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目标桌面未取得焦点，未点击 UU 图标。");
            return context;
        }
    }
}

internal sealed record UuDesktopContext(string Session, uint SessionPid, string ForegroundName);
