namespace Roadhog.Application.Workers;

/// <summary>A cleanup cannot safely continue or replay. End the worker instead of leaving it idle.</summary>
public sealed class CleanupExecutionFailedException(string message, Exception inner) : Exception(message, inner);
