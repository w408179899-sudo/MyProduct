using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class StandaloneShopTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static Task PriceAndRequestAsync()
    {
        foreach (var discount in Enumerable.Range(4, 6))
            Check(DiscountedPersonalShopWorkflow.UnitPrice(20000, discount) == (ulong)discount * 100, "4 to 9 discount prices");
        Check(DiscountedPersonalShopWorkflow.UnitPrice(19999, 5) == 499, "round down once at final price");
        Check(DiscountedPersonalShopWorkflow.UnitPrice(49, 9) == 2, "do not truncate intermediate system price / 20");
        Check(DiscountedPersonalShopWorkflow.UnitPrice(ulong.MaxValue, 9) == (ulong)decimal.Floor(ulong.MaxValue * 9m / 200m), "large prices cannot overflow multiplication");
        Check(DiscountedPersonalShopWorkflow.UnitPrice(0, 5) == 0 && DiscountedPersonalShopWorkflow.UnitPrice(1, 5) == 0, "zero and sub-gold prices skipped");
        foreach (var invalid in new[] { 0, 3, 10, int.MaxValue })
        {
            try { DiscountedPersonalShopWorkflow.UnitPrice(20000, invalid); throw new Exception("accepted invalid discount"); }
            catch (ArgumentOutOfRangeException) { }
        }
        var settings = new ScriptSettings();
        settings.Maintenance.CleanupWorkflow = new() { NpcCleanup = true, Auction = true, TransferGold = true, StandaloneShopDiscount = 9 };
        settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "石", UnitPrice = 99999 });
        settings.Maintenance.BagCleanupExcludedItemNames.Add("石");
        settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("石");
        var item = new InventoryItemSnapshot(1, 11, "魔石", 3, 0, false, VendorSellUnitPrice: 20000);
        var reports = new List<string>();
        var plan = DiscountedPersonalShopWorkflow.Plan(new[] { item, item with { InstanceId = 12, Name = "书" },
            item with { InstanceId = 13, IsEquipped = true }, item with { InstanceId = 14, Slot = -1 },
            item with { InstanceId = 15, Count = 0 }, item with { InstanceId = 16, VendorSellUnitPrice = 0 },
            item with { InstanceId = 17, VendorSellUnitPrice = 1 } }, settings.Maintenance, reports.Add);
        Check(plan.Count == 1 && plan[0].UnitPrice == 900 && plan[0].Item.Count == 3 && reports.Count == 2,
            "stall keyword only, ignore fixed price and other lists, exclude invalid inventory and price");
        var mailbox = new CleanupRequestMailbox();
        Check(mailbox.Request(settings, true, standaloneShop: true).Success, "request accepted");
        var request = mailbox.Current!;
        Check(request.StandaloneShop && request.Settings.Maintenance.CleanupWorkflow is { PersonalShop: true, NpcCleanup: false, Auction: false, TransferGold: false }, "only standalone shop enabled");
        Check(!mailbox.Request(settings, true, standaloneShop: true).Success, "duplicate task rejected");
        Check(settings.Maintenance.CleanupWorkflow is { PersonalShop: false, NpcCleanup: true, Auction: true, TransferGold: true }, "saved cleanup stages unchanged");
        settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 4;
        Check(request.Settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 9, "task captures settings");
        var restored = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(settings.Clone()))!;
        Check(restored.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 4, "discount clone and JSON roundtrip");
        Check(JsonSerializer.Deserialize<ScriptSettings>("{}")!.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 5, "legacy default five discount");
        mailbox.Complete(); settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 10;
        Check(!mailbox.Request(settings, true, standaloneShop: true).Success && mailbox.Current == null, "invalid request never queued");
        return Task.CompletedTask;
    }

    public static async Task EmptyAndCancelAsync()
    {
        var api = new FakeGameApi();
        var logger = new InMemoryRoadhogLogger();
        var settings = new MaintenanceScriptSettings();
        var resumed = false; var sold = false;
        var reader = api.Create(new(), logger, default);
        try
        {
            await DiscountedPersonalShopWorkflow.RunAsync(reader, settings, _ => { sold = true; return Task.CompletedTask; },
                () => { resumed = true; return Task.CompletedTask; }, _ => {}, default);
            throw new Exception("empty plan accepted");
        }
        catch (InvalidOperationException) { }
        Check(!sold && !resumed, "empty plan never claims sold out");
        settings.BagCleanupStallItems.Add(new() { Name = "item" });
        api.InventoryItems = new[] { new InventoryItemSnapshot(1, 11, "item", 1, 0, false, VendorSellUnitPrice: 20000) };
        using var stop = new CancellationTokenSource();
        try
        {
            await DiscountedPersonalShopWorkflow.RunAsync(reader, settings, _ => { stop.Cancel(); return Task.CompletedTask; },
                () => { resumed = true; return Task.CompletedTask; }, _ => {}, stop.Token);
            throw new Exception("cancelled task resumed");
        }
        catch (OperationCanceledException) { }
        Check(!resumed, "stop after settlement still blocks return");
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var settings = new ScriptSettings();
                settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 8;
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "shop-ui", ScriptSettings = settings });
                ScriptSettings? submitted = null;
                using var form = new AccountSettingsForm("shop-ui", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths",
                    startStandaloneShop: value => { submitted = value; return Task.FromResult(OperationResult.Ok()); });
                form.ShowInTaskbar = false; form.Location = new(-32000,-32000); form.StartPosition = FormStartPosition.Manual;
                form.Show(); Application.DoEvents();
                var tabs = form.Controls.OfType<TabControl>().Single();
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "清包"); Application.DoEvents();
                var comboControl = form.Controls.Find("standaloneShopDiscount", true).Single();
                var combo = comboControl.Controls.OfType<ComboBox>().Single();
                var button = (Button)form.Controls.Find("standaloneShopButton", true).Single();
                Check(combo.Items.Count == 6 && combo.SelectedIndex == 4 && button.Enabled && button.Visible, "UI loads eight discount and exposes action");
                Check(button.Bottom <= button.Parent!.Height && !button.Bounds.IntersectsWith(comboControl.Bounds), "controls fit and do not overlap");
                combo.SelectedIndex = 2;
                var captured = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
                Check(captured.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 6, "UI captures selected six discount");
                button.PerformClick(); Application.DoEvents();
                Check(submitted?.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 6, "button submits selected discount through account task callback");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single();
                Check(saved.ScriptSettings!.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 6, "button persists selection before submitting");
                var preview = Environment.GetEnvironmentVariable("ROADHOG_SHOP_PREVIEW");
                if (!string.IsNullOrEmpty(preview))
                {
                    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new(System.Drawing.Point.Empty, form.Size)); bitmap.Save(preview);
                }
                form.Close(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
