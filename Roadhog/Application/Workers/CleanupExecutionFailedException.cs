namespace Roadhog.Application.Workers;

/// <summary>End this cleanup attempt without replaying its actions; the worker continues normal work.</summary>
public sealed class CleanupExecutionFailedException(string message, Exception inner) : Exception(message, inner);
