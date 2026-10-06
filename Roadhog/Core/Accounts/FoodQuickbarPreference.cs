namespace Roadhog.Core.Accounts;

/// <summary>Persist item identity; resolve its current slot and key at use time.</summary>
public sealed record FoodQuickbarPreference(uint TemplateId, string Name);
