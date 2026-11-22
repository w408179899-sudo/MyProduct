using System.Globalization;
using System.Text.Json;
using Roadhog.Infrastructure.Config;

namespace Roadhog;

public sealed partial class MultiAccountForm
{
    private readonly RoundedTextBox _dmaReadCacheInput = new();
    private int? _savedDmaReadCacheMs;
    private bool _savingDmaReadCache;

    private Control BuildDmaReadCacheField()
    {
        var field = new FlowLayoutPanel
        {
            Name = "dmaReadCacheField",
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(24, 5, 0, 0)
        };
        field.Controls.Add(new Label { Text = "缓存 (ms)", AutoSize = true, Margin = new Padding(0, 5, 5, 0) });
        _dmaReadCacheInput.Name = "dmaReadCacheInput";
        _dmaReadCacheInput.AccessibleName = "DMA 读缓存（毫秒）";
        _dmaReadCacheInput.Size = new Size(58, 28);
        _dmaReadCacheInput.Margin = Padding.Empty;
        _dmaReadCacheInput.Text = JsonDmaReadCacheStore.DefaultPeriodMs.ToString(CultureInfo.InvariantCulture);
        _dmaReadCacheInput.Enabled = false;

        var input = _dmaReadCacheInput.Controls.OfType<TextBox>().Single();
        input.MaxLength = 10;
        input.KeyDown += async (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            await RunUiActionAsync(SaveDmaReadCacheAsync);
        };
        input.Leave += async (_, _) => await RunUiActionAsync(SaveDmaReadCacheAsync);
        field.Controls.Add(_dmaReadCacheInput);
        return field;
    }

    private void LoadDmaReadCacheField()
    {
        try
        {
            _savedDmaReadCacheMs = _workspace.DmaReadCache.Load();
            _dmaReadCacheInput.Text = _savedDmaReadCacheMs.Value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _workspace.Logger.Error("manager.dma_read_cache.load_failed", ex);
            Report("读取 DMA 缓存设置失败。" + ex.Message);
        }
        _dmaReadCacheInput.Enabled = true;
    }

    private async Task SaveDmaReadCacheAsync()
    {
        if (_savingDmaReadCache) return;
        if (!int.TryParse(_dmaReadCacheInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var periodMs) ||
            !JsonDmaReadCacheStore.IsValidPeriod(periodMs))
        {
            Report($"缓存请输入 {JsonDmaReadCacheStore.TickPeriodMs} ms 的正整数倍，例如 100 或 150。");
            return;
        }
        if (periodMs == _savedDmaReadCacheMs) return;

        _savingDmaReadCache = true;
        _dmaReadCacheInput.ReadOnly = true;
        try
        {
            await _workspace.DmaReadCache.SaveAsync(periodMs, _operations.Token);
            _savedDmaReadCacheMs = periodMs;
            _dmaReadCacheInput.Text = periodMs.ToString(CultureInfo.InvariantCulture);
            Report($"已保存 DMA 缓存 {periodMs} ms。正在读取的账号后台会自动应用。");
        }
        finally
        {
            _dmaReadCacheInput.ReadOnly = false;
            _savingDmaReadCache = false;
        }
    }
}
