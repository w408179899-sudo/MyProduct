using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Hardware.KmBox;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Input;

internal static class KeyboardPickerChordTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static Task SelectionPersistenceAndInputAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var settings = new ScriptSettings();
                settings.Paths.TownReturnKey = "NumPad8";
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "picker", ScriptSettings = settings });
                using var form = new AccountSettingsForm("picker",
                    new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm)
                    .GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(form, args);
                var returnButton = (Button)typeof(AccountSettingsForm)
                    .GetField("townReturnKeyButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                var device = DispatchProxy.Create<IKmBoxDevice, QuickbarChordTests.Device>();
                var recorder = (QuickbarChordTests.Device)device;
                using var input = new KmBoxNetKeyboardInput(new(), device);
                var primaryKeys = new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus" };
                var labels = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

                foreach (var (label, modifier, hid) in new[] { ("A+", "Alt+", 226), ("C+", "Ctrl+", 224) })
                for (var slot = 0; slot < primaryKeys.Length; slot++)
                {
                    string? selected = null;
                    using var dialog = (Form)Call("CreateKeyboardPickerDialog", returnButton.Tag as string,
                        "选择技能按键", (Action<string>)(key => selected = key))!;
                    dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-32000, -32000);
                    dialog.Show(); System.Windows.Forms.Application.DoEvents();
                    var keys = dialog.Controls.OfType<Button>().Where(b => b.Tag is string).ToArray();
                    Check(keys.Length == 63 && keys.Select(b => (string)b.Tag!).Distinct().Count() == 63, "all 39 single keys and 24 chords remain distinct");
                    Check(keys.All(b => !((string)b.Tag!).StartsWith("NumPad")), "picker has no numpad row");
                    Check(keys.Single(b => b.Text == "C+1").Top < keys.Single(b => b.Text == "A+1").Top &&
                        keys.Single(b => b.Text == "A+1").Top < keys.Single(b => b.Text == "1").Top,
                        "remaining row order stays Ctrl, Alt, numbers");
                    var cancel = dialog.Controls.OfType<Button>().Single(b => b.Text == "取消");
                    foreach (var key in keys)
                        Check(key.Right <= dialog.ClientSize.Width && key.Bottom <= cancel.Top && key.Left >= 0, "all five rows fit above cancel");
                    Check(dialog.Controls.OfType<Label>().Any(l => l.Text == "A = Alt，C = Ctrl"), "modifier legend is visible");
                    if (slot == 0 && label == "A+" && Environment.GetEnvironmentVariable("ROADHOG_TEST_KEYBOARD_PICKER_IMAGE") is { Length: > 0 } imagePath)
                    {
                        using var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
                        dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
                        image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var button = keys.Single(b => b.Text == label + labels[slot]);
                    button.PerformClick();
                    Check(selected == modifier + primaryKeys[slot] && dialog.DialogResult == DialogResult.OK, "clicked label resolves canonical chord");
                    Call("SetKeyButton", returnButton, selected);
                    Check(returnButton.Text == label + labels[slot], "settings button displays short chord label");
                    Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "manual chord saves");
                    var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                    var restored = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(saved))!.Clone();
                    Check(restored.Paths.TownReturnKey == selected, "manual chord persists");
                    Call("ApplyScriptSettings", restored);
                    Check((string?)returnButton.Tag == selected && returnButton.Text == label + labels[slot], "reload retains executable key and display");
                    recorder.Calls.Clear();
                    Check(input.PressKeyAsync(restored.Paths.TownReturnKey, TimeSpan.FromMilliseconds(25)).GetAwaiter().GetResult().Success,
                        "persisted selection reaches production input");
                    var primary = slot < 10 ? 30 + slot : slot == 10 ? 45 : 46;
                    Check(recorder.Calls.Where(c => !c.StartsWith("Connect")).SequenceEqual(new[]
                        { $"KeyDownAsync:{hid}", $"KeyDownAsync:{primary}", $"KeyUpAsync:{primary}", $"KeyUpAsync:{hid}" }),
                        "selected modifier is sent separately before primary and both are released");
                }

                // Exercise the real modal return contract, including cancellation of an existing chord.
                foreach (var choice in new[] { "A+1", "取消" })
                {
                    using var timer = new System.Windows.Forms.Timer { Interval = 20 };
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    var clicked = false;
                    timer.Tick += (_, _) =>
                    {
                        var dialog = System.Windows.Forms.Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Name == "KeyboardPickerForm");
                        if (dialog is null) return;
                        dialog.Location = new(-32000, -32000);
                        timer.Stop();
                        if (DateTime.UtcNow > deadline) { dialog.Close(); return; }
                        clicked = true;
                        dialog.Controls.OfType<Button>().Single(b => b.Text == choice).PerformClick();
                    };
                    timer.Start();
                    var result = (string?)Call("ShowKeyboardPicker", "Ctrl+OemPlus", "选择技能按键");
                    Check(clicked && result == (choice == "取消" ? null : "Alt+D1"), "real modal picker returns selection or cancellation");
                    Check((string?)returnButton.Tag == "Ctrl+OemPlus", "opening and cancelling does not rewrite settings");
                }
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
}
