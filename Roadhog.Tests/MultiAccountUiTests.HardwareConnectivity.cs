using System.Windows.Forms;
using Roadhog;
using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class MultiAccountUiTests
{
    public static Task HardwareConnectivityAndDraftSaveAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1);
        var sibling = test.Account(2);
        test.Initialize([account, sibling]);
        Require(Pump(test.Workspace.Processes.StartAsync(sibling.InstanceId)).Success, "sibling occupies a DMA device");
        var siblingPid = test.View(sibling).WorkerProcessId;
        var probe = new ConnectivityProbe();
        test.Workspace.DiscoveryProbe = probe;
        IReadOnlyList<HardwareDeviceFeature> online = [];
        test.Workspace.DiscoveryDevices = () => online;
        using var editor = new AccountHardwareForm(account, [Device(account)], (_, _) =>
            throw new InvalidOperationException("connectivity test should not verify role"),
            availability: () => test.Workspace.HardwareAvailability(account.InstanceId),
            testDma: test.Workspace.TestDmaConnectionAsync, testKmBox: test.Workspace.TestKmBoxConnectionAsync);
        ShowHidden(editor);
        var button = Field<Button>(editor, "_testConnection");
        var dma = (Label)editor.Controls.Find("DmaConnectionStatus", true).Single();
        var kmBox = (Label)editor.Controls.Find("KmBoxConnectionStatus", true).Single();
        Require(dma.Text == "未测试" && kmBox.Text == "未测试", "untested hardware is neutral");

        button.PerformClick();
        Require(dma.Text == "× 离线" && kmBox.ForeColor == System.Drawing.Color.DarkRed && probe.DmaCalls == 0,
            "offline DMA is reported without opening any index");
        online = [Device(account)];
        button.PerformClick();
        Require(dma.ForeColor == System.Drawing.Color.DarkGreen && kmBox.ForeColor == System.Drawing.Color.DarkRed &&
            kmBox.AccessibleDescription == "握手超时", "a KMBox failure leaves DMA success visible");
        Require(!Field<CheckBox>(editor, "_confirm").Enabled, "connectivity does not confirm a role");

        Field<ComboBox>(editor, "_vmm").Text = sibling.VmmDeviceName;
        Require(dma.Text == "未测试" && kmBox.ForeColor == System.Drawing.Color.DarkRed, "editing an index resets DMA status only");
        probe.KmBox = _ => Task.CompletedTask;
        button.PerformClick();
        Require(dma.Text == "× 占用" && kmBox.ForeColor == System.Drawing.Color.DarkGreen && probe.DmaCalls == 1,
            "occupied DMA is skipped independently of KMBox");

        Field<TextBox>(editor, "_ip").Text = "未填写";
        Require(kmBox.Text == "未测试" && dma.Text == "× 占用", "editing KMBox resets only its status");
        Field<Button>(editor, "_save").PerformClick();
        Require(editor.DialogResult == DialogResult.OK && editor.Config.VmmDeviceName == sibling.VmmDeviceName &&
            editor.Config.KmBox?.IpAddress == "未填写" && !HardwareVerificationSession.IsCurrent(editor.Config),
            "an occupied index and incomplete KMBox can be saved as an unverified draft");
        Pump(test.Workspace.SaveHardwareDraftAsync([editor.Config, sibling], CancellationToken.None));
        var saved = Pump(test.Workspace.Accounts.LoadAllAsync()).Value!.Single(config => config.InstanceId == account.InstanceId);
        Require(saved.VmmDeviceName == sibling.VmmDeviceName && !HardwareVerificationSession.IsCurrent(saved), "draft persists on disk");
        Require(!Pump(test.Workspace.Processes.StartAsync(account.InstanceId)).Success &&
            test.View(account).WorkerProcessId is null && test.View(sibling).WorkerProcessId == siblingPid,
            "unverified draft cannot start and the occupied sibling continues running");
    });

    public static Task HardwareConnectivityDropsStaleResultsAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1);
        var releaseDma = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var editor = new AccountHardwareForm(account, [Device(account)], (_, _) =>
            throw new InvalidOperationException(), testDma: (_, _) => releaseDma.Task, testKmBox: async (_, token) =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            });
        ShowHidden(editor);
        var pending = (Task)Invoke(editor, "TestConnectionAsync")!;
        var status = Field<Label>(editor, "_dmaConnection");
        Require(status.Text == "测试中…", "probe reports pending status");
        Field<ComboBox>(editor, "_vmm").Text = "fpga://devindex=4";
        releaseDma.SetResult();
        Application.DoEvents();
        Require(status.Text == "未测试", "a stale result cannot turn a changed index green");
        Field<Button>(editor, "_save").PerformClick();
        Pump(pending);
        Require(cancelled && editor.DialogResult == DialogResult.OK && editor.Config.VmmDeviceName == "fpga://devindex=4",
            "saving while a connectivity probe is pending preserves the draft and cancels the probe");
    });

    public static Task HardwareDraftSavesAfterCancellingRoleTestAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var account = test.Account(1);
        using var editor = new AccountHardwareForm(account, [Device(account)], async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("role test should be cancelled");
        });
        ShowHidden(editor);
        Field<ComboBox>(editor, "_vmm").Text = "fpga://devindex=4";
        var verification = (Task)Invoke(editor, "VerifyAsync")!;
        Field<Button>(editor, "_save").PerformClick();
        Pump(verification);
        Require(editor.DialogResult == DialogResult.OK && editor.Config.VmmDeviceName == "fpga://devindex=4" &&
            !HardwareVerificationSession.IsCurrent(editor.Config), "save cancels an in-flight role test and accepts the unverified draft");
    });

    private sealed class ConnectivityProbe : IDeviceDiscoveryProbe
    {
        public int DmaCalls { get; private set; }
        public Func<CancellationToken, Task> KmBox { get; set; } = _ => Task.FromException(new TimeoutException("握手超时"));
        public Task<string> ReadRoleAsync(string vmmDeviceName, string processName, CancellationToken token) =>
            throw new InvalidOperationException("connectivity must not read the game role");
        public Task VerifyDmaAsync(string hardwareKey, string vmmDeviceName, CancellationToken token)
        {
            DmaCalls++;
            return Task.CompletedTask;
        }
        public Task VerifyKmBoxAsync(AccountKmBoxSettings settings, CancellationToken token) => KmBox(token);
    }
}
