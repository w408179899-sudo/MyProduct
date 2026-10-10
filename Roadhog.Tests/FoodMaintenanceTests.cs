using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Food;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class FoodMaintenanceTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static InventoryItemSnapshot Item(uint id, string skill, int level, int required = 30) =>
        new(id, id, "arbitrary localized name", 2, 0, false, 15, 1, 0, new(skill, level, required));
    private static PlayerAbnormalStatusSnapshot Status(uint id) =>
        new(1, DateTimeOffset.Now, 0, new[] { new AbnormalStatusEntrySnapshot(0, id, 3, 900000, 3, 0) });

    public static Task CatalogAsync()
    {
        var catalog = FoodCatalog.Default;
        Check(catalog.HasStatus(Status(10062), FoodKind.Drink), "cocktail occupies drink category");
        Check(catalog.HasStatus(Status(10097), FoodKind.Food), "sushi occupies food category despite different effects");
        Check(!catalog.HasStatus(Status(12076), FoodKind.Drink), "other MP regeneration is not a drink");
        Check(!catalog.HasStatus(Status(10044), FoodKind.Food), "categories independent");
        var high = Item(1, "shop_food_hpregen_mpregen", 5, 50);
        var middle = Item(2, "food_hpregen_mpregen", 3);
        var items = new[] { high, middle, Item(3, "shop_food_hpregen_mpregen", 2),
            Item(4, "shop_food_hpregen_mpregen", 3, 60), Item(5, "combo_food_msboost_Maccuracy", 3),
            middle with { InstanceId = 9, Count = 0 }, middle with { InstanceId = 10, IsEquipped = true } };
        Check(catalog.Select(items, FoodKind.Drink, 50) == middle, "lowest eligible level, no low ranks or unusable levels");
        Check(catalog.Select(items, FoodKind.Food, 50)?.TemplateId == 5, "arbitrary item ID and name classify via skill");
        Check(catalog.Select(items, FoodKind.Drink, 20) == null, "character cannot use these items");
        return Task.CompletedTask;
    }

    public static Task ProviderAsync()
    {
        var bytes = new byte[528];
        BitConverter.GetBytes(21u).CopyTo(bytes, 328);
        BitConverter.GetBytes(123UL).CopyTo(bytes, 280);
        bytes[493] = 40; bytes[498] = 4;
        Check(AionVmmGameApi.TryDecodeFoodDefinition(bytes, _ => "food_hpregen_mpregen", out var food) &&
            food == new FoodItemDefinition("food_hpregen_mpregen", 4, 40), "cocktail metadata decode");
        Check(!AionVmmGameApi.TryDecodeFoodDefinition(bytes, _ => null, out _), "failed skill name cannot become no-food");
        Check(!AionVmmGameApi.TryDecodeFoodDefinition(bytes.AsSpan(0, 300), _ => "x", out _), "truncated record rejected");
        BitConverter.GetBytes(0u).CopyTo(bytes, 328);
        Check(AionVmmGameApi.TryDecodeFoodDefinition(bytes, _ => throw new Exception(), out food) && food == null,
            "non-food is a valid null and does not read the skill pointer");
        var old = Item(1, "food_hpregen_mpregen", 3);
        var fields = new InventoryItemFieldValidity(true, true, true, true, true, true, true, true, false);
        var partial = new InventoryReadResult(InventoryReadCompleteness.Partial,
            new[] { new InventoryItemObservation(old with { Count = 1, Food = null }, fields) }, "fault");
        var merged = AionVmmGameApi.MergeInventoryRead(partial, new[] { old });
        Check(merged.Single().Food == old.Food && merged.Single().Count == 1, "valid count updates while failed metadata holds");
        Check(AionVmmGameApi.MergeInventoryRead(partial, Array.Empty<InventoryItemSnapshot>()).Count == 0,
            "cold partial item is not published as non-food");
        var complete = partial with { Completeness = InventoryReadCompleteness.Complete,
            Observations = new[] { partial.Observations[0] with { Fields = fields with { Food = true } } } };
        Check(AionVmmGameApi.MergeInventoryRead(complete, new[] { old }).Single().Food == null, "valid null clears old metadata");
        return Task.CompletedTask;
    }

    public static async Task SequenceAsync()
    {
        var game = new Simulation();
        Check(await game.Run(), "use confirms category status");
        Check(game.CursorVisits.Contains(new GameUiPoint(680, 468)), "drink approaches via fixed reset point");
        Check(game.Clicks == 1 && !game.Open && game.Input.Keys.SequenceEqual(new[] { "I", "I" }), "one click and restore bag");
        Check(game.Input.MouseCommands.Count(c => c == "up:Right") == 1, "single click releases right mouse");
        Check(!await game.Run() && game.Clicks == 1, "existing same-category status never consumes again");
        foreach (var offset in new[] { 0, 1, 15 })
        {
            var stale = new Simulation { RequireHoverReentry = true };
            stale.Api.InventoryUiCursor = new(100 + offset, 100);
            Check(await stale.Run() && stale.LeftItem && stale.Clicks == 1 && !stale.Open,
                "reopened bag requires leaving the item and reentering before one click");
        }
        var opened = new Simulation { Open = true };
        Check(await opened.Run() && opened.Open && opened.Input.Keys.Count == 0, "preserve user-opened bag");
        foreach (var failure in new[] { "combat", "modal", "moved", "changed", "scene", "hp", "hover", "no-status" })
        {
            var bad = new Simulation { Failure = failure };
            try { await bad.Run(); throw new Exception("unexpected success: " + failure); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { }
            Check(bad.Clicks == (failure == "no-status" ? 1 : 0), failure + " guards input, including no extra click batch");
        }
        var cancel = new Simulation(); using var stop = new CancellationTokenSource();
        cancel.Input.AfterMouseDown = _ => stop.Cancel();
        try { await cancel.Run(stop.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Check(cancel.Input.MouseCommands.Last() == "up:Right", "stop releases held mouse");
    }

    public static async Task ControllerAsync()
    {
        var game = new Simulation();
        var settings = new MaintenanceScriptSettings { AutoDrinkEnabled = true };
        var c = new FoodMaintenanceController(game.Input);
        c.ObserveCombat(settings, false); Check(!c.Pending, "no use before combat");
        c.ObserveCombat(settings, true); Check(c.Pending, "combat arms maintenance");
        var logger = new InMemoryRoadhogLogger();
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "test", ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto, Maintenance = settings } },
            game.Api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
        var state = new StationaryCombatState { Fighting = true };
        var prepares = 0;
        Task Prepare() { prepares++; return Task.CompletedTask; }
        Check(!await c.TickAsync(context, state, Prepare, () => false) && prepares == 0, "no inventory input while fighting");
        state.Fighting = false;
        Check(!await c.TickAsync(context, state, Prepare, () => true) && prepares == 0, "cleanup/channel owns input");
        Check(await c.TickAsync(context, state, Prepare, () => false) && game.Clicks == 1 && !c.Pending, "post-combat consumes once");
        var copy = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(new ScriptSettings {
            Maintenance = new() { AutoDrinkEnabled = true, AutoFoodEnabled = true } }))!.Clone();
        Check(copy.Maintenance.AutoDrinkEnabled && copy.Maintenance.AutoFoodEnabled, "both switches survive JSON and clone");
        Check(!new MaintenanceScriptSettings().AutoDrinkEnabled && new MaintenanceScriptSettings().AutoFoodEnabled, "only food enabled by default");
        var legacy = JsonSerializer.Deserialize<MaintenanceScriptSettings>("{}")!;
        Check(!legacy.AutoDrinkEnabled && legacy.AutoFoodEnabled, "missing old configuration fields default to food only");
        var disabled = JsonSerializer.Deserialize<MaintenanceScriptSettings>(
            "{\"AutoDrinkEnabled\":false,\"AutoFoodEnabled\":false}")!.Clone();
        Check(!disabled.AutoDrinkEnabled && !disabled.AutoFoodEnabled, "explicit disabled choices survive load and clone");

        var independent = new Simulation { Failure = "drink-no-status" };
        independent.Api.InventoryItems = independent.Api.InventoryItems.Concat(new[] {
            Item(160003559, "shop_food_phyAttack_msboost", 3) with { Slot = 1 } }).ToArray();
        var both = new MaintenanceScriptSettings { AutoDrinkEnabled = true, AutoFoodEnabled = true };
        var controller = new FoodMaintenanceController(independent.Input,
            (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 200);
        controller.ObserveCombat(both, true);
        var bothContext = new AccountWorkerContext(new AccountConfig { AccountName = "test", ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto, Maintenance = both } },
            independent.Api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
        Check(await controller.TickAsync(bothContext, new(), () => Task.CompletedTask, () => false) &&
            independent.Clicks == 2 && FoodCatalog.Default.HasStatus(independent.Api.PlayerAbnormalStatuses, FoodKind.Food),
            "unconfirmed drink does not starve independent food category");
        Check(!await controller.TickAsync(bothContext, new(), () => Task.CompletedTask, () => false) && independent.Clicks == 2,
            "failed category retry is throttled");
        Check(independent.Input.Keys.SequenceEqual(new[] { "I", "I" }) && !independent.Open,
            "first category failure still shares one inventory session with second category");

        foreach (var scenario in new[] { "both", "already-open", "combat-after-drink", "cancel-after-drink" })
        {
            var batch = new Simulation { Open = scenario == "already-open" };
            batch.Api.InventoryItems = batch.Api.InventoryItems.Concat(new[] {
                Item(160003559, "shop_food_phyAttack_msboost", 3) with { Slot = 1 } }).ToArray();
            using var cancel = new CancellationTokenSource();
            var batchContext = new AccountWorkerContext(new AccountConfig { AccountName = "batch", ScriptSettings = new() { Maintenance = both } },
                batch.Api, logger, new AccountRuntimeManager(logger), new(), cancel.Token);
            var batchState = new StationaryCombatState();
            var clicked = batch.Input.AfterMouseDown;
            batch.Input.AfterMouseDown = button =>
            {
                clicked?.Invoke(button);
                if (scenario == "combat-after-drink") batchState.Fighting = true;
                if (scenario == "cancel-after-drink") cancel.Cancel();
            };
            var batchController = new FoodMaintenanceController(batch.Input);
            batchController.ObserveCombat(both, true);
            try { await batchController.TickAsync(batchContext, batchState, () => Task.CompletedTask, () => false); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            var interrupted = scenario is "combat-after-drink" or "cancel-after-drink";
            Check(batch.Clicks == (interrupted ? 1 : 2), scenario + " respects per-item safety");
            Check(batch.CursorVisits.Count(p => p == new GameUiPoint(680, 468)) == batch.Clicks,
                "each drink and food bag click first visits fixed point");
            var keys = scenario == "already-open" ? Array.Empty<string>() :
                scenario == "cancel-after-drink" ? new[] { "I" } : new[] { "I", "I" };
            Check(batch.Input.Keys.SequenceEqual(keys), scenario + " opens and closes at most once per round");
            Check(batch.Open == (scenario is "already-open" or "cancel-after-drink"), scenario + " preserves inventory ownership");
            Check(batch.Input.MouseCommands.Last() == "up:Right", scenario + " releases right mouse");
        }

        foreach (var mode in new[] { AccountMainMode.CustomCombat, AccountMainMode.SemiAuto })
        {
            var same = new Simulation();
            var clock = new ManualClock();
            var maintenance = new FoodMaintenanceController(same.Input, timeProvider: clock);
            var ctx = new AccountWorkerContext(new AccountConfig { AccountName = "mode", ScriptSettings = new() { MainMode = mode, Maintenance = settings } },
                same.Api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
            maintenance.ObserveCombat(settings, false);
            Check(!maintenance.Pending, "neither mode checks before combat");
            maintenance.ObserveCombat(settings, true);
            Check(await maintenance.TickAsync(ctx, new(), () => Task.CompletedTask, () => false) && !same.Open,
                "both modes consume after combat then close their inventory");
            same.Api.PlayerAbnormalStatuses = Status(12076);
            clock.Advance(6); maintenance.ObserveCombat(settings, true);
            Check(await maintenance.TickAsync(ctx, new(), () => Task.CompletedTask, () => false) && same.Clicks == 2,
                "next battle can trigger maintenance without 910-second schedule");
        }
    }

    public static Task UiAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "test" });
                var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!, store);
                using var form = new AccountSettingsForm("test", runtime, store, new InMemorySharedPathStore(),
                    new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(AccountSettingsForm);
                var load = t.GetMethod("ApplyScriptSettings", flags)!;
                load.Invoke(form, new object[] { new ScriptSettings { Maintenance = new() { AutoDrinkEnabled = true } } });
                Check((bool)t.GetField("autoDrinkCheckBox", flags)!.GetValue(form)!.GetType()
                    .GetProperty("Checked")!.GetValue(t.GetField("autoDrinkCheckBox", flags)!.GetValue(form))!, "saved switch loads");
                // Inspect the actual settings editor and serialization path, without saving an account.
                foreach (var field in new[] { "autoDrinkCheckBox", "autoFoodCheckBox" })
                {
                    var control = (Control)t.GetField(field, flags)!.GetValue(form)!;
                    Check(control != null && control.Text.StartsWith("自动"), "maintenance checkbox exists");
                    control!.GetType().GetProperty("Checked")!.SetValue(control, true);
                }
                var collect = t.GetMethod("CaptureScriptSettings", flags)!;
                var result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.AutoDrinkEnabled && result.Maintenance.AutoFoodEnabled, "editor collects both switches");
                var preference = new FoodQuickbarPreference(160002117, "Cocktail");
                load.Invoke(form, new object[] { new ScriptSettings { Maintenance = new()
                { PreferredDrinks = new() { preference }, PreferredFoods = new() { new(160002158, "Sushi") } } } });
                result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.PreferredDrinks.Single() == preference && result.Maintenance.PreferredFoods.Single().TemplateId == 160002158,
                    "dropdown selections survive editor load and capture even when not currently on bar");
                var serialized = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(result))!.Clone();
                serialized.Maintenance.PreferredDrinks.Clear();
                Check(result.Maintenance.PreferredDrinks.Count == 1, "preference clone owns its list");
                var drink1 = Item(160002117, "food_hpregen_mpregen", 4);
                var drink2 = Item(160003553, "shop_food_hpregen_mpregen", 3);
                var food = Item(160002158, "combo_food_msboost_Maccuracy", 3);
                var items = new[] { drink1, drink2, food };
                var quickbar = new QuickbarSnapshot(0, new[] {
                    new QuickbarSlotSnapshot(SkillQuickbar.Main, 1, 1, 0, drink2.TemplateId, (uint)drink2.InstanceId),
                    new QuickbarSlotSnapshot(SkillQuickbar.Main, 2, 1, 0, food.TemplateId, (uint)food.InstanceId) });
                t.GetMethod("UpdateFoodCandidates", flags)!.Invoke(form, new object[] { quickbar, items });
                var combo = form.Controls.Find("drinkPreferenceCombo", true).Single().Controls.OfType<ComboBox>().Single();
                var key = (Button)form.Controls.Find("drinkPreferenceKey", true).Single();
                Check(combo.Items.Count == 3 && combo.SelectedIndex == 2 && combo.Text.Contains(drink1.TemplateId.ToString()),
                    "single-select lists bag items even without quickbar binding and shows saved item");
                Check(!key.Enabled && key.Text == "\u672a\u653e\u5165\u6280\u80fd\u680f", "unbound selected food displays missing quickbar message");
                combo.SelectedIndex = 1;
                result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.PreferredDrinks.Single().TemplateId == drink2.TemplateId && key.Text.Contains("2"),
                    "selecting another item replaces selection and shows automatic key");
                t.GetMethod("UpdateFoodCandidates", flags)!.Invoke(form, new object[] {
                    new QuickbarSnapshot(0, Array.Empty<QuickbarSlotSnapshot>()), Array.Empty<InventoryItemSnapshot>() });
                result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.PreferredDrinks.Single().TemplateId == drink2.TemplateId && combo.Text.Contains(drink2.TemplateId.ToString()),
                    "selection remains visible after item is consumed or moved off bar");
                combo.SelectedIndex = 0;
                result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.PreferredDrinks.Count == 0, "automatic bag choice clears explicit selection");
                load.Invoke(form, new object[] { new ScriptSettings { Maintenance = new() {
                    PreferredDrinks = new() { preference, new(drink2.TemplateId, drink2.Name) } } } });
                result = (ScriptSettings)collect.Invoke(form, null)!;
                Check(result.Maintenance.PreferredDrinks.Single() == preference, "legacy multiple selections migrate to first item");
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }

    public static async Task WorkerAsync()
    {
        var game = new Simulation();
        game.Api.TargetEntityId = 100;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var config = new AccountConfig { AccountName = "test", MainMode = AccountMainMode.SemiAuto,
            ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto,
                Maintenance = new() { AutoDrinkEnabled = true, SitMaintenanceEnabled = false } } };
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, new InMemorySharedPathStore());
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger),
            new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        var work = new DefaultAccountWorkerLoop(game.Input, semi, combat).RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "semi_auto.plan.loaded"))
            { if (work.IsCompleted) await work; await Task.Delay(10, stop.Token); }
            await Task.Delay(100, stop.Token);
            Check(game.Clicks == 0, "actual worker never eats with live combat target");
            game.Api.TargetEntityId = 0;
            while (!logger.Entries.Any(e => e.EventName == "food.used"))
            { if (work.IsCompleted) await work; await Task.Delay(10, stop.Token); }
            Check(game.Clicks == 1, "actual worker schedules food after target clears");
        }
        finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
    }

    public static async Task QuickbarAsync()
    {
        var catalog = FoodCatalog.Default;
        var game = new Simulation();
        var high = game.Api.InventoryItems.Single();
        var middle = Item(2, "food_hpregen_mpregen", 3);
        game.Api.InventoryItems = new[] { middle, high };
        var slot = new QuickbarSlotSnapshot(SkillQuickbar.Alt, 10, 1, 0, high.TemplateId, (uint)high.InstanceId);
        game.Api.Quickbar = new(2, new[] { slot });
        var preferences = new List<FoodQuickbarPreference> { new(high.TemplateId, high.Name) };
        var binding = FoodQuickbarBindings.Select(game.Api.Quickbar, game.Api.InventoryItems, catalog, FoodKind.Drink, 50, preferences)!;
        Check(binding.Item == high && binding.Key == "NumPadAdd", "selected bar item outranks lower-grade unselected item");
        Check(FoodQuickbarBindings.Select(game.Api.Quickbar, game.Api.InventoryItems, catalog, FoodKind.Food, 50, preferences) == null,
            "drink cannot bind as food");
        Check(FoodQuickbarBindings.Select(game.Api.Quickbar, game.Api.InventoryItems, catalog, FoodKind.Drink, 30, preferences) == null,
            "character must meet selected item level");
        Check(await game.Run(binding: binding) && game.Hotkeys == 1 && game.Clicks == 0 && !game.Open &&
            game.Input.Keys.SequenceEqual(new[] { "NumPadAdd" }), "hotkey confirms without opening inventory or clicking");
        Check(!await game.Run(binding: binding) && game.Hotkeys == 1, "existing category never repeats hotkey");

        foreach (var fault in new[] { "page", "slot", "empty", "modal", "no-status" })
        {
            var bad = new Simulation();
            bad.Api.Quickbar = fault switch
            {
                "page" => new(3, new[] { slot }),
                "slot" => new(2, new[] { slot with { ContentType = 21, SkillId = 100 } }),
                "empty" => new(2, new[] { slot with { ItemInstanceId = 0 } }),
                _ => new(2, new[] { slot })
            };
            bad.Failure = fault;
            try { await bad.Run(binding: binding); throw new Exception("unexpected success: " + fault); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { }
            Check(bad.Hotkeys == (fault == "no-status" ? 1 : 0) && bad.Clicks == 0 && !bad.Open,
                fault + " cannot cause wrong key or blind bag fallback");
        }

        foreach (var available in new[] { true, false })
        {
            var test = new Simulation();
            test.Api.InventoryItems = available ? new[] { middle, high } : new[] { middle };
            test.Api.Quickbar = new(2, new[] { slot });
            var settings = new MaintenanceScriptSettings { AutoDrinkEnabled = true, AutoFoodEnabled = false, PreferredDrinks = preferences };
            var logger = new InMemoryRoadhogLogger();
            var context = new AccountWorkerContext(new AccountConfig { AccountName = "priority", ScriptSettings = new() { Maintenance = settings } },
                test.Api, logger, new AccountRuntimeManager(logger), new(), CancellationToken.None);
            var controller = new FoodMaintenanceController(test.Input);
            controller.ObserveCombat(settings, true);
            Check(await controller.TickAsync(context, new(), () => Task.CompletedTask, () => false), "selected/fallback use confirms");
            Check(test.Hotkeys == (available ? 1 : 0) && test.Clicks == (available ? 0 : 1) && !test.Open,
                "controller prefers selected key and falls back only when item unavailable");
        }
    }

    private sealed class Simulation
    {
        public FakeGameApi Api = new();
        public RecordingKeyboardInput Input = new();
        public bool Open;
        public int Clicks;
        public bool RequireHoverReentry;
        public bool LeftItem;
        public readonly List<GameUiPoint> CursorVisits = new();
        private bool hoverRefreshed;
        public int Hotkeys;
        public string Failure = "";
        private bool moved;
        private readonly InventoryItemSnapshot item = Item(160002117, "food_hpregen_mpregen", 4, 40);
        public Simulation()
        {
            Api.Player = Api.Player with { Level = 50 };
            Api.TargetEntityId = 0;
            Api.TargetIsTargetingLocalPlayer = false;
            Api.TargetServerObjectId = 0;
            Api.InventoryItems = new[] { item };
            Api.TransitionRead = () => new(!(moved && Failure == "scene"), Api.Player,
                Api.Channel with { CapturedAt = DateTimeOffset.Now }, DateTimeOffset.Now);
            Api.InventoryInteractionRead = () => new(Open, false, false,
                Api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count,
                    new(moved && Failure == "moved" ? 300 : 100 + i.Slot * 60, 100))).ToArray(),
                Failure == "hover" || (RequireHoverReentry && !hoverRefreshed) ? 0 : (uint)(Api.InventoryItems.FirstOrDefault(i =>
                    Api.InventoryUiCursor == new GameUiPoint(100 + i.Slot * 60, 100))?.InstanceId ?? 0),
                0, null, null, Failure == "modal");
            Input.AfterPress = key =>
            {
                if (key == "I") { Open = !Open; return; }
                var bound = Api.Quickbar.Slots.FirstOrDefault(s => SkillKeyBindings.GetSlotKey(s.Bar, s.Slot) == key);
                if (bound == null) return;
                Hotkeys++;
                if (Failure != "no-status") Api.PlayerAbnormalStatuses = Status(10062);
            };
            Input.AfterMove = (x, y) =>
            {
                Api.InventoryUiCursor = new(Api.InventoryUiCursor.X + x, Api.InventoryUiCursor.Y + y);
                CursorVisits.Add(Api.InventoryUiCursor);
                if (RequireHoverReentry)
                {
                    if (Math.Abs(Api.InventoryUiCursor.X - 100) > 32 || Math.Abs(Api.InventoryUiCursor.Y - 100) > 32) LeftItem = true;
                    else if (LeftItem) hoverRefreshed = true;
                }
                moved = true;
                if (Failure == "changed") Api.InventoryItems = new[] { item with { Count = 1 } };
                if (Failure == "hp") Api.Player = Api.Player with { CurrentHp = 80 };
            };
            Input.AfterMouseDown = _ =>
            {
                Clicks++;
                var clicked = Api.InventoryItems.Single(i => Api.InventoryUiCursor == new GameUiPoint(100 + i.Slot * 60, 100));
                var drink = clicked.Food!.UseSkillName == "food_hpregen_mpregen";
                if (Failure != "no-status" && !(drink && Failure == "drink-no-status"))
                    Api.PlayerAbnormalStatuses = Status(drink ? 10062u : 10051u);
            };
        }
        public Task<bool> Run(CancellationToken token = default, FoodQuickbarBinding? binding = null) => new FoodUseSequence(Input, FoodCatalog.Default,
            (ms, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 200).RunAsync(
            Api.Create(new AccountConfig(), new InMemoryRoadhogLogger(), token), FoodKind.Drink, item,
            () => Task.FromResult(!(moved && Failure == "combat")), token, binding);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
        public void Advance(int seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
