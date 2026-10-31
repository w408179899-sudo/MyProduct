using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.GameSessions;

internal sealed class VmmGameProcessSnapshotSource : IGameProcessSnapshotSource, IDisposable
{
    private readonly GameProcessSnapshotPublisher _publisher;
    private readonly GameExitOperationExecutor _reads = new(TimeSpan.FromSeconds(5));
    private readonly Func<Action<string>, GameProcessSnapshot> _capture;

    internal VmmGameProcessSnapshotSource(AionVmmGameApi api, AccountConfig account, IRoadhogLogger logger)
        : this(new(new(account.AccountName, 0, "Aion.bin", account.VmmDeviceName, true), logger),
            lifecycle => api.WithLoginConnection(account.VmmDeviceName, vmm => Capture(vmm, account, lifecycle))) { }

    internal VmmGameProcessSnapshotSource(GameProcessSnapshotPublisher publisher, Func<Action<string>, GameProcessSnapshot> capture)
    { _publisher = publisher; _capture = capture; }

    public async Task<GameProcessSnapshot> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            GameProcessSnapshot? value;
            try
            {
                value = await _reads.RunAsync(t => _publisher.Capture(_capture, t), "游戏进程观测", token).ConfigureAwait(false);
            }
            catch (TimeoutException) { value = _publisher.ReadPublished(token); }
            if (value != null) return value;
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    private static GameProcessSnapshot Capture(Vmmsharp.Vmm vmm, AccountConfig account, Action<string> lifecycle)
    {
        // Connection/account scope survives game exit: a valid empty inventory replaces the prior one.
        // A new connection clears the old publication before any reads can fail.
        var session = account.InstanceId + ":" + account.VmmDeviceName + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vmm);
        lifecycle(session);
        GameProcessIdentity[] Inventory() => LoginNativeReader.Inventory(vmm)
            .Where(p => p.Info.sName.Equals("Aion.bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Info.dwPID)
            .Select(p => new GameProcessIdentity(checked((int)p.Info.dwPID),
                $"{p.Info.dwPID}:{p.Info.vaEPROCESS:X}:{p.Info.vaPEB:X}:{p.Info.fWow64}")).ToArray();
        var first = Inventory();
        return ValidateCapture(session, first, Inventory());
    }

    internal static GameProcessSnapshot ValidateCapture(string session, GameProcessIdentity[] first, GameProcessIdentity[] second)
    {
        if (!first.SequenceEqual(second)) throw new InvalidOperationException("Game process identity changed during enumeration.");
        return new(session, first);
    }

    public void Dispose()
    {
        _publisher.Dispose(); // Reject a late capture immediately, even when the native call is draining.
        _reads.Close(() => { });
    }
}
