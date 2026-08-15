namespace Roadhog.Core.Api;

/// <summary>
/// One validated value published by the lower read layer.  Business code only
/// receives this type; raw read quality and retry details never cross the
/// boundary.
/// </summary>
public sealed record PublishedGameSnapshot<T>(long Version, T Value);
