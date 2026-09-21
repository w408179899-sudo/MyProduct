using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Infrastructure.Paths;

internal static class PathEditingTests
{
    public static Task BufferEditsAsync()
    {
        var buffer = new PathRecordingBuffer();
        buffer.Load(Document().Points, 47);
        var history = new PathEditHistory();
        history.Remember(buffer.ToDocument("route"), 1);
        var action = buffer.Points[1].GatherActions[0];
        Check(buffer.ReplaceAt(1, new(6, 8, 0), DateTimeOffset.Now).Success, "replace middle");
        Check(ReferenceEquals(action, buffer.Points[1].GatherActions[0]), "replacement retains bound action");
        Equal(10D, buffer.Points[1].SegmentDistance, "3D distance updated");
        Check(buffer.InsertAt(0, new(-3, -4, 0), DateTimeOffset.Now).Success, "insert before first");
        Check(buffer.InsertAt(buffer.Count, new(20, 0, 0), DateTimeOffset.Now).Success, "insert after last");
        Check(buffer.InsertAt(2, new(1, 2, 3), DateTimeOffset.Now).Success, "insert in middle");
        Equal(0, buffer.Points[2].GatherActions.Count, "new point does not copy neighbor actions");
        for (var i = 0; i < buffer.Count; i++) Equal(i + 1, buffer.Points[i].Index, "consecutive index");
        Equal(buffer.Points.Sum(point => point.SegmentDistance), buffer.TotalDistance, "distance sum");
        Check(!buffer.RemoveAt(-1).Success && !buffer.RemoveAt(buffer.Count).Success, "invalid delete rejected");
        Check(!buffer.InsertAt(-1, new(), DateTimeOffset.Now).Success, "invalid insertion rejected");
        Check(!buffer.ReplaceAt(0, new(float.NaN, 0, 0), DateTimeOffset.Now).Success, "NaN rejected");
        Check(!buffer.InsertAt(0, new(0, float.PositiveInfinity, 0), DateTimeOffset.Now).Success, "infinity rejected");
        action.GatherName = "changed";
        buffer.Clear();
        Check(history.TryUndo(buffer, out var selection), "undo clear and all later changes from deep snapshot");
        Equal(1, selection, "selection restored");
        Equal(3, buffer.Count, "points restored");
        Equal((uint?)47, buffer.MapId, "map restored");
        Equal("herb", buffer.Points[1].GatherActions[0].GatherName, "history is a deep copy");
        while (buffer.Count > 0) Check(buffer.RemoveAt(0).Success, "delete first through last");
        Equal(0D, buffer.TotalDistance, "empty distance");
        Check(buffer.InsertAt(0, new(), DateTimeOffset.Now).Success, "empty insertion");
        Equal(0D, buffer.TotalDistance, "single point distance");
        for (var i = 0; i < 55; i++) history.Remember(buffer.ToDocument("route"), 0);
        var count = 0;
        while (history.TryUndo(buffer, out _)) count++;
        Equal(50, count, "bounded undo history");
        return Task.CompletedTask;
    }

    public static Task AllTabsAsync() => RunSta(() =>
    {
        using var form = CreateForm(new InMemorySharedPathStore(Document()));
        Show(form);
        foreach (var kind in Enum.GetValues<SharedPathKind>())
        {
            var editor = Editor(form, kind);
            Invoke(form, "LoadPathByName", editor, "route");
            SelectTab(editor);
            var list = Get<ListView>(editor, "GatherPointsList");
            Equal(3, list.Items.Count, "all tabs show point rows");
            var page = Get<Control>(editor, "Page");
            var delete = Get<Button>(editor, "DeletePointButton");
            Check(delete.Bottom <= page.ClientSize.Height, "editing buttons fit default window on " + kind);
            Select(list, 1);
            SetCoordinates(editor, "6", "8", "0");
            Get<Button>(editor, "ReplaceButton").PerformClick();
            var buffer = Get<PathRecordingBuffer>(editor, "Buffer");
            Equal(6D, buffer.Points[1].X, "button replaces selected point");
            Equal("herb", buffer.Points[1].GatherActions[0].GatherName, "all tabs preserve actions");
            Check(Dirty(form, editor), "replacement marks dirty");
            SetCoordinates(editor, "7", "9", "1");
            Get<Button>(editor, "InsertBeforeButton").PerformClick();
            Equal(7D, buffer.Points[1].X, "insert before selected");
            Equal(1, list.SelectedIndices[0], "new before point selected");
            Get<Button>(editor, "InsertAfterButton").PerformClick();
            Equal(2, list.SelectedIndices[0], "new after point selected");
            Equal(5, buffer.Count, "two insertions");
            Get<Button>(editor, "DeletePointButton").PerformClick();
            Equal(4, buffer.Count, "delete selected");
            Equal(2, list.SelectedIndices[0], "next neighbor selected");
            for (var i = 0; i < 4; i++) Get<Button>(editor, "UndoButton").PerformClick();
            Equal(3, buffer.Count, "all operations undone");
            Equal(3D, buffer.Points[1].X, "original coordinates restored");
            Check(!Dirty(form, editor), "undo to baseline clears dirty");
            Select(list, 2);
            Get<Button>(editor, "DeletePointButton").PerformClick();
            Equal(1, list.SelectedIndices[0], "delete last selects previous");
            while (buffer.Count > 0) Get<Button>(editor, "DeletePointButton").PerformClick();
            Check(!Get<Button>(editor, "ReplaceButton").Enabled, "empty list disables selected actions");
            Check(!Get<Button>(editor, "ExecutePathButton").Enabled, "empty list cannot execute");
            Get<Button>(editor, "UndoButton").PerformClick();
            Equal(1, buffer.Count, "undo last deletion restores point");
            Invoke(form, "LoadPathByName", editor, "route");
            Check(!Get<PathEditHistory>(editor, "History").CanUndo, "loading resets history");
        }
        var texts = Descendants(form).Select(control => control.Text).ToArray();
        Check(!texts.Contains("开始录制") && !texts.Contains("停止录制"), "automatic recording has no UI entry");
        var output = Environment.GetEnvironmentVariable("ROADHOG_PATH_EDIT_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            foreach (var kind in new[] { SharedPathKind.Revive, SharedPathKind.Gather, SharedPathKind.Maintenance })
            {
                SelectTab(Editor(form, kind));
                System.Windows.Forms.Application.DoEvents();
                using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
                bitmap.Save(Path.Combine(output, kind + ".png"));
            }
        }
    });

    public static Task SaveReloadAsync() => RunSta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-path-edit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSharedPathStore(directory);
            Pump(store.SaveAsync(Document()));
            using var form = CreateForm(store);
            Show(form);
            foreach (var kind in Enum.GetValues<SharedPathKind>())
            {
                var editor = Editor(form, kind);
                Invoke(form, "LoadPathByName", editor, "route");
                SelectTab(editor);
                Select(Get<ListView>(editor, "GatherPointsList"), 1);
                SetCoordinates(editor, "6", "8", "2");
                Invoke(form, "EditSelectedPathPoint", editor, "replace");
                Pump((Task)Invoke(form, "SavePathAsync", editor)!);
                Check(!Dirty(form, editor), "save clears dirty");
                var saved = Pump(store.LoadAsync("route")).Value!;
                Equal(6D, saved.Points[1].X, "edited coordinate persisted");
                Equal("herb", saved.Points[1].GatherActions[0].GatherName, "action persisted");
                Equal((uint?)47, saved.MapId, "map preserved");
                Equal((double?)30, saved.BoundStationaryCombatRadius, "radius preserved");
                Equal("merchant", saved.CleanupNpcName, "NPC preserved");
                Equal("auction", saved.AuctionNpcName, "auction NPC preserved");
                Equal((int?)123, saved.BagCleanupSellItemClickX, "cleanup click preserved");
                Invoke(form, "LoadPathByName", editor, "route");
                Equal(6D, Get<PathRecordingBuffer>(editor, "Buffer").Points[1].X, "reload edited path");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    public static Task ValidationAndBusyAsync() => RunSta(() =>
    {
        using var form = CreateForm(new InMemorySharedPathStore(Document()));
        Show(form);
        var editor = Editor(form, SharedPathKind.Combat);
        Invoke(form, "LoadPathByName", editor, "route");
        SelectTab(editor);
        Select(Get<ListView>(editor, "GatherPointsList"), 1);
        foreach (var invalid in new[] { "", "NaN", "Infinity", "1e100", "wrong" })
        {
            SetCoordinates(editor, invalid, "2", "3");
            Invoke(form, "EditSelectedPathPoint", editor, "before");
            Equal(3, Get<PathRecordingBuffer>(editor, "Buffer").Count, "invalid input leaves points intact");
            Check(!Get<PathEditHistory>(editor, "History").CanUndo, "invalid input does not consume undo");
        }
        SetCoordinates(editor, "5", "6", "7");
        foreach (var busyProperty in new[] { "SavingPath", "ReadingPosition", "ExecutePathCancellation" })
        {
            using var cts = new CancellationTokenSource();
            Set(editor, busyProperty, busyProperty == "ExecutePathCancellation" ? cts : true);
            Invoke(form, "RefreshPathEditState", editor);
            Check(!Get<Button>(editor, "ReplaceButton").Enabled, "busy edit disabled");
            Check(!Get<Control>(editor, "SavedPathCombo").Enabled, "busy switching disabled");
            Invoke(form, "EditSelectedPathPoint", editor, "delete");
            Equal(3, Get<PathRecordingBuffer>(editor, "Buffer").Count, "busy handler guard");
            if (busyProperty == "ExecutePathCancellation")
            {
                Check(Get<Button>(editor, "ExecutePathButton").Enabled, "stop remains available");
                cts.Cancel();
                Invoke(form, "EditSelectedPathPoint", editor, "delete");
                Equal(3, Get<PathRecordingBuffer>(editor, "Buffer").Count, "stopping still locks edit");
            }
            Set(editor, busyProperty, busyProperty == "ExecutePathCancellation" ? null : false);
            Invoke(form, "RefreshPathEditState", editor);
            Check(Get<Button>(editor, "ReplaceButton").Enabled, "editing restored after completion");
        }
    });

    public static Task SaveFailureAndChoicesAsync() => RunSta(() =>
    {
        var store = new ControlledStore();
        using var form = CreateForm(store);
        Show(form);
        var editor = Editor(form, SharedPathKind.Combat);
        Invoke(form, "LoadPathByName", editor, "route");
        SelectTab(editor);
        Select(Get<ListView>(editor, "GatherPointsList"), 1);
        SetCoordinates(editor, "8", "9", "10");
        Invoke(form, "EditSelectedPathPoint", editor, "replace");
        Check(!Pump((Task<bool>)Invoke(form, "ResolvePathDraftChoiceAsync", editor, DialogResult.Cancel)!), "cancel keeps draft");
        Check(Dirty(form, editor), "cancel does not clear changes");
        Check(Pump((Task<bool>)Invoke(form, "ResolvePathDraftChoiceAsync", editor, DialogResult.No)!), "discard allows navigation");
        Equal(0, store.Saves, "discard does not write");
        store.Fail = true;
        Check(!Pump((Task<bool>)Invoke(form, "ResolvePathDraftChoiceAsync", editor, DialogResult.Yes)!), "failed save prevents navigation");
        Check(Dirty(form, editor), "failed save retains draft");
        store.Fail = false;
        store.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = (Task)Invoke(form, "SavePathAsync", editor)!;
        Check(!pending.IsCompleted, "delayed save in flight");
        Check(!Get<Button>(editor, "DeletePointButton").Enabled, "saving disables edits");
        var saves = store.Saves;
        Pump((Task)Invoke(form, "SavePathAsync", editor)!);
        Equal(saves, store.Saves, "double save ignored");
        store.Gate.SetResult();
        Pump(pending);
        Check(!Dirty(form, editor), "successful save clears draft");
        Invoke(form, "UndoPathEdit", editor);
        Check(Dirty(form, editor), "undo after saving creates a new draft");
        store.Gate = null;
        Check(Pump((Task<bool>)Invoke(form, "ResolvePathDraftChoiceAsync", editor, DialogResult.Yes)!), "successful save allows navigation");
    });

    public static Task ReadPositionAsync() => RunSta(() =>
    {
        var api = new FakeGameApi { Channel = new(0, 1, 47, DateTimeOffset.Now) };
        using var form = CreateForm(new InMemorySharedPathStore(Document()), api);
        Show(form);
        var editor = Editor(form, SharedPathKind.Revive);
        Invoke(form, "LoadPathByName", editor, "route");
        SelectTab(editor);
        Select(Get<ListView>(editor, "GatherPointsList"), 1);
        var gate = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.TransitionReadAsync = _ => gate.Task;
        var pending = (Task)Invoke(form, "ReadPathEditPositionAsync", editor)!;
        Check(!Get<ListView>(editor, "GatherPointsList").Enabled, "read freezes point selection");
        gate.SetResult(new(true, api.Player, api.Channel, DateTimeOffset.Now));
        Pump(pending);
        Equal("0", ((Control[])Get<object>(editor, "Coordinates"))[0].Text, "current position fills inputs");
        Check(!Dirty(form, editor), "reading only does not mutate draft");
        api.TransitionReadAsync = null;
        api.Channel = new(0, 1, 48, DateTimeOffset.Now);
        SetCoordinates(editor, "123", "456", "789");
        Pump((Task)Invoke(form, "ReadPathEditPositionAsync", editor)!);
        Equal("123", ((Control[])Get<object>(editor, "Coordinates"))[0].Text, "wrong map leaves input intact");
        Check(Get<Label>(editor, "StatusLabel").Text.Contains("不一致"), "wrong map explained");
        api.TransitionRead = () => new(false, null, null, DateTimeOffset.Now);
        Pump((Task)Invoke(form, "ReadPathEditPositionAsync", editor)!);
        Check(Get<Label>(editor, "StatusLabel").Text.Contains("加载"), "loading scene explained");
        api.TransitionRead = () => throw new InvalidOperationException("read failed");
        Pump((Task)Invoke(form, "ReadPathEditPositionAsync", editor)!);
        Check(Get<Button>(editor, "ReadPositionButton").Enabled, "read failure releases busy state");
        Check(Get<Label>(editor, "StatusLabel").Text.Contains("超时"), "persistent read failure times out visibly");
        api.TransitionRead = null;
        api.Channel = new(0, 1, 47, DateTimeOffset.Now);
        var buffer = Get<PathRecordingBuffer>(editor, "Buffer");
        Select(Get<ListView>(editor, "GatherPointsList"), 1);
        Pump((Task)Invoke(form, "AddCurrentPlayerPointAsync", editor, "test", true, false)!);
        Equal(4, buffer.Count, "manual append adds current position at end");
        Equal(3, Get<ListView>(editor, "GatherPointsList").SelectedIndices[0], "append selects last point");
        Invoke(form, "UndoPathEdit", editor);
        Equal(3, buffer.Count, "manual append can be undone");
        Equal(1, Get<ListView>(editor, "GatherPointsList").SelectedIndices[0], "undo append restores selection");
        buffer.Clear();
        Get<PathEditHistory>(editor, "History").Clear();
        Invoke(form, "RefreshPathEditor", editor);
        Pump((Task)Invoke(form, "AddCurrentPlayerPointAsync", editor, "test", true, false)!);
        Equal((uint?)47, buffer.MapId, "first append binds map");
        Invoke(form, "UndoPathEdit", editor);
        Equal(0, buffer.Count, "undo first append restores empty path");
        Equal((uint?)null, buffer.MapId, "undo first append restores unbound map");
    });

    public static Task MissingConfiguredPathsAsync() => RunSta(() =>
    {
        var logger = new InMemoryRoadhogLogger();
        using var form = new AccountSettingsForm("account1",
            new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
            new InMemoryAccountConfigStore(new AccountConfig
            {
                AccountName = "account1", ScriptSettings = new()
                {
                    Paths = new() { RevivePathName = "missing-revive", CombatPathName = "missing-combat" }
                }
            }), new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
        Show(form);
        foreach (var kind in Enum.GetValues<SharedPathKind>())
            Check(!Dirty(form, Editor(form, kind)), "loading missing configured paths is not a user edit");
        form.Close();
        Check(form.IsDisposed, "unchanged form closes without a draft prompt");
    });

    public static Task MetadataSaveAsync() => RunSta(() =>
    {
        var store = new InMemorySharedPathStore(Document());
        using var form = CreateForm(store);
        Show(form);
        var editor = Editor(form, SharedPathKind.Combat);
        var other = Editor(form, SharedPathKind.Revive);
        Invoke(form, "LoadPathByName", editor, "route");
        Invoke(form, "LoadPathByName", other, "route");
        SelectTab(editor);
        Get<Control>(editor, "StationaryRadiusTextBox").Text = "35";
        object?[] arguments = { string.Empty };
        Check((bool)Invoke(form, "SaveSelectedPathRadiusBindings", arguments)!, "save radius metadata");
        Check(!Dirty(form, editor) && !Dirty(form, other), "saved radius alone is no longer dirty on either tab");
        Select(Get<ListView>(editor, "GatherPointsList"), 1);
        SetCoordinates(editor, "100", "200", "300");
        Invoke(form, "EditSelectedPathPoint", editor, "replace");
        Get<Control>(editor, "StationaryRadiusTextBox").Text = "40";
        Check((bool)Invoke(form, "SaveSelectedPathRadiusBindings", arguments)!, "save metadata beside coordinate draft");
        Check(Dirty(form, editor), "saving radius does not clear coordinate draft");
        Check(!Dirty(form, other), "other editor stays clean");
        Equal(3D, Pump(store.LoadAsync("route")).Value!.Points[1].X, "metadata save does not write coordinate draft");
        Equal(100D, Get<PathRecordingBuffer>(editor, "Buffer").Points[1].X, "coordinate draft retained in editor");
    });

    private static SharedPathDocument Document() => new()
    {
        Name = "route", MapId = 47, BoundStationaryCombatRadius = 30, CleanupNpcName = "merchant", AuctionNpcName = "auction",
        BagCleanupSellItemClickX = 123, BagCleanupSellItemClickY = 234, BagCleanupSellButtonClickX = 345, BagCleanupSellButtonClickY = 456,
        Points = new()
        {
            new() { X = 0, Y = 0, Z = 0 },
            new() { X = 3, Y = 4, Z = 0, GatherActions = new() { new() { GatherName = "herb", ExpectedGatherSourceId = 123, GatherKey = "D1" } } },
            new() { X = 12, Y = 0, Z = 0 }
        }
    };

    private static AccountSettingsForm CreateForm(ISharedPathStore store, FakeGameApi? api = null)
    {
        var logger = new InMemoryRoadhogLogger();
        var runtime = new RoadhogRuntime(api ?? new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
        return new("account1", runtime, new InMemoryAccountConfigStore(new AccountConfig
        {
            AccountName = "account1", ScriptSettings = new()
        }), store, new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
    }

    private static void Show(AccountSettingsForm form)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-32000, -32000);
        form.Show();
        ((TabControl)Field(form, "settingsTabs")).SelectedIndex = 1;
        System.Windows.Forms.Application.DoEvents();
    }
    private static void SelectTab(object editor)
    {
        var page = (TabPage)Get<Control>(editor, "Page").Parent!;
        ((TabControl)page.Parent!).SelectedTab = page;
        System.Windows.Forms.Application.DoEvents();
    }
    private static void Select(ListView list, int index)
    {
        list.SelectedIndices.Clear();
        list.Items[index].Selected = true;
        list.Items[index].Focused = true;
        System.Windows.Forms.Application.DoEvents();
    }
    private static void SetCoordinates(object editor, string x, string y, string z)
    {
        var fields = (Control[])Get<object>(editor, "Coordinates");
        fields[0].Text = x; fields[1].Text = y; fields[2].Text = z;
    }
    private static bool Dirty(AccountSettingsForm form, object editor) => (bool)Invoke(form, "HasPathDraftChanges", editor)!;
    private static object Editor(AccountSettingsForm form, SharedPathKind kind) => ((IDictionary)Field(form, "pathEditors"))[kind]!;
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Invoke(AccountSettingsForm form, string name, params object?[] args) => typeof(AccountSettingsForm)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(form, args);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
        Check(task.IsCompleted, "async operation timed out");
        task.GetAwaiter().GetResult();
    }
    private static T Pump<T>(Task<T> task) { Pump((Task)task); return task.GetAwaiter().GetResult(); }
    private static Task RunSta(Action action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); result.SetResult(); } catch (Exception ex) { result.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    private sealed class ControlledStore : ISharedPathStore
    {
        private readonly InMemorySharedPathStore inner = new(Document());
        public bool Fail { get; set; }
        public int Saves { get; private set; }
        public TaskCompletionSource? Gate { get; set; }
        public Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(CancellationToken token = default) => inner.LoadSummariesAsync(token);
        public Task<OperationResult<SharedPathDocument>> LoadAsync(string name, CancellationToken token = default) => inner.LoadAsync(name, token);
        public Task<OperationResult> DeleteAsync(string name, CancellationToken token = default) => inner.DeleteAsync(name, token);
        public async Task<OperationResult> SaveAsync(SharedPathDocument path, CancellationToken token = default)
        {
            Saves++;
            if (Gate is not null) await Gate.Task;
            return Fail ? OperationResult.Fail("simulated save failure") : await inner.SaveAsync(path, token);
        }
    }
}
