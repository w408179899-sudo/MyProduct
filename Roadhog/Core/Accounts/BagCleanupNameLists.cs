using Roadhog.Core.Common;

namespace Roadhog.Core.Accounts;

public sealed class BagCleanupNameListsDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<string> Whitelist { get; set; } = new();

    public List<string> Blacklist { get; set; } = new();

    public BagCleanupNameListsDocument Clone()
    {
        return new BagCleanupNameListsDocument
        {
            Version = CurrentVersion,
            Whitelist = NormalizeKeywords(Whitelist),
            Blacklist = NormalizeKeywords(Blacklist)
        };
    }

    public void ApplyTo(MaintenanceScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.BagCleanupExcludedItemNames = NormalizeKeywords(Whitelist);
        settings.BagCleanupDiscardItemNameKeywords = NormalizeKeywords(Blacklist);
    }

    public static BagCleanupNameListsDocument FromSettings(MaintenanceScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new BagCleanupNameListsDocument
        {
            Whitelist = NormalizeKeywords(settings.BagCleanupExcludedItemNames),
            Blacklist = NormalizeKeywords(settings.BagCleanupDiscardItemNameKeywords)
        };
    }

    public static List<string> NormalizeKeywords(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }
}

public enum BagCleanupNameListsSource
{
    None,
    Json,
    LegacyText
}

public sealed class BagCleanupNameListsLoadResult
{
    public BagCleanupNameListsLoadResult(
        BagCleanupNameListsDocument? document,
        BagCleanupNameListsSource source)
    {
        Document = document?.Clone();
        Source = source;
    }

    public BagCleanupNameListsDocument? Document { get; }

    public BagCleanupNameListsSource Source { get; }

    public bool Found => Source != BagCleanupNameListsSource.None && Document is not null;
}

public interface IBagCleanupNameListStore
{
    string FilePath { get; }

    Task<OperationResult<BagCleanupNameListsLoadResult>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        BagCleanupNameListsDocument document,
        CancellationToken cancellationToken = default);
}
