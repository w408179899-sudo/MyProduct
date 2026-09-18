using CommunityToolkit.Mvvm.ComponentModel;
using Smart.Hosting;

namespace SampleProject.Desktop.ViewModels;

public partial class AccountRow(ManagedAccount account) : ObservableObject
{
    public ManagedAccount Account { get; } = account;
    public string Id => Account.Profile.Id;
    public string Mode => Account.Profile.Mode == RuntimeMode.Mock ? "模拟" : "硬件";
    public string TestedCharacter => Account.Profile.TestedCharacter?.Name ?? "—";
    [ObservableProperty] private string _state = "已停止";
    [ObservableProperty] private string _pid = "—";
    [ObservableProperty] private string _elapsed = "00:00:00";
    [ObservableProperty] private long _ticks;
    [ObservableProperty] private long _actions;
    [ObservableProperty] private string _error = "";
    public void Refresh(DateTimeOffset now)
    {
        State = Account.Status.State switch { SessionState.Stopped => "已停止", SessionState.Connecting => "连接中", SessionState.Running => "运行中",
            SessionState.Reconnecting => "重连中", SessionState.Stopping => "停止中", SessionState.Paused => "已暂停", _ => "异常" };
        Pid = Account.Target?.ProcessId.ToString() ?? "—";
        Ticks = Account.Metrics?.Ticks ?? 0; Actions = Account.Metrics?.Actions ?? 0; Error = Account.Status.Error ?? "";
        if (Account.StartedAt is { } since && Account.Status.State is not (SessionState.Stopped or SessionState.Paused or SessionState.Faulted))
        { var duration = now - since; Elapsed = $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"; }
    }
}
