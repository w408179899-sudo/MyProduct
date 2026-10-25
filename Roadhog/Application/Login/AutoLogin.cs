using Roadhog.Core.Common;

namespace Roadhog.Application.Login;

internal enum LoginStage
{
    Desktop, AcceleratorHome, Accelerating, AcceleratorReady, Launcher, PasswordLogin,
    Captcha, CaptchaError, CredentialsRejected, Agreement, ServerSelection,
    CharacterSelection, EntryAgreement, Loading, InWorld, Blocked, AcceleratorReconnect, Disconnected
}

internal enum LoginAction
{
    None, OpenAccelerator, FocusWindow, Accelerate, LaunchGame, StartLauncher,
    SelectPasswordTab, AcceptLoginProtocol, SubmitCredentials, SolveCaptcha,
    DismissCaptchaError, AcceptAgreement, SelectServer, SelectCharacter,
    EnterCharacter, AcceptEntryAgreement, ConfirmEntryAgreement, Reaccelerate, ConfirmGameExit
}

// Immutable, provider-published facts only. No raw read status or age is exposed.
internal sealed record LoginSnapshot(string Session, LoginStage Stage, LoginAction Action,
    string ActionIdentity, string Description, string CharacterName = "", int GameProcessId = 0, int GameProcessCount = 0);

internal interface IAutoLoginSession : IAsyncDisposable
{
    Task<LoginSnapshot> ReadAsync(CancellationToken cancellationToken);
    // The adapter revalidates the action's process, window, foreground and control before input.
    Task<bool> ExecuteAsync(LoginSnapshot snapshot, LoginAction action, CancellationToken cancellationToken);
    Task ReleaseInputAsync();
}

internal sealed class AutoLoginOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan TransitionTimeout { get; init; } = TimeSpan.FromSeconds(100);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(800);
    public TimeSpan ActionRetryInterval { get; init; } = TimeSpan.FromSeconds(3);
}

internal sealed class AutoLoginRunner(AutoLoginOptions? options = null)
{
    private readonly AutoLoginOptions _options = options ?? new();

    public async Task<OperationResult<int>> RunAsync(IAutoLoginSession session, string expectedCharacter,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedCharacter))
            return OperationResult<int>.Fail("请先在设备/角色中保存目标角色。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        string? lastDispatched = null, lastDescription = null;
        var gameActions = new HashSet<string>(StringComparer.Ordinal);
        string? enteringWorldSession = null;
        bool authenticationSubmitted = false;
        bool gameLaunchRequested = false;
        bool reconnectRequested = false;
        bool waitingForGameExit = false;
        LoginStage? lastStage = null;
        var actionStarted = System.Diagnostics.Stopwatch.StartNew();
        var transitionStarted = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var snapshot = await session.ReadAsync(deadline.Token).ConfigureAwait(false);
                if (waitingForGameExit)
                {
                    if (snapshot.GameProcessCount > 0)
                    {
                        const string waiting = "已确认离线提示，等待游戏进程退出…";
                        if (lastDescription != waiting) { lastDescription = waiting; progress?.Report(waiting); }
                        if (transitionStarted.Elapsed >= _options.TransitionTimeout)
                            return OperationResult<int>.Fail("登录步骤等待超时：" + waiting + "。可再次点击登录接续。");
                        await Task.Delay(_options.PollInterval, deadline.Token).ConfigureAwait(false);
                        continue;
                    }
                    // Only a published, complete process inventory proving Aion absent
                    // allows a new login attempt. Old world data and UU popups cannot.
                    waitingForGameExit = false;
                    progress?.Report("游戏进程已退出，接续自动登录");
                    lastDescription = null;
                    gameLaunchRequested = authenticationSubmitted = reconnectRequested = false;
                    enteringWorldSession = lastDispatched = null;
                    gameActions.Clear();
                    lastStage = null;
                    transitionStarted.Restart();
                }
                // A verified reconnect popup can interrupt any login stage. Reset launch guards
                // only after the provider confirms that no game/passport/launcher remains.
                if (reconnectRequested && snapshot.Stage is LoginStage.Desktop or LoginStage.AcceleratorHome
                    or LoginStage.Accelerating or LoginStage.AcceleratorReady)
                {
                    gameLaunchRequested = authenticationSubmitted = false;
                    enteringWorldSession = null;
                    gameActions.Clear();
                    reconnectRequested = false;
                }
                // UU normally minimizes before the launcher process exists. Once launch input
                // is sent, process/foreground changes must not send this attempt back to UU.
                bool waitingForGameWindow = gameLaunchRequested && (snapshot.Stage is
                    LoginStage.Desktop or LoginStage.AcceleratorHome or LoginStage.Accelerating or LoginStage.AcceleratorReady);
                // A confirmed page transition is progress even when that page has no button yet.
                if (!waitingForGameWindow && lastStage != snapshot.Stage)
                {
                    lastStage = snapshot.Stage;
                    transitionStarted.Restart();
                }
                var description = waitingForGameWindow
                    ? "已点击启动游戏，等待启动器或游戏窗口出现…"
                    : snapshot.Description;
                if (description != lastDescription)
                {
                    lastDescription = description;
                    progress?.Report(description);
                }
                if (snapshot.Stage == LoginStage.InWorld)
                {
                    if (!string.Equals(snapshot.CharacterName, expectedCharacter, StringComparison.Ordinal))
                        return OperationResult<int>.Fail("当前在线角色与配置的角色不一致，已停止登录。");
                    return OperationResult<int>.Ok(snapshot.GameProcessId);
                }
                else
                {
                    if (snapshot.Stage is LoginStage.Blocked or LoginStage.CredentialsRejected)
                        return OperationResult<int>.Fail(snapshot.Description);
                    var identity = snapshot.Session + ":" + snapshot.ActionIdentity;
                    bool gameAction = snapshot.Stage is LoginStage.Agreement or LoginStage.ServerSelection or LoginStage.CharacterSelection or LoginStage.EntryAgreement;
                    // Only retry controls whose current state proves the operation is still
                    // needed. Starting clients, submitting login and entering a server/world
                    // wait for their outcomes instead of receiving duplicate input.
                    bool retryControl = snapshot.Action is LoginAction.OpenAccelerator or LoginAction.SelectPasswordTab
                        or LoginAction.AcceptLoginProtocol or LoginAction.DismissCaptchaError or LoginAction.AcceptAgreement
                        or LoginAction.SelectCharacter or LoginAction.AcceptEntryAgreement or LoginAction.Reaccelerate;
                    bool retryDue = retryControl && actionStarted.Elapsed >= _options.ActionRetryInterval;
                    if (!waitingForGameWindow && snapshot.Action != LoginAction.None && (identity != lastDispatched || retryDue)
                        && (!gameAction || !gameActions.Contains(identity) || retryDue)
                        && (snapshot.Action is LoginAction.Reaccelerate or LoginAction.ConfirmGameExit || snapshot.Session != enteringWorldSession)
                        && !(authenticationSubmitted && snapshot.Stage is LoginStage.Launcher or LoginStage.AcceleratorReady))
                    {
                        if (await session.ExecuteAsync(snapshot, snapshot.Action, deadline.Token).ConfigureAwait(false))
                        {
                            if (gameAction) gameActions.Add(identity);
                            if (snapshot.Action == LoginAction.ConfirmEntryAgreement) enteringWorldSession = snapshot.Session;
                            if (snapshot.Action is LoginAction.SubmitCredentials or LoginAction.SolveCaptcha) authenticationSubmitted = true;
                            if (snapshot.Action == LoginAction.LaunchGame) gameLaunchRequested = true;
                            if (snapshot.Action == LoginAction.Reaccelerate) reconnectRequested = true;
                            if (snapshot.Action == LoginAction.ConfirmGameExit) waitingForGameExit = true;
                            lastDispatched = identity;
                            actionStarted.Restart();
                            transitionStarted.Restart();
                        }
                    }
                    if (transitionStarted.Elapsed >= _options.TransitionTimeout)
                        return OperationResult<int>.Fail("登录步骤等待超时：" + description + "。可再次点击登录接续。");
                }
                await Task.Delay(_options.PollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return OperationResult<int>.Fail("自动登录超过 10 分钟，已停止输入。可检查当前页面后重新登录。"); }
        finally { await session.ReleaseInputAsync().ConfigureAwait(false); }
    }
}
