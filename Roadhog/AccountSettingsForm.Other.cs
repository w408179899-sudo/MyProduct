namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private TabPage CreateOtherTab()
    {
        var tab = CreateBaseTab("其他");
        var page = CreatePagePanel();
        tab.Controls.Add(page);

        var otherTabs = new TabControl
        {
            Alignment = TabAlignment.Top,
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(104, 28),
            Name = "otherTabs",
            SizeMode = TabSizeMode.Fixed
        };
        otherTabs.DrawItem += GreenTabs_DrawItem;
        otherTabs.TabPages.Add(CreateEquipmentUpgradeTab());
        otherTabs.TabPages.Add(CreateManastoneExchangeTab());
        page.Controls.Add(otherTabs);

        return tab;
    }

    private TabPage CreateManastoneExchangeTab()
    {
        var tab = CreateBaseTab("魔石兑换");
        var page = CreatePagePanel();
        tab.Controls.Add(page);
        page.AutoScroll = true;
        _manastoneExchangeStart = AddButton(page, "兑换魔石", 24, 18, 110, 30);
        _manastoneExchangeStart.Click += async (_, _) => await StartManastoneExchangeAsync();
        AddLabel(page, "靠近比洛加后点击兑换魔石，先按 F1，再按 F8 选择 NPC，按 C 对话。", 24, 66, 850, 30);
        AddLabel(page, "魔石袋：生命力 +60、命中 +20、盾牌防御 +20，每种 10 个。", 24, 112, 850, 30);
        AddLabel(page, "魔石包袱：精神力 +60、回避 +12、武器防御 +20，每种 10 个。", 24, 150, 850, 30);
        AddLabel(page, "自动接受 → 交出魔石 → 确认；两组材料都不足时结束。再次点击按钮可停止。", 24, 200, 850, 30);
        _manastoneExchangeStatus = AddLabel(page, "等待开始。", 24, 250, 850, 80);
        _manastoneExchangeStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        return tab;
    }

    private CancellationTokenSource? _manastoneExchangeCts;
    private Button? _manastoneExchangeStart;
    private Label? _manastoneExchangeStatus;

    private async Task StartManastoneExchangeAsync()
    {
        if (_manastoneExchangeCts != null)
        {
            _manastoneExchangeCts.Cancel();
            if (_manastoneExchangeStart != null) _manastoneExchangeStart.Text = "正在停止";
            return;
        }
        if (_equipmentUpgradeCts != null) return;
        using var stop = new CancellationTokenSource();
        _manastoneExchangeCts = stop;
        SetEquipmentUpgradeBusy(true);
        if (_manastoneExchangeStart != null)
        {
            _manastoneExchangeStart.Text = "停止兑换";
            _manastoneExchangeStart.Enabled = true;
        }
        void Status(string text) { if (!IsDisposed && _manastoneExchangeStatus != null) _manastoneExchangeStatus.Text = text; }
        try
        {
            Status("正在开始魔石兑换……");
            var result = await _runtime.RunManastoneExchangeAsync(_account, new Progress<string>(Status), stop.Token);
            Status(result.Success ? result.Value!.Message : result.Error ?? "魔石兑换停止。");
        }
        catch (OperationCanceledException) { Status("已停止魔石兑换。"); }
        catch (Exception ex) { Status(ex.Message); }
        finally
        {
            _manastoneExchangeCts = null;
            SetEquipmentUpgradeBusy(false);
            if (!IsDisposed && _manastoneExchangeStart != null)
            {
                _manastoneExchangeStart.Text = "兑换魔石";
                _manastoneExchangeStart.Enabled = true;
            }
        }
    }
}
