namespace Roadhog.Core.Common;

public sealed class OperationResult
{
    private OperationResult(bool success, string? error)
    {
        Success = success;
        Error = error;
    }

    public bool Success { get; }

    public string? Error { get; }

    public static OperationResult Ok()
    {
        return new OperationResult(true, null);
    }

    public static OperationResult Fail(string error)
    {
        return new OperationResult(false, string.IsNullOrWhiteSpace(error) ? "Operation failed." : error);
    }
}

public sealed class OperationResult<T>
{
    private OperationResult(bool success, T? value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    public bool Success { get; }

    public T? Value { get; }

    public string? Error { get; }

    public static OperationResult<T> Ok(T value)
    {
        return new OperationResult<T>(true, value, null);
    }

    public static OperationResult<T> Fail(string error)
    {
        return new OperationResult<T>(false, default, string.IsNullOrWhiteSpace(error) ? "Operation failed." : error);
    }
}
