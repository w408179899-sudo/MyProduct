using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    // In-process comparison only. Keep DateTimeOffset and parsed JSON strings encoded identically.
    private static readonly JsonSerializerOptions PathFingerprintJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private bool resolvingPathClose;
    private bool allowPathClose;
    private bool resolvingProfilePaths;

    private void AddPathMoreMenu(Control page, PathEditorControls editor)
    {
        var button = AddButton(page, "更多操作 ▾", 256, 74, 102, 30);
        var menu = new ContextMenuStrip();
        menu.Items.Add("清空当前坐标", null, (_, _) => ClearPathPoints(editor));
        menu.Items.Add("删除已保存路径", null, (_, _) => DeleteSavedPath(editor));
        button.ContextMenuStrip = menu;
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        button.Disposed += (_, _) => menu.Dispose();
        editor.MoreButton = button;
        var folderButton = AddButton(page, "打开路径文件夹", 120, 74, 128, 30,
            (_, _) => OpenPathLibraryFolder(editor));
        folderButton.Name = "openPathLibraryFolderButton";
    }

    private void AddPathPointList(Control page, PathEditorControls editor, int top)
    {
        var list = new ListView
        {
            Name = "pathPointsList", Location = new Point(12, top), Size = new Size(800, editor.Kind == SharedPathKind.Maintenance ? 90 : 132),
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = _inputBackground,
            ForeColor = _textGreen, BorderStyle = BorderStyle.FixedSingle
        };
        list.Columns.Add("序号", 56);
        list.Columns.Add("坐标 X / Y / Z", 510);
        list.Columns.Add("与上一点距离", 205);
        list.SelectedIndexChanged += (_, _) => PopulatePathPointEditor(editor);
        page.Controls.Add(list);
        editor.GatherPointsList = list;
    }

    private void AddPathPointEditControls(Control page, PathEditorControls editor, int top)
    {
        editor.Page = page;
        var toolbarTop = 147 + (editor.Kind == SharedPathKind.Maintenance ? 72 : editor.Kind == SharedPathKind.Auction ? 40 : 0);
        editor.SelectionLabel = AddLabel(page, "未选择坐标", 195, toolbarTop, 145, 24, _textGreen, FontStyle.Bold);
        AddLabel(page, "下方坐标经按钮应用后生效", 345, toolbarTop, 270, 24);
        editor.ReadPositionButton = AddButton(page, "读取当前位置", 670, top, 142, 28,
            async (_, _) => await ReadPathEditPositionAsync(editor).ConfigureAwait(true));
        editor.Coordinates = new RoundedTextBox[3];
        for (var i = 0; i < 3; i++)
        {
            AddLabel(page, new[] { "X", "Y", "Z" }[i], 12 + i * 218, top + 4, 22, 24);
            var input = AddTextBox(page, string.Empty, 38 + i * 218, top, 174, 28);
            input.Name = "pathCoordinate" + new[] { "X", "Y", "Z" }[i];
            editor.Coordinates[i] = input;
        }
        editor.ReplaceButton = AddButton(page, "替换选中点", 12, top + 36, 116, 30,
            (_, _) => EditSelectedPathPoint(editor, "replace"));
        editor.InsertBeforeButton = AddButton(page, "在选中点前插入", 138, top + 36, 142, 30,
            (_, _) => EditSelectedPathPoint(editor, "before"));
        editor.InsertAfterButton = AddButton(page, "在选中点后插入", 290, top + 36, 142, 30,
            (_, _) => EditSelectedPathPoint(editor, "after"));
        editor.DeletePointButton = AddButton(page, "删除选中点", 442, top + 36, 116, 30,
            (_, _) => EditSelectedPathPoint(editor, "delete"));
        editor.UndoButton = AddButton(page, "撤销上一步", 696, top + 36, 116, 30,
            (_, _) => UndoPathEdit(editor));
    }

    private static int SelectedPathPointIndex(PathEditorControls editor) =>
        editor.GatherPointsList?.SelectedIndices.Count > 0 ? editor.GatherPointsList.SelectedIndices[0] : -1;

    private void PopulatePathPointEditor(PathEditorControls editor)
    {
        if (editor.RefreshingGatherPoints) return;
        var point = GetSelectedGatherPoint(editor);
        if (editor.SelectionLabel is not null)
            editor.SelectionLabel.Text = point is null ? "未选择坐标" : $"已选中第 {point.Index} 点";
        if (editor.Coordinates is { } fields)
        {
            var values = point is null ? null : new[] { point.X, point.Y, point.Z };
            for (var i = 0; i < fields.Length; i++)
                fields[i].Text = values is null ? string.Empty : values[i].ToString("G", CultureInfo.InvariantCulture);
        }
        if (editor.Kind == SharedPathKind.Gather) PopulateGatherPointEditor(editor);
        RefreshPathEditState(editor);
    }

    private async Task ReadPathEditPositionAsync(PathEditorControls editor)
    {
        if (IsPathEditorBusy(editor) || SelectedPathPointIndex(editor) < 0) return;
        editor.ReadingPosition = true;
        RefreshPathEditState(editor);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var scene = await _runtime.ReadSceneForPathRecordingAsync(_account, timeout.Token).ConfigureAwait(true);
            if (!scene.IsReady)
            {
                SetPathStatus(editor, "地图加载中，未读取坐标", true);
                return;
            }
            if (editor.Buffer.MapId is > 0 && editor.Buffer.MapId != scene.Channel!.MapId)
            {
                SetPathStatus(editor, "当前地图与路径不一致，未读取坐标", true);
                return;
            }
            var position = scene.Player!.Position!.Value;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            {
                SetPathStatus(editor, "读取到的坐标无效", true);
                return;
            }
            var values = new[] { position.X, position.Y, position.Z };
            for (var i = 0; i < 3; i++) editor.Coordinates![i].Text = values[i].ToString("G", CultureInfo.InvariantCulture);
            SetPathStatus(editor, "已读取当前位置，请选择替换、前插入或后插入", false);
        }
        catch (OperationCanceledException) { SetPathStatus(editor, "读取坐标超时，请稍后重试", true); }
        catch (Exception ex) { SetPathStatus(editor, "读取坐标失败: " + ex.Message, true); }
        finally { editor.ReadingPosition = false; RefreshPathEditState(editor); }
    }

    private void EditSelectedPathPoint(PathEditorControls editor, string operation)
    {
        if (IsPathEditorBusy(editor)) return;
        var index = SelectedPathPointIndex(editor);
        if (index < 0) { SetPathStatus(editor, "请先选择一个路径点", true); return; }
        var position = default(Vector3Snapshot);
        if (operation != "delete" && !TryReadPathEditCoordinates(editor, out position)) return;
        var before = editor.Buffer.ToDocument(string.Empty);
        var now = DateTimeOffset.Now;
        var result = operation switch
        {
            "replace" => editor.Buffer.ReplaceAt(index, position, now),
            "before" => editor.Buffer.InsertAt(index, position, now),
            "after" => editor.Buffer.InsertAt(index + 1, position, now),
            "delete" => editor.Buffer.RemoveAt(index),
            _ => OperationResult.Fail("未知编辑操作")
        };
        if (!result.Success) { SetPathStatus(editor, result.Error!, true); return; }
        editor.History.Remember(before, index);
        editor.PendingSelection = operation == "after" ? index + 1 : Math.Min(index, editor.Buffer.Count - 1);
        RefreshPathEditor(editor);
        RefreshPathOverviews();
        SetPathStatus(editor, "已更新路径点，可撤销；请点击“保存修改”写入路径", false);
    }

    private bool TryReadPathEditCoordinates(PathEditorControls editor, out Vector3Snapshot position)
    {
        position = default;
        var values = new float[3];
        for (var i = 0; i < 3; i++)
        {
            var text = editor.Coordinates![i].Text.Trim();
            if ((!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) &&
                 !float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[i])) || !float.IsFinite(values[i]))
            {
                SetPathStatus(editor, "X、Y、Z 必须填写有效数值", true);
                editor.Coordinates[i].Focus();
                return false;
            }
        }
        position = new Vector3Snapshot(values[0], values[1], values[2]);
        return true;
    }

    private void UndoPathEdit(PathEditorControls editor)
    {
        if (IsPathEditorBusy(editor) || !editor.History.TryUndo(editor.Buffer, out var selection)) return;
        editor.PendingSelection = selection;
        RefreshPathEditor(editor);
        RefreshPathOverviews();
        SetPathStatus(editor, "已撤销上一步", false);
    }

    private static bool IsPathEditorBusy(PathEditorControls editor) =>
        editor.SavingPath || editor.ReadingPosition || editor.ExecutePathCancellation is not null;

    private static string PathDraftFingerprint(PathEditorControls editor) => JsonSerializer.Serialize(new
    {
        Name = editor.PathNameTextBox?.Text.Trim(), editor.Buffer.MapId, editor.Buffer.Points,
        RadiusEnabled = editor.BindStationaryRadiusCheckBox?.Checked,
        Radius = editor.BindStationaryRadiusCheckBox?.Checked == true ? editor.StationaryRadiusTextBox?.Text.Trim() : null,
        Npc = editor.Kind is SharedPathKind.Maintenance or SharedPathKind.Auction ? GetSelectedCleanupNpcName(editor) : null
    }, PathFingerprintJson);

    private static bool HasPathDraftChanges(PathEditorControls editor) =>
        editor.SavedFingerprint is not null && editor.SavedFingerprint != PathDraftFingerprint(editor);

    private void MarkPathDraftSaved(PathEditorControls editor)
    {
        editor.SavedFingerprint = PathDraftFingerprint(editor);
        RefreshPathEditState(editor);
    }

    private void MarkPathMetadataSaved(PathEditorControls editor, params string[] fields)
    {
        if (string.IsNullOrEmpty(editor.SavedFingerprint)) return;
        var baseline = JsonNode.Parse(editor.SavedFingerprint)!;
        var current = JsonNode.Parse(PathDraftFingerprint(editor))!;
        if (baseline["Name"]?.ToString() != current["Name"]?.ToString()) return;
        foreach (var field in fields) baseline[field] = current[field]?.DeepClone();
        // Metadata saved by "保存配置" must not mark unsaved coordinate edits as saved.
        editor.SavedFingerprint = baseline.ToJsonString(PathFingerprintJson);
        RefreshPathEditState(editor);
    }

    private void InitializePathDraftTracking(PathEditorControls editor)
    {
        if (editor.PathNameTextBox is { } name) name.TextChanged += (_, _) => RefreshPathEditState(editor);
        if (editor.StationaryRadiusTextBox is { } radius) radius.TextChanged += (_, _) => RefreshPathEditState(editor);
        if (editor.BindStationaryRadiusCheckBox is { } binding) binding.Click += (_, _) => RefreshPathEditState(editor);
        if (editor.CleanupNpcCombo is { } npc) npc.TextChanged += (_, _) => RefreshPathEditState(editor);
        MarkPathDraftSaved(editor);
    }

    private void RefreshPathEditState(PathEditorControls editor)
    {
        if (IsDisposed || editor.Page is null) return;
        var busy = IsPathEditorBusy(editor);
        if (busy && !editor.ControlsLocked)
        {
            foreach (Control control in editor.Page.Controls)
            {
                if (control == editor.ExecutePathButton || control is Label) continue;
                editor.EnabledBeforeLock[control] = control.Enabled;
                control.Enabled = false;
            }
            editor.ControlsLocked = true;
        }
        else if (!busy && editor.ControlsLocked)
        {
            foreach (var entry in editor.EnabledBeforeLock)
                if (!entry.Key.IsDisposed) entry.Key.Enabled = entry.Value;
            editor.EnabledBeforeLock.Clear();
            editor.ControlsLocked = false;
        }
        var selected = SelectedPathPointIndex(editor) >= 0;
        foreach (var control in new Control?[] { editor.ReplaceButton, editor.InsertBeforeButton,
                     editor.InsertAfterButton, editor.DeletePointButton, editor.ReadPositionButton })
            if (control is not null) control.Enabled = !busy && selected;
        if (editor.Coordinates is { } fields)
            foreach (var field in fields) field.Enabled = !busy && selected;
        if (editor.UndoButton is { } undo) undo.Enabled = !busy && editor.History.CanUndo;
        if (editor.ExecutePathButton is { } execute)
            execute.Enabled = editor.Kind != SharedPathKind.Gather &&
                (editor.ExecutePathCancellation is not null || !busy && editor.Buffer.Count > 0);
        if (editor.DirtyLabel is { } dirty)
        {
            dirty.Text = editor.SavingPath ? "保存中…" : HasPathDraftChanges(editor) ? "● 有未保存修改" :
                editor.LoadedDocument is null ? "未修改" : "已保存";
            dirty.ForeColor = HasPathDraftChanges(editor) ? Color.DarkOrange : _textGreen;
        }
    }

    private async Task<bool> ResolvePathDraftAsync(PathEditorControls editor)
    {
        if (IsPathEditorBusy(editor)) { SetPathStatus(editor, "请先完成当前操作或停止路径", true); return false; }
        if (!HasPathDraftChanges(editor)) return true;
        var choice = MessageBox.Show(this,
            $"路径“{editor.PathNameTextBox?.Text}”有未保存修改。\n是：保存修改后继续\n否：放弃修改并继续\n取消：留在当前页面",
            "保存路径修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return await ResolvePathDraftChoiceAsync(editor, choice).ConfigureAwait(true);
    }

    // Kept separate from the dialog so save failures and cancel/discard can be exercised with mocks.
    private async Task<bool> ResolvePathDraftChoiceAsync(PathEditorControls editor, DialogResult choice)
    {
        if (IsPathEditorBusy(editor)) return false;
        if (choice == DialogResult.No) return true;
        if (choice != DialogResult.Yes) return false;
        await SavePathAsync(editor).ConfigureAwait(true);
        return !HasPathDraftChanges(editor);
    }

    private void RestorePathCombo(PathEditorControls editor, string? name)
    {
        var previous = loadingPathCombos;
        loadingPathCombos = true;
        try
        {
            if (string.IsNullOrWhiteSpace(name) || !SelectPathComboItem(editor, name, loadPath: false))
                if (editor.SavedPathCombo is { } combo) combo.SelectedIndex = -1;
        }
        finally { loadingPathCombos = previous; }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || allowPathClose) return;
        if (!pathEditors.Values.Any(editor => HasPathDraftChanges(editor) || IsPathEditorBusy(editor))) return;
        e.Cancel = true;
        if (resolvingPathClose) return;
        resolvingPathClose = true;
        try
        {
            var resolved = new Dictionary<PathEditorControls, string>();
            foreach (var editor in pathEditors.Values)
            {
                if (!await ResolvePathDraftAsync(editor).ConfigureAwait(true)) return;
                resolved[editor] = PathDraftFingerprint(editor);
            }
            // A save can yield to another tab. Never close over edits made after its prompt.
            if (resolved.Any(entry => IsPathEditorBusy(entry.Key) || PathDraftFingerprint(entry.Key) != entry.Value)) return;
            allowPathClose = true;
            // Defer Close until this FormClosing event has returned, including synchronous mock saves.
            BeginInvoke(new Action(Close));
        }
        finally { resolvingPathClose = false; }
    }

    private sealed partial class PathEditorControls
    {
        public Control? Page { get; set; }
        public Button? SaveButton { get; set; }
        public Button? MoreButton { get; set; }
        public Label? DirtyLabel { get; set; }
        public Label? SelectionLabel { get; set; }
        public RoundedTextBox[]? Coordinates { get; set; }
        public Button? ReplaceButton { get; set; }
        public Button? InsertBeforeButton { get; set; }
        public Button? InsertAfterButton { get; set; }
        public Button? DeletePointButton { get; set; }
        public Button? ReadPositionButton { get; set; }
        public Button? UndoButton { get; set; }
        public PathEditHistory History { get; } = new();
        public int? PendingSelection { get; set; }
        public string? SavedFingerprint { get; set; }
        public bool ReadingPosition { get; set; }
        public bool ControlsLocked { get; set; }
        public Dictionary<Control, bool> EnabledBeforeLock { get; } = new();
    }
}
