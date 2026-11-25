using System.Text.Json;

namespace Roadhog;

public sealed partial class MultiAccountForm
{
    private Task OpenGlobalSettingsAsync()
    {
        var store = _workspace.GlobalSettings;
        var settings = store.Current;
        string? warning = null;
        try { settings = store.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _workspace.Logger.Warn("manager.global_settings.load_failed", new Dictionary<string, object?> { ["error"] = ex.Message });
            warning = "读取全局设置失败，正在显示最近可用的值。";
        }

        using var dialog = new GlobalSettingsForm(settings, store.SaveAsync, _operations.Token, warning);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Report("全局设置已保存。账号后台会自动读取，日志设置在下次整理时应用。");
        return Task.CompletedTask;
    }
}
