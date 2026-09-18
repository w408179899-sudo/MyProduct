using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.Runtime;

namespace Smart.Hosting.Windows;

// Implement in the project composition root. Register the same typed readers used at runtime;
// the returned delegate obtains character identity exclusively from the supplied official reader.
public interface IProjectCharacterVerification
{
    Func<ISnapshotReader, CancellationToken, ValueTask<VerifiedCharacter>> Register(
        AccountProfile profile, SnapshotCatalog catalog, DmaDispatcher dispatcher, ProcessBinding process);
}

public sealed class AccountProfileVerifier(IProjectCharacterVerification? project = null,
    IHardwareDiagnostics? diagnostics = null) : IAccountProfileVerifier
{
    internal Func<VmmConnectionPool> CreatePool { get; init; } =
        () => new(new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory));

    public async Task<ProfileVerification> VerifyAsync(AccountProfile profile, CancellationToken token)
    {
        profile.Validate(); token.ThrowIfCancellationRequested();
        if (profile.Mode == RuntimeMode.Mock) return new(null, null);
        if (project is null)
            throw new InvalidOperationException("项目尚未接入角色快照验证器，不能保存硬件配置；进程或模块头读取成功不等于读到角色。");
        if (profile.Dma!.Binding is null)
            throw new InvalidOperationException("请先刷新并选择明确的物理 DMA 设备，不能只按设备序号保存。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var pool = CreatePool();
            await using var connection = await pool.AcquireAsync(profile.Dma!, deadline.Token).ConfigureAwait(false);
            var transport = connection.Transport;
            var settings = profile.Dma!;
            var process = await Task.Run(() =>
            {
                var selected = HardwareSessionFactory.Select(transport.ListProcesses(), settings, requireModule: false);
                return transport.GetProcess(selected.ProcessId, settings.ModuleName);
            }, deadline.Token).ConfigureAwait(false);
            if (process.ModuleBase == 0) throw new IOException("目标模块尚未加载。");
            var catalog = new SnapshotCatalog();
            var readCharacter = project.Register(profile, catalog, connection.Dispatcher, process);
            if (catalog.Channels.Count == 0) throw new InvalidOperationException("角色验证必须注册正式快照通道。");
            catalog.Seal();
            await using var snapshots = new SnapshotProvider(catalog).OpenSession(new(transport.DeviceId, transport.ConnectionId,
                profile.Id, Guid.NewGuid().ToString("N"), process.ProcessId, process.ProcessIdentity, process.ModuleIdentity));
            await using var lifetime = new ConnectionSnapshotLifetime(transport, connection, snapshots);
            if (!connection.IsCurrent) throw new IOException("验证连接已经失效。");
            // A new snapshot session cannot reuse a previous account's cached character.
            var character = await readCharacter(snapshots.Reader, deadline.Token).ConfigureAwait(false);
            var result = new ProfileVerification(character, new(process.ProcessId, process.Name, process.ModuleBase));
            result.ValidateFor(profile);
            await (diagnostics ?? new HardwareDiagnostics()).TestInputAsync(profile.Input!, deadline.Token).ConfigureAwait(false);
            var current = await Task.Run(() => transport.GetProcess(process.ProcessId, settings.ModuleName), deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (!connection.IsCurrent || current.ProcessIdentity != process.ProcessIdentity || current.ModuleIdentity != process.ModuleIdentity)
                throw new IOException("验证期间目标进程或模块发生变化，请重新验证。");
            return result;
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("配置验证超时：未在期限内完成角色读取和 KMBox 握手，不能保存。", ex); }
    }
}
