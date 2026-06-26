using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Diagnostics;

public sealed class CompositeRoadhogLogger : IRoadhogLogger
{
    private readonly IReadOnlyList<IRoadhogLogger> _loggers;

    public CompositeRoadhogLogger(params IRoadhogLogger[] loggers)
    {
        _loggers = loggers.Where(logger => logger is not null).ToArray();
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Info(eventName, fields);
        }
    }

    public void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Warn(eventName, fields);
        }
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Error(eventName, exception, fields);
        }
    }
}
