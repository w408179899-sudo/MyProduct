namespace Roadhog.Core.Diagnostics;

public sealed class NoOpRoadhogLogger : IRoadhogLogger
{
    public static NoOpRoadhogLogger Instance { get; } = new();

    private NoOpRoadhogLogger()
    {
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }

    public void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }
}
