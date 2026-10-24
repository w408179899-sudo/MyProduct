using Roadhog.Core.Common;

namespace Roadhog.Application.Login;

internal enum LoginStage
{
    Desktop, AcceleratorHome, Accelerating, AcceleratorReady, Launcher, PasswordLogin,
    Captcha, CaptchaError, CredentialsRejected, Agreement, ServerSelection,
    CharacterSelection, EntryAgreement, Loading, InWorld, Blocked
}

internal enum LoginAction
{
    None, OpenAccelerator, FocusWindow, Accelerate, LaunchGame, StartLauncher,
    SelectPasswordTab, AcceptLoginProtocol, SubmitCredentials, SolveCaptcha,
    DismissCaptchaError, AcceptAgreement, SelectServer, SelectCharacter,
    EnterCharacter, AcceptEntryAgreement, ConfirmEntryAgreement
}

// Immutable, provider-published facts only. No raw read status or age is exposed.
internal sealed record LoginSnapshot(string Session, LoginStage Stage, LoginAction Action,
    string ActionIdentity, string Description, string CharacterName = "", int GameProcessId = 0);

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
        var transitionStarted = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var snapshot = await session.ReadAsync(deadline.Token).ConfigureAwait(false);
                if (snapshot.Description != lastDescription)
                {
                    lastDescription = snapshot.Description;
                    progress?.Report(snapshot.Description);
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
                    if (snapshot.Action != LoginAction.None && identity != lastDispatched
                        && (!gameAction || !gameActions.Contains(identity)) && snapshot.Session != enteringWorldSession
                        && !(authenticationSubmitted && snapshot.Stage is LoginStage.Launcher or LoginStage.AcceleratorReady))
                    {
                        if (await session.ExecuteAsync(snapshot, snapshot.Action, deadline.Token).ConfigureAwait(false))
                        {
                            if (gameAction) gameActions.Add(identity);
                            if (snapshot.Action == LoginAction.ConfirmEntryAgreement) enteringWorldSession = snapshot.Session;
                            if (snapshot.Action is LoginAction.SubmitCredentials or LoginAction.SolveCaptcha) authenticationSubmitted = true;
                            lastDispatched = identity;
                            transitionStarted.Restart();
                        }
                    }
                    else if (transitionStarted.Elapsed >= _options.TransitionTimeout)
                        return OperationResult<int>.Fail("登录步骤等待超时：" + snapshot.Description + "。可再次点击登录接续。");
                }
                await Task.Delay(_options.PollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return OperationResult<int>.Fail("自动登录超过 10 分钟，已停止输入。可检查当前页面后重新登录。"); }
        finally { await session.ReleaseInputAsync().ConfigureAwait(false); }
    }
}
