using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application;

public static class SharedConfigurationRefresh
{
    public static async Task RefreshAsync(ISharedAccountConfiguration shared, ScriptSettings settings,
        bool includeCleanup, CancellationToken token = default)
    {
        var filters = await shared.LoadMonsterFiltersAsync(token).ConfigureAwait(false);
        if (!filters.Success) throw new InvalidDataException("共享怪物过滤读取失败：" + filters.Error);
        if (includeCleanup)
        {
            var lists = await shared.LoadAsync(token).ConfigureAwait(false);
            if (!lists.Success || lists.Value?.Document is not { } document)
                throw new InvalidDataException("共享清包配置读取失败：" + lists.Error);
            document.ApplyTo(settings.Maintenance);
        }
        settings.Combat.ActiveMonsterNameFilters = filters.Value!;
    }

    public static async Task CaptureCleanupAsync(ISharedAccountConfiguration shared, CleanupRequest request,
        CancellationToken token = default)
    {
        if (request.SharedConfigurationLoaded) return;
        var lists = await shared.LoadAsync(token).ConfigureAwait(false);
        if (!lists.Success || lists.Value?.Document is not { } document)
            throw new InvalidDataException("共享清包配置读取失败：" + lists.Error);
        document.ApplyTo(request.Settings.Maintenance);
        request.SharedConfigurationLoaded = true;
    }
}
