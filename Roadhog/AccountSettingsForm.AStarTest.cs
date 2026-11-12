using System.Globalization;
using Roadhog.Application.NavigationTests;
using Roadhog.Core.Model;
using Roadhog.Core.NavigationTests;
using Roadhog.Infrastructure.NavigationTests;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private CancellationTokenSource? _aStarTestCts;
    private AStarTestMap? _aStarTestMap;
    private AStarTestPlan? _aStarTestPlan;
    private TabPage? _aStarTestTab;
    private readonly List<Control> _aStarTestInputs = new();
    private readonly TextBox[] _aStarStart = new TextBox[3];
    private readonly TextBox[] _aStarGoal = new TextBox[3];
    private Button? _aStarWalkButton;
    private Button? _aStarStopButton;
    private Label? _aStarMapLabel;
    private Label? _aStarResultLabel;
    private TextBox? _aStarLog;
    private AStarTestCanvas? _aStarCanvas;
    private NumericUpDown? _aStarFloor;
    private DataGridView? _aStarRouteGrid;

    private TabPage CreateAStarTestTab()
    {
        var tab = CreateBaseTab("A*测试");
        tab.Name = "aStarTestTab";
        _aStarTestTab = tab;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), BackColor = _pageBackground,
            ColumnCount = 1, RowCount = 7
        };
        foreach (var h in new[] { 38, 44, 72, 38, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        tab.Controls.Add(layout);
        var files = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        layout.Controls.Add(files, 0, 0);
        Button Button(FlowLayoutPanel panel, string text, string name, Func<Task> action)
        {
            var b = AddButton(panel, text, 0, 0, 116, 30);
            b.Name = name; b.Margin = new Padding(0, 0, 8, 0);
            b.Click += async (_, _) => await action();
            _aStarTestInputs.Add(b);
            return b;
        }
        Button(files, "加载导航文件", "aStarLoad", PickAStarMapAsync);
        Button(files, "加载副本样本", "aStarSample", () => LoadAStarMapAsync(AStarTestMapFile.SamplePath, true));
        files.Controls.Add(new Label { Text = "显示高度 Z", AutoSize = true, Margin = new Padding(10, 7, 2, 0), ForeColor = _textGreen });
        _aStarFloor = new NumericUpDown { Name = "aStarFloor", Width = 100, DecimalPlaces = 2, Minimum = -100000, Maximum = 100000 };
        _aStarFloor.ValueChanged += (_, _) => { if (_aStarCanvas != null) { _aStarCanvas.FloorHeight = (float)_aStarFloor.Value; _aStarCanvas.Invalidate(); } };
        files.Controls.Add(_aStarFloor);
        _aStarMapLabel = new Label { Name = "aStarMapInfo", Dock = DockStyle.Fill, ForeColor = _textGreen, Text = "加载导航地图 → 在目标位置点击“设为目的地” → 手动走远 → 点击“启动寻路”。先停止本账号挂机。" };
        layout.Controls.Add(_aStarMapLabel, 0, 1);
        var coordinates = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        coordinates.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        coordinates.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(coordinates, 0, 2);
        AddCoordinates(0, "起点", _aStarStart, true);
        AddCoordinates(1, "目的地", _aStarGoal, false);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        layout.Controls.Add(actions, 0, 3);
        Button(actions, "预览路线", "aStarPlan", PlanAStarAsync);
        _aStarWalkButton = Button(actions, "启动寻路", "aStarWalk", WalkAStarAsync);
        _aStarWalkButton.Enabled = false;
        _aStarStopButton = AddButton(actions, "停止", 0, 0, 90, 30);
        _aStarStopButton.Name = "aStarStop"; _aStarStopButton.Enabled = false;
        _aStarStopButton.Click += (_, _) => { _aStarTestCts?.Cancel(); AppendAStarStatus("正在停止……"); };
        _aStarResultLabel = new Label { Name = "aStarResult", Dock = DockStyle.Fill, ForeColor = _textGreen, Text = "尚未计算路线。" };
        layout.Controls.Add(_aStarResultLabel, 0, 4);
        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        _aStarCanvas = new AStarTestCanvas { Name = "aStarCanvas" };
        preview.Controls.Add(_aStarCanvas, 0, 0);
        _aStarRouteGrid = new DataGridView
        {
            Name = "aStarRoute", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = _inputBackground, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        foreach (var name in new[] { "序号", "X", "Y", "Z" }) _aStarRouteGrid.Columns.Add(name, name);
        preview.Controls.Add(_aStarRouteGrid, 1, 0);
        layout.Controls.Add(preview, 0, 5);
        _aStarLog = new TextBox { Name = "aStarLog", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = _inputBackground };
        layout.Controls.Add(_aStarLog, 0, 6);
        return tab;

        void AddCoordinates(int row, string label, TextBox[] fields, bool start)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            panel.Controls.Add(new Label { Text = label, Width = 50, Margin = new Padding(0, 5, 0, 0), ForeColor = _textGreen });
            for (var i = 0; i < 3; i++)
            {
                panel.Controls.Add(new Label { Text = "XYZ"[i].ToString(), Width = 17, Margin = new Padding(2, 5, 0, 0) });
                var field = new TextBox { Name = "aStar" + (start ? "Start" : "Goal") + "XYZ"[i], Width = 110, Text = "0", BackColor = _inputBackground };
                field.TextChanged += (_, _) => InvalidateAStarPlan();
                fields[i] = field; panel.Controls.Add(field); _aStarTestInputs.Add(field);
            }
            Button(panel, start ? "读取当前位置" : "设为目的地", start ? "aStarReadStart" : "aStarReadGoal", () => ReadAStarPositionAsync(start));
            coordinates.Controls.Add(panel, 0, row);
        }
    }

    private async Task PickAStarMapAsync()
    {
        using var dialog = new OpenFileDialog { Title = "选择 A* 导航地图", Filter = "A* 导航文件|*.astar.bin;*.astar.bin.gz;*.astar.json;*.astar.json.gz|JSON 文件|*.json;*.json.gz" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadAStarMapAsync(dialog.FileName, false);
    }

    private Task LoadAStarMapAsync(string path, bool sample) => RunAStarUiOperationAsync(async token =>
    {
        InvalidateAStarPlan();
        var map = await Task.Run(() => AStarTestMapFile.LoadAsync(path, token), token);
        token.ThrowIfCancellationRequested();
        _aStarTestMap = map;
        _aStarMapLabel!.Text = $"{map.Name} · 地图 {map.MapId} · {map.Count:N0} 个节点\r\n{map.Description}";
        _aStarCanvas!.Map = map;
        SetAStarCoordinates(_aStarStart, sample ? new(557, 558, 1022.11f) : map.Position(0));
        if (sample) SetAStarCoordinates(_aStarGoal, new(576, 563, 1022.02f));
        else foreach (var field in _aStarGoal) field.Clear();
        _aStarFloor!.Value = Math.Clamp((decimal)ReadAStarCoordinates(_aStarStart).Z, _aStarFloor.Minimum, _aStarFloor.Maximum);
        _aStarCanvas.Invalidate();
        AppendAStarStatus("已加载 " + map.Name);
        if (sample) await CalculateAStarAsync(token);
    });

    private Task ReadAStarPositionAsync(bool start) => RunAStarUiOperationAsync(async token =>
    {
        var point = await ReadAStarCurrentPositionAsync(token);
        SetAStarCoordinates(start ? _aStarStart : _aStarGoal, point);
        if (start) SetAStarDisplayHeight(point.Z);
        AppendAStarStatus(start ? $"已读取起点：{point.X:F2}, {point.Y:F2}, {point.Z:F2}" :
            $"目的地已记录：{point.X:F2}, {point.Y:F2}, {point.Z:F2}。手动走远后点击“启动寻路”。");
    });

    private async Task<Vector3Snapshot> ReadAStarCurrentPositionAsync(CancellationToken token)
    {
        var map = _aStarTestMap ?? throw new InvalidOperationException("请先加载导航地图。");
        var scene = await _runtime.ReadSceneForPathRecordingAsync(_account, token);
        token.ThrowIfCancellationRequested();
        if (!scene.IsReady) throw new InvalidOperationException("角色尚未进入地图。");
        if (map.MapId != scene.Channel!.MapId)
            throw new InvalidOperationException($"当前地图 {scene.Channel!.MapId} 与加载地图 {map.MapId} 不一致。");
        return scene.Player!.Position!.Value;
    }

    private void SetAStarDisplayHeight(float z) =>
        _aStarFloor!.Value = Math.Clamp((decimal)z, _aStarFloor.Minimum, _aStarFloor.Maximum);

    private Task PlanAStarAsync() => RunAStarUiOperationAsync(CalculateAStarAsync);

    private async Task CalculateAStarAsync(CancellationToken token)
    {
        var map = _aStarTestMap ?? throw new InvalidOperationException("请先加载导航地图。");
        var start = ReadAStarCoordinates(_aStarStart); var goal = ReadAStarCoordinates(_aStarGoal);
        InvalidateAStarPlan();
        var result = await Task.Run(() => new AStarTestPlanner().Plan(map, start, goal, token), token);
        token.ThrowIfCancellationRequested();
        _aStarTestPlan = result;
        _aStarResultLabel!.Text = result.Success
            ? $"长度 {result.Length:F2} 米 · {result.Points.Length} 个路线点 · 搜索 {result.ExpandedNodes:N0} 个节点\r\n起点贴合 {result.StartSnapDistance:F2} 米，终点贴合 {result.GoalSnapDistance:F2} 米；行走到贴合后的导航点。"
            : result.Message;
        _aStarCanvas!.Route = result.Points;
        _aStarCanvas.Invalidate();
        foreach (var p in result.Points.Take(20_000)) _aStarRouteGrid!.Rows.Add(_aStarRouteGrid.Rows.Count + 1, p.X.ToString("F2"), p.Y.ToString("F2"), p.Z.ToString("F2"));
        AppendAStarStatus(result.Success ? $"路线计算完成：{result.Length:F2} 米。" : result.Message);
    }

    private Task WalkAStarAsync() => RunAStarUiOperationAsync(async token =>
    {
        var map = _aStarTestMap ?? throw new InvalidOperationException("请先加载导航地图。");
        _ = ReadAStarCoordinates(_aStarGoal);
        if (pathEditors.Values.Any(e => e.ExecutePathCancellation != null) || _equipmentUpgradeCts != null ||
            _manastoneExchangeCts != null || _personalShopTestCts != null || _auctionTestCts != null || _inventoryDiscardTestCts != null)
            throw new InvalidOperationException("请先停止当前手动操作。");
        InvalidateAStarPlan();
        var start = await ReadAStarCurrentPositionAsync(token);
        SetAStarCoordinates(_aStarStart, start);
        SetAStarDisplayHeight(start.Z);
        AppendAStarStatus($"从角色当前位置 {start.X:F2}, {start.Y:F2}, {start.Z:F2} 计算到已设目的地的路线……");
        await CalculateAStarAsync(token);
        token.ThrowIfCancellationRequested();
        if (_aStarTestPlan is not { Success: true } plan) return;
        var result = await _runtime.RunAStarWalkTestAsync(_account,
            new AStarWalkTestRequest(map.MapId, map.Name, plan.Points.ToArray()),
            new Progress<string>(s => { if (_aStarTestCts is { IsCancellationRequested: false }) AppendAStarStatus(s); }), token);
        AppendAStarStatus(result.Success ? "行走测试完成，已到达路线终点。" : result.Error ?? "行走测试结束。");
    }, walking: true);

    private async Task RunAStarUiOperationAsync(Func<CancellationToken, Task> action, bool walking = false)
    {
        if (_aStarTestCts != null) return;
        using var cts = new CancellationTokenSource();
        _aStarTestCts = cts;
        var disabledPages = new List<TabPage>();
        foreach (var control in _aStarTestInputs) control.Enabled = false;
        _aStarStopButton!.Enabled = true;
        if (walking)
            foreach (TabPage page in settingsTabs.TabPages)
                if (page != _aStarTestTab && page.Enabled) { page.Enabled = false; disabledPages.Add(page); }
        try { await action(cts.Token); }
        catch (OperationCanceledException) { AppendAStarStatus("测试已停止。"); }
        catch (Exception e) { AppendAStarStatus(e.Message); }
        finally
        {
            _aStarTestCts = null;
            if (!IsDisposed)
            {
                foreach (var control in _aStarTestInputs) control.Enabled = true;
                foreach (var page in disabledPages) page.Enabled = true;
                _aStarStopButton.Enabled = false;
                UpdateAStarStartButton();
            }
        }
    }

    private void AppendAStarStatus(string message)
    {
        if (IsDisposed || _aStarLog is null || _aStarLog.IsDisposed) return;
        if (_aStarLog.TextLength > 20_000) _aStarLog.Clear();
        _aStarLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    }

    private void InvalidateAStarPlan()
    {
        _aStarTestPlan = null;
        UpdateAStarStartButton();
        if (_aStarResultLabel != null) _aStarResultLabel.Text = "启动寻路会自动读取角色当前位置，重新计算并行走到已设目的地。";
        if (_aStarCanvas != null) { _aStarCanvas.Route = Array.Empty<Vector3Snapshot>(); _aStarCanvas.Invalidate(); }
        _aStarRouteGrid?.Rows.Clear();
    }

    private void UpdateAStarStartButton()
    {
        if (_aStarWalkButton is null) return;
        _aStarWalkButton.Enabled = _aStarTestCts is null && _aStarTestMap != null &&
            _aStarGoal.All(field => field != null && float.TryParse(field.Text.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) && float.IsFinite(value));
    }

    private static Vector3Snapshot ReadAStarCoordinates(TextBox[] fields)
    {
        var values = new float[3];
        for (var i = 0; i < 3; i++)
            if (!float.TryParse(fields[i].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i]))
                throw new InvalidOperationException("坐标必须是有效数字，使用小数点分隔。");
        return new(values[0], values[1], values[2]);
    }

    private static void SetAStarCoordinates(TextBox[] fields, Vector3Snapshot point)
    {
        var values = new[] { point.X, point.Y, point.Z };
        for (var i = 0; i < 3; i++) fields[i].Text = values[i].ToString("F3", CultureInfo.InvariantCulture);
    }
}
