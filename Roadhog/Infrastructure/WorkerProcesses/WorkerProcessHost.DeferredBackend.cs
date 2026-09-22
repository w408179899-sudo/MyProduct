using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>Keeps control/status IPC alive while provider construction is pending or blocked in native code.</summary>
internal sealed class DeferredWorkerProcessBackend(WorkerLaunchSpec spec, Func<WorkerLaunchSpec, IWorkerProcessBackend> factory)
    : IWorkerProcessBackend
{
    private Task<IWorkerProcessBackend>? _creation;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Native construction may ignore cancellation. Its resources remain owned by this worker until completion or process exit.
        var creation = Task.Run(() => factory(spec), CancellationToken.None);
        Volatile.Write(ref _creation, creation);
        var backend = await creation.ConfigureAwait(false);
        await backend.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private IWorkerProcessBackend? Ready => Volatile.Read(ref _creation) is { IsCompletedSuccessfully: true } creation ? creation.Result : null;

    public WorkerStatus GetStatus() => Ready?.GetStatus() ?? new WorkerStatus
    {
        InstanceId = spec.Account.InstanceId,
        ProcessId = Environment.ProcessId,
        Authorized = false,
        AuthorizationError = "正在初始化账号后台"
    };

    public Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken) =>
        Ready?.StartAsync(account, cleanupFirst, cancellationToken) ?? Task.FromResult(OperationResult.Fail("账号后台尚未完成初始化。"));

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken) =>
        Ready?.StopAsync(cancellationToken) ?? Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken) =>
        Ready?.ReleaseInputAsync(cancellationToken) ?? Task.FromResult(OperationResult.Ok());

    public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) =>
        Ready?.VerifyHardwareAsync(cancellationToken) ?? Task.FromResult(OperationResult<HardwareVerification>.Fail("账号后台尚未完成初始化。"));

    public Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken) =>
        Ready?.InvokeAsync(method, arguments, progress, cancellationToken) ?? Task.FromException<object?>(new InvalidOperationException("账号后台尚未完成初始化。"));

    public async ValueTask DisposeAsync()
    {
        var creation = Volatile.Read(ref _creation);
        if (creation is null) return;
        IWorkerProcessBackend backend;
        try { backend = await creation.ConfigureAwait(false); }
        catch { return; }
        await backend.DisposeAsync().ConfigureAwait(false);
    }
}
