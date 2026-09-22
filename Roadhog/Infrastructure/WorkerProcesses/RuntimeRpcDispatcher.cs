using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Roadhog.Application;

namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>Dispatches only the UI runtime contract and confines every call to this worker's binding.</summary>
public sealed class RuntimeRpcDispatcher
{
    private static readonly IReadOnlyDictionary<string, MethodInfo> Methods = typeof(IRoadhogRuntime)
        .GetMethods().ToDictionary(method => method.Name, StringComparer.Ordinal);
    private readonly IRoadhogRuntime _runtime;
    private readonly string _accountName;
    private readonly string _vmmDeviceName;

    public RuntimeRpcDispatcher(IRoadhogRuntime runtime, string accountName, string vmmDeviceName)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        _runtime = runtime;
        _accountName = accountName;
        _vmmDeviceName = vmmDeviceName;
    }

    public async Task<object?> InvokeAsync(string method, JsonElement[] arguments,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Methods.TryGetValue(method, out var target))
            throw new InvalidOperationException("Unknown account runtime operation: " + method);
        var parameters = target.GetParameters();
        var dataParameters = parameters.Where(parameter => parameter.ParameterType != typeof(CancellationToken)
            && parameter.ParameterType != typeof(IProgress<string>)).ToArray();
        if (arguments.Length != dataParameters.Length)
            throw new InvalidOperationException("The account runtime operation has an invalid argument count.");

        var values = new object?[parameters.Length];
        var next = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(CancellationToken)) values[i] = cancellationToken;
            else if (parameter.ParameterType == typeof(IProgress<string>)) values[i] = progress;
            else
            {
                var value = arguments[next++].Deserialize(parameter.ParameterType, WorkerRpcProtocol.Json);
                if (parameter.Name == "accountName")
                {
                    if (value is string account && !string.IsNullOrWhiteSpace(account)
                        && !string.Equals(account, _accountName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("This worker cannot access another account.");
                    value = _accountName;
                }
                else if (parameter.Name == "vmmDeviceName"
                    && !string.Equals(value as string, _vmmDeviceName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This worker cannot access another DMA device.");
                values[i] = value;
            }
        }

        object? result;
        try { result = target.Invoke(_runtime, values); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        if (result is not Task task) return result;
        await task.ConfigureAwait(false);
        return target.ReturnType.IsGenericType ? target.ReturnType.GetProperty("Result")!.GetValue(task) : null;
    }
}
