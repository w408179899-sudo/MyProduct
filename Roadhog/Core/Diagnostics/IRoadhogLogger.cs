namespace Roadhog.Core.Diagnostics;

public interface IRoadhogLogger
{
    void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null);

    void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null);

    void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null);
}
