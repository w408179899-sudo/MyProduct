using System.Globalization;
using Roadhog.Application;
using Roadhog.Application.Shell;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;

namespace Roadhog;

internal sealed class RadarEditorForm : Form
{
    private const int DesiredCanvasWidth = 920;
    private const int SettingsPanelMinimumWidth = 280;

    private readonly string _account;
    private readonly RoadhogRuntime _runtime;
    private readonly IRadarMapStore _mapStore;
    private readonly IFolderLauncher _folderLauncher;
    private readonly Func<RadarObstacleScriptSettings, OperationResult> _applySettings;
    private readonly RadarRoutePlanner _planner = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stack<List<RadarObstacleSegment>> _undo = new();
    private readonly SplitContainer _contentSplit = new();
    private readonly RadarCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly Label _mapLabel = new();
    private readonly Label _selectionLabel = new();
    private readonly Label _statusLabel = new();
    private readonly CheckBox _enabledCheckBox = new();
    private readonly CheckBox _showRouteCheckBox = new();
    private readonly NumericUpDown _waypointReachNumeric = new();
    private readonly NumericUpDown _replanNumeric = new();
    private readonly NumericUpDown _detourNumeric = new();
    private readonly NumericUpDown _rangeNumeric = new();
    private RadarMapDocument _document = new();
    private RadarLiveSnapshot? _snapshot;
    private bool _dirty;
    private bool _refreshInFlight;
    private bool _disposed;
    private Button? _drawButton;
    private Button? _selectButton;
    private Button? _viewModeButton;

    public RadarEditorForm(
        string account,
        RoadhogRuntime runtime,
        IRadarMapStore mapStore,
        IFolderLauncher folderLauncher,
        RadarObstacleScriptSettings settings,
        Func<RadarObstacleScriptSettings, OperationResult> applySettings)
    {
        _account = account;
        _runtime = runtime;
        _mapStore = mapStore;
        _folderLauncher = folderLauncher;
        _applySettings = applySettings;

        Text = "\u96f7\u8fbe\u969c\u788d\u7f16\u8f91\u5668 - " + account;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1280, 820);
        MinimumSize = new Size(980, 640);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(248, 250, 252);
        KeyPreview = true;

        BuildLayout();
        ApplySettingsToControls(settings);
        WireEvents();
        SetCanvasMode(RadarCanvasMode.DrawObstacle);
        _refreshTimer.Start();
        Shown += async (_, _) => await RefreshLiveAsync(force: true).ConfigureAwait(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(
                this,
                "\u5f53\u524d\u5730\u56fe\u969c\u788d\u5c1a\u672a\u4fdd\u5b58\uff0c\u786e\u5b9a\u653e\u5f03\u4fee\u6539\u5e76\u5173\u95ed\uff1f",
                "\u672a\u4fdd\u5b58\u7684\u969c\u788d",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _refreshTimer.Stop();
        _lifetime.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyInitialSplitterLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _refreshTimer.Dispose();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3)
        };
        root.Controls.Add(toolbar, 0, 0);
        _drawButton = AddToolbarButton(toolbar, "\u753b\u7ebf", (_, _) => SetCanvasMode(RadarCanvasMode.DrawObstacle));
        _selectButton = AddToolbarButton(toolbar, "\u9009\u62e9", (_, _) => SetCanvasMode(RadarCanvasMode.Select));
        AddToolbarButton(toolbar, "\u5220\u9664\u9009\u4e2d", (_, _) => DeleteSelected());
        AddToolbarButton(toolbar, "\u64a4\u9500", (_, _) => Undo());
        AddToolbarButton(toolbar, "\u6e05\u7a7a", (_, _) => ClearSegments());
        AddToolbarButton(toolbar, "\u4fdd\u5b58\u5730\u56fe", async (_, _) => await SaveMapAsync().ConfigureAwait(true), 104);
        AddToolbarButton(toolbar, "\u91cd\u65b0\u8bfb\u53d6", async (_, _) => await ReloadMapAsync().ConfigureAwait(true), 104);
        AddToolbarButton(toolbar, "\u9884\u89c8\u7ed5\u884c", (_, _) => PreviewRoute(), 104);
        _viewModeButton = AddToolbarButton(toolbar, "\u5b9e\u65f6\u89c6\u56fe", (_, _) => ToggleViewMode(), 104);
        AddToolbarButton(toolbar, "\u5c45\u4e2d\u89d2\u8272", (_, _) => CenterPlayer(), 104);
        AddToolbarButton(toolbar, "\u6253\u5f00\u5730\u56fe\u6587\u4ef6\u5939", (_, _) => OpenMapFolder(), 128);

        _contentSplit.Dock = DockStyle.Fill;
        _contentSplit.FixedPanel = FixedPanel.Panel2;
        root.Controls.Add(_contentSplit, 0, 1);
        _contentSplit.Panel1.Controls.Add(_canvas);
        _contentSplit.Panel2.Controls.Add(BuildSettingsPanel());

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        root.Controls.Add(_statusLabel, 0, 2);
    }

    private void ApplyInitialSplitterLayout()
    {
        var availableWidth = _contentSplit.ClientSize.Width - _contentSplit.SplitterWidth;
        if (availableWidth < SettingsPanelMinimumWidth + _contentSplit.Panel1MinSize)
        {
            return;
        }

        _contentSplit.Panel2MinSize = SettingsPanelMinimumWidth;
        var maximumDistance = availableWidth - SettingsPanelMinimumWidth;
        _contentSplit.SplitterDistance = Math.Clamp(
            DesiredCanvasWidth,
            _contentSplit.Panel1MinSize,
            maximumDistance);
    }

    private Control BuildSettingsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
            BackColor = Color.White
        };

        var title = new Label
        {
            AutoSize = false,
            Size = new Size(240, 30),
            Text = "\u96f7\u8fbe\u4e0e\u7ed5\u969c\u8bbe\u7f6e",
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 83, 45)
        };
        panel.Controls.Add(title);

        _mapLabel.AutoSize = false;
        _mapLabel.Size = new Size(240, 48);
        _mapLabel.Text = "MapId: \u7b49\u5f85\u8bfb\u53d6";
        panel.Controls.Add(_mapLabel);

        _enabledCheckBox.AutoSize = true;
        _enabledCheckBox.Text = "\u542f\u7528\u539f\u5730\u6253\u602a\u7ed5\u969c";
        _enabledCheckBox.Margin = new Padding(3, 8, 3, 8);
        panel.Controls.Add(_enabledCheckBox);

        AddNumericRow(panel, "\u7ed5\u884c\u70b9\u5230\u8fbe\u8ddd\u79bb (m)", _waypointReachNumeric, 0.25M, 1.5M, 0.25M);
        AddNumericRow(panel, "\u602a\u7269\u79fb\u52a8\u91cd\u7b97 (m)", _replanNumeric, 0.5M, 20M, 0.5M);
        AddNumericRow(panel, "\u5141\u8bb8\u989d\u5916\u7ed5\u8def (m)", _detourNumeric, 0M, 500M, 1M);
        AddNumericRow(panel, "\u663e\u793a\u534a\u5f84 (m)", _rangeNumeric, 10M, 1000M, 5M);

        _showRouteCheckBox.AutoSize = true;
        _showRouteCheckBox.Text = "\u663e\u793a\u9884\u89c8\u8def\u7ebf";
        panel.Controls.Add(_showRouteCheckBox);

        var applyButton = new Button
        {
            Size = new Size(240, 36),
            Margin = new Padding(3, 14, 3, 4),
            Text = "\u5e94\u7528\u5e76\u4fdd\u5b58\u7ed5\u969c\u5f00\u5173",
            BackColor = Color.FromArgb(22, 163, 74),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        applyButton.Click += (_, _) => ApplyAccountSettings();
        panel.Controls.Add(applyButton);

        _selectionLabel.AutoSize = false;
        _selectionLabel.Size = new Size(240, 88);
        _selectionLabel.Margin = new Padding(3, 14, 3, 3);
        _selectionLabel.Text = "\u9009\u62e9\u6a21\u5f0f\u4e0b\u70b9\u602a\u7269\u53ef\u9884\u89c8\u8def\u7ebf\uff1b\u70b9\u9ed1\u7ebf\u53ef\u5220\u9664\u3002";
        _selectionLabel.ForeColor = Color.FromArgb(71, 85, 105);
        panel.Controls.Add(_selectionLabel);

        var help = new Label
        {
            AutoSize = false,
            Size = new Size(240, 170),
            Margin = new Padding(3, 12, 3, 3),
            ForeColor = Color.FromArgb(71, 85, 105),
            Text = "\u64cd\u4f5c\uff1a\n" +
                   "1. \u753b\u7ebf\u6a21\u5f0f\u5de6\u952e\u8fde\u7eed\u70b9\u9009\u53ef\u751f\u6210\u6298\u7ebf\uff1b\u53f3\u952e/Esc \u7ed3\u675f\u5f53\u524d\u8fde\u7ebf\u3002\n" +
                   "2. \u6eda\u8f6e\u7f29\u653e\uff0c\u4e2d\u952e\u62d6\u52a8\u5e73\u79fb\u3002\n" +
                   "3. \u7ebf\u6bb5\u5148\u7559\u5728\u7f16\u8f91\u5668\uff1b\u70b9\u4fdd\u5b58\u5730\u56fe/Ctrl+S \u624d\u6309 MapId \u5199\u5165\u6587\u4ef6\u3002\n" +
                   "4. \u89d2\u8272\u84dd\u70b9\uff0c\u4e3b\u52a8\u602a\u7ea2\u70b9\uff0c\u88ab\u52a8\u602a\u7eff\u70b9\uff0c\u672a\u77e5\u602a\u7070\u70b9\uff1b\u8def\u7ebf\u53ea\u5728\u7a7f\u8fc7\u9ed1\u7ebf\u65f6\u89c6\u4e3a\u88ab\u6321\u3002\n" +
                   "5. \u96f7\u8fbe\u4e0a\u5317\u53f3\u4e1c\uff1a\u5317 -X\uff0c\u4e1c +Y\u3002"
        };
        panel.Controls.Add(help);
        return panel;
    }

    private void WireEvents()
    {
        _refreshTimer.Tick += async (_, _) => await RefreshLiveAsync(force: false).ConfigureAwait(true);
        _canvas.SegmentCreated += (_, args) => AddSegment(args.Start, args.End);
        _canvas.SelectionChanged += (_, _) => UpdateSelectionText();
        _showRouteCheckBox.CheckedChanged += (_, _) =>
        {
            _canvas.ShowPlannedRoute = _showRouteCheckBox.Checked;
            _canvas.Invalidate();
        };
        _rangeNumeric.ValueChanged += (_, _) => _canvas.DisplayRangeMeters = (double)_rangeNumeric.Value;
        KeyDown += RadarEditorForm_KeyDown;
    }

    private async Task RefreshLiveAsync(bool force)
    {
        if (_refreshInFlight || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            var result = await _runtime.ReadRadarSnapshotAsync(_account, _lifetime.Token).ConfigureAwait(true);
            if (!result.Success || result.Value?.Player?.Position is null)
            {
                SetStatus("\u96f7\u8fbe\u5237\u65b0\u5931\u8d25\uff1a" + (result.Error ?? "unknown"), true);
                return;
            }

            var snapshot = result.Value;
            if (_document.MapId != snapshot.MapId)
            {
                if (_dirty && _document.MapId != 0)
                {
                    SetStatus(
                        $"\u68c0\u6d4b\u5230 MapId \u4ece {_document.MapId} \u53d8\u4e3a {snapshot.MapId}\uff0c\u8bf7\u5148\u4fdd\u5b58\u6216\u91cd\u65b0\u8bfb\u53d6\u3002",
                        true);
                    return;
                }

                await LoadMapAsync(snapshot.MapId).ConfigureAwait(true);
            }
            else if (force && _document.MapId != 0 && !_dirty)
            {
                await LoadMapAsync(snapshot.MapId).ConfigureAwait(true);
            }

            _snapshot = snapshot;
            _canvas.Snapshot = snapshot;
            _mapLabel.Text = $"MapId: {snapshot.MapId}\n\u969c\u788d\u7ebf\u6bb5: {_document.Segments.Count}";
            if (!force)
            {
                SetStatus(
                    $"\u5df2\u5237\u65b0  \u89d2\u8272: {snapshot.Player.Position.Value.X:F1}, {snapshot.Player.Position.Value.Y:F1}  \u602a\u7269: {CountMonsters(snapshot.WorldObjects)}",
                    false);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the editor cancels the current read.
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private async Task LoadMapAsync(uint mapId)
    {
        var result = await _mapStore.LoadAsync(mapId, _lifetime.Token).ConfigureAwait(true);
        if (!result.Success || result.Value is null)
        {
            SetStatus("\u8bfb\u53d6\u969c\u788d\u5730\u56fe\u5931\u8d25\uff1a" + (result.Error ?? "unknown"), true);
            return;
        }

        _document = result.Value.Document.Clone();
        _document.MapId = mapId;
        _canvas.Document = _document;
        _canvas.RoutePlan = null;
        _undo.Clear();
        SetDirty(false);
        _mapLabel.Text = $"MapId: {mapId}\n\u969c\u788d\u7ebf\u6bb5: {_document.Segments.Count}";
        SetStatus(
            result.Value.Found
                ? $"\u5df2\u8bfb\u53d6 {mapId}.json\uff0c\u5171 {_document.Segments.Count} \u6761\u969c\u788d\u3002"
                : $"MapId {mapId} \u5c1a\u65e0\u969c\u788d\u6587\u4ef6\uff0c\u53ef\u76f4\u63a5\u5f00\u59cb\u753b\u7ebf\u3002",
            false);
    }

    private async Task ReloadMapAsync()
    {
        var mapId = _snapshot?.MapId ?? _document.MapId;
        if (mapId == 0)
        {
            SetStatus("\u5c1a\u672a\u8bfb\u5230 MapId\u3002", true);
            return;
        }

        if (_dirty && MessageBox.Show(
                this,
                "\u91cd\u65b0\u8bfb\u53d6\u4f1a\u653e\u5f03\u672a\u4fdd\u5b58\u7684\u969c\u788d\uff0c\u7ee7\u7eed\uff1f",
                "\u91cd\u65b0\u8bfb\u53d6",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await LoadMapAsync(mapId).ConfigureAwait(true);
    }

    private async Task SaveMapAsync()
    {
        if (_document.MapId == 0)
        {
            SetStatus("\u5c1a\u672a\u8bfb\u5230 MapId\uff0c\u4e0d\u80fd\u4fdd\u5b58\u3002", true);
            return;
        }

        var result = await _mapStore.SaveAsync(_document.Clone(), _lifetime.Token).ConfigureAwait(true);
        if (!result.Success)
        {
            SetStatus("\u4fdd\u5b58\u969c\u788d\u5730\u56fe\u5931\u8d25\uff1a" + (result.Error ?? "unknown"), true);
            return;
        }

        _runtime.NotifyRadarMapSaved(_document.MapId);
        SetDirty(false);
        SetStatus(
            $"\u5df2\u539f\u5b50\u4fdd\u5b58 {_document.MapId}.json\uff0c\u5171 {_document.Segments.Count} \u6761\u969c\u788d\u3002",
            false);
    }

    private void AddSegment(RadarPoint start, RadarPoint end)
    {
        if (_document.MapId == 0)
        {
            SetStatus("\u8bf7\u5148\u7b49\u5f85 MapId \u8bfb\u53d6\u6210\u529f\u3002", true);
            return;
        }

        PushUndo();
        _document.Segments.Add(new RadarObstacleSegment
        {
            Id = "wall-" + Guid.NewGuid().ToString("N")[..12],
            Start = RoundPoint(start),
            End = RoundPoint(end)
        });
        MarkMapChanged("\u5df2\u6dfb\u52a0\u969c\u788d\u7ebf\u6bb5");
    }

    private void DeleteSelected()
    {
        var index = _canvas.SelectedSegmentIndex;
        if (index < 0 || index >= _document.Segments.Count)
        {
            SetStatus("\u8bf7\u5148\u5207\u6362\u5230\u9009\u62e9\u6a21\u5f0f\uff0c\u70b9\u4e2d\u4e00\u6761\u969c\u788d\u3002", true);
            return;
        }

        PushUndo();
        _document.Segments.RemoveAt(index);
        _canvas.ClearSelection();
        MarkMapChanged("\u5df2\u5220\u9664\u9009\u4e2d\u969c\u788d");
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            SetStatus("\u6ca1\u6709\u53ef\u64a4\u9500\u7684\u64cd\u4f5c\u3002", false);
            return;
        }

        _document.Segments = _undo.Pop();
        _canvas.Document = _document;
        MarkMapChanged("\u5df2\u64a4\u9500\u4e0a\u4e00\u6b21\u969c\u788d\u4fee\u6539");
    }

    private void ClearSegments()
    {
        if (_document.Segments.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "\u786e\u5b9a\u6e05\u7a7a\u5f53\u524d MapId \u7684\u5168\u90e8\u969c\u788d\u7ebf\u6bb5\uff1f",
                "\u6e05\u7a7a\u969c\u788d",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        PushUndo();
        _document.Segments.Clear();
        _canvas.ClearSelection();
        MarkMapChanged("\u5df2\u6e05\u7a7a\u969c\u788d\uff0c\u70b9\u51fb\u4fdd\u5b58\u5730\u56fe\u540e\u751f\u6548");
    }

    private void PreviewRoute()
    {
        if (_snapshot?.Player?.Position is not { } playerPosition)
        {
            SetStatus("\u5c1a\u672a\u8bfb\u5230\u89d2\u8272\u5750\u6807\u3002", true);
            return;
        }

        if (_canvas.SelectedMonster?.Position is not { } monsterPosition)
        {
            SetCanvasMode(RadarCanvasMode.Select);
            SetStatus("\u8bf7\u5728\u96f7\u8fbe\u4e0a\u70b9\u9009\u4e00\u4e2a\u84dd\u8272\u602a\u7269\uff0c\u518d\u70b9\u9884\u89c8\u3002", true);
            return;
        }

        var settings = CaptureSettings();
        var plan = _planner.Plan(new RadarRouteRequest(
            new RadarPoint(playerPosition.X, playerPosition.Y),
            new RadarPoint(monsterPosition.X, monsterPosition.Y),
            _document.Segments,
            settings.MaximumDetourExtraMeters));
        _canvas.RoutePlan = plan;
        SetStatus(
            plan.Success
                ? $"\u8def\u7ebf\u9884\u89c8\uff1a{(plan.Direct ? "\u76f4\u7ebf" : "\u7ed5\u884c")}\uff0c\u7ed5\u884c\u70b9 {Math.Max(0, plan.Points.Count - 2)} \u4e2a\uff0c\u8def\u7a0b {plan.RouteDistance:F1}m\uff0c\u8ba1\u7b97 {plan.Elapsed.TotalMilliseconds:F1}ms\u3002"
                : "\u8def\u7ebf\u9884\u89c8\u5931\u8d25\uff1a" + plan.Reason,
            !plan.Success);
    }

    private void ApplyAccountSettings()
    {
        var settings = CaptureSettings();
        var result = _applySettings(settings);
        if (!result.Success)
        {
            SetStatus("\u4fdd\u5b58\u7ed5\u969c\u5f00\u5173\u5931\u8d25\uff1a" + (result.Error ?? "unknown"), true);
            return;
        }

        SetStatus(
            settings.Enabled
                ? "\u7ed5\u969c\u5f00\u5173\u5df2\u4fdd\u5b58\u5e76\u7acb\u5373\u5e94\u7528\u4e8e\u5f53\u524d\u8d26\u53f7\u7684\u539f\u5730\u6253\u602a\u3002"
                : "\u7ed5\u969c\u5f00\u5173\u5df2\u5173\u95ed\uff0c\u5df2\u6062\u590d\u539f\u5730\u6253\u602a\u65e7\u903b\u8f91\u3002",
            false);
    }

    private RadarObstacleScriptSettings CaptureSettings()
    {
        return new RadarObstacleScriptSettings
        {
            Enabled = _enabledCheckBox.Checked,
            WaypointReachMeters = (double)_waypointReachNumeric.Value,
            TargetReplanDistanceMeters = (double)_replanNumeric.Value,
            MaximumDetourExtraMeters = (double)_detourNumeric.Value,
            DisplayRangeMeters = (double)_rangeNumeric.Value,
            ShowPlannedRoute = _showRouteCheckBox.Checked
        };
    }

    private void ApplySettingsToControls(RadarObstacleScriptSettings settings)
    {
        var value = settings ?? new RadarObstacleScriptSettings();
        _enabledCheckBox.Checked = value.Enabled;
        SetNumeric(_waypointReachNumeric, value.WaypointReachMeters);
        SetNumeric(_replanNumeric, value.TargetReplanDistanceMeters);
        SetNumeric(_detourNumeric, value.MaximumDetourExtraMeters);
        SetNumeric(_rangeNumeric, value.DisplayRangeMeters);
        _showRouteCheckBox.Checked = value.ShowPlannedRoute;
        _canvas.DisplayRangeMeters = value.DisplayRangeMeters;
        _canvas.ShowPlannedRoute = value.ShowPlannedRoute;
    }

    private void ToggleViewMode()
    {
        _canvas.FollowPlayer = !_canvas.FollowPlayer;
        if (_canvas.FollowPlayer)
        {
            _canvas.CenterOnPlayer();
        }

        if (_viewModeButton is not null)
        {
            _viewModeButton.Text = _canvas.FollowPlayer ? "\u5b9e\u65f6\u89c6\u56fe" : "\u7f16\u8f91\u89c6\u56fe";
        }

        _canvas.Invalidate();
    }

    private void CenterPlayer()
    {
        _canvas.CenterOnPlayer();
        if (_viewModeButton is not null)
        {
            _viewModeButton.Text = "\u5b9e\u65f6\u89c6\u56fe";
        }
    }

    private void OpenMapFolder()
    {
        var result = _folderLauncher.Open(_mapStore.DirectoryPath);
        if (!result.Success)
        {
            SetStatus("\u6253\u5f00\u5730\u56fe\u6587\u4ef6\u5939\u5931\u8d25\uff1a" + (result.Error ?? "unknown"), true);
        }
    }

    private void SetCanvasMode(RadarCanvasMode mode)
    {
        _canvas.SetMode(mode);
        if (_drawButton is not null)
        {
            _drawButton.BackColor = mode == RadarCanvasMode.DrawObstacle
                ? Color.FromArgb(22, 163, 74)
                : SystemColors.Control;
            _drawButton.ForeColor = mode == RadarCanvasMode.DrawObstacle ? Color.White : Color.Black;
        }

        if (_selectButton is not null)
        {
            _selectButton.BackColor = mode == RadarCanvasMode.Select
                ? Color.FromArgb(22, 163, 74)
                : SystemColors.Control;
            _selectButton.ForeColor = mode == RadarCanvasMode.Select ? Color.White : Color.Black;
        }
    }

    private void UpdateSelectionText()
    {
        if (_canvas.SelectedMonster is { } monster)
        {
            _selectionLabel.Text = $"\u5df2\u9009\u602a\u7269\uff1a{monster.Name}\nEntityId: {monster.EntityId}\nServerObjectId: {monster.ServerObjectId}";
            return;
        }

        if (_canvas.SelectedSegmentIndex is var index && index >= 0 && index < _document.Segments.Count)
        {
            var segment = _document.Segments[index];
            _selectionLabel.Text = $"\u5df2\u9009\u969c\u788d #{index + 1}\n{segment.Start.X:F1},{segment.Start.Y:F1} -> {segment.End.X:F1},{segment.End.Y:F1}\n\u957f\u5ea6: {segment.Length:F1}m";
            return;
        }

        _selectionLabel.Text = "\u9009\u62e9\u6a21\u5f0f\u4e0b\u70b9\u602a\u7269\u53ef\u9884\u89c8\u8def\u7ebf\uff1b\u70b9\u9ed1\u7ebf\u53ef\u5220\u9664\u3002";
    }

    private void PushUndo()
    {
        _undo.Push(_document.Segments.Select(segment => segment.Clone()).ToList());
        if (_undo.Count > 100)
        {
            var retained = _undo.Take(100).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in retained)
            {
                _undo.Push(item);
            }
        }
    }

    private void MarkMapChanged(string message)
    {
        _canvas.Document = _document;
        _canvas.RoutePlan = null;
        SetDirty(true);
        _mapLabel.Text = $"MapId: {_document.MapId}\n\u969c\u788d\u7ebf\u6bb5: {_document.Segments.Count}";
        SetStatus(message + "\uff0c\u5c1a\u672a\u4fdd\u5b58\u3002", false);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        var baseTitle = "\u96f7\u8fbe\u969c\u788d\u7f16\u8f91\u5668 - " + _account;
        Text = dirty ? baseTitle + " *" : baseTitle;
    }

    private void SetStatus(string text, bool error)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = error
            ? Color.FromArgb(185, 28, 28)
            : Color.FromArgb(71, 85, 105);
    }

    private void RadarEditorForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            _ = SaveMapAsync();
        }
        else if (e.Control && e.KeyCode == Keys.Z)
        {
            e.SuppressKeyPress = true;
            Undo();
        }
        else if (e.KeyCode == Keys.Delete)
        {
            e.SuppressKeyPress = true;
            DeleteSelected();
        }
        else if (e.KeyCode == Keys.Escape && _canvas.CancelPendingSegment())
        {
            e.SuppressKeyPress = true;
            SetStatus("\u5df2\u7ed3\u675f\u5f53\u524d\u8fde\u7ebf\u3002", false);
        }
    }

    private static Button AddToolbarButton(
        Control parent,
        string text,
        EventHandler click,
        int width = 86)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 32),
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.Flat
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }

    private static void AddNumericRow(
        FlowLayoutPanel parent,
        string labelText,
        NumericUpDown numeric,
        decimal minimum,
        decimal maximum,
        decimal increment)
    {
        var row = new Panel { Size = new Size(240, 54), Margin = new Padding(3, 2, 3, 2) };
        var label = new Label
        {
            Text = labelText,
            AutoSize = false,
            Location = new Point(0, 0),
            Size = new Size(240, 22)
        };
        numeric.Location = new Point(0, 24);
        numeric.Size = new Size(150, 28);
        numeric.DecimalPlaces = increment < 1M ? 2 : 0;
        numeric.Minimum = minimum;
        numeric.Maximum = maximum;
        numeric.Increment = increment;
        row.Controls.Add(label);
        row.Controls.Add(numeric);
        parent.Controls.Add(row);
    }

    private static void SetNumeric(NumericUpDown numeric, double value)
    {
        var converted = (decimal)Math.Clamp(value, (double)numeric.Minimum, (double)numeric.Maximum);
        numeric.Value = converted;
    }

    private static RadarPoint RoundPoint(RadarPoint point)
    {
        return new RadarPoint(Math.Round(point.X, 3), Math.Round(point.Y, 3));
    }

    private static int CountMonsters(IReadOnlyList<WorldObjectSnapshot> objects)
    {
        return objects.Count(item =>
            string.Equals(item.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase) &&
            item.Position is not null &&
            item.IsAlive);
    }
}
