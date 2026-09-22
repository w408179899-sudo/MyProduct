using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class WorkerRpcTests
{
    public static async Task RunAllAsync()
    {
        await ResultProgressAndErrorsAsync();
        await CancellationAndConcurrencyAsync();
        await DisconnectCancelsServerAsync();
        await AuthenticationAndMalformedFramesAsync();
        await BoundRuntimeCatalogAsync();
        await LazyClientReconnectsAsync();
        await NotificationDoesNotStartWorkerAsync();
    }

    public static async Task ResultProgressAndErrorsAsync()
    {
        await using var fixture = new ServerFixture((method, arguments, progress, token) =>
        {
            if (method == "throw") throw new InvalidOperationException("diagnostic survives RPC");
            if (method == "failure") return Task.FromResult<object?>(OperationResult<string>.Fail("action rejected"));
            if (method == "plain") return Task.FromResult<object?>(OperationResult.Ok());
            progress.Report("first");
            progress.Report("second");
            return Task.FromResult<object?>(OperationResult<string>.Ok(arguments[0].GetString()!));
        });
        var events = new List<string>();
        var result = await fixture.Client.CallAsync<OperationResult<string>>("success", new object?[] { "中文结果" },
            fixture.Token, new InlineProgress(events.Add));
        Require(result.Success && result.Value == "中文结果", "generic operation results must preserve value");
        Require(events.SequenceEqual(new[] { "first", "second" }), "progress must arrive in order before completion");
        var failure = await fixture.Client.CallAsync<OperationResult<string>>("failure", Array.Empty<object?>(), fixture.Token);
        Require(!failure.Success && failure.Error == "action rejected", "domain failures must preserve their error");
        Require((await fixture.Client.CallAsync<OperationResult>("plain", Array.Empty<object?>(), fixture.Token)).Success,
            "non-generic private-constructor results must deserialize");
        await ExpectAsync<WorkerRpcException>(() => fixture.Client.CallAsync<object>("throw", Array.Empty<object?>(), fixture.Token),
            "diagnostic survives RPC");
    }

    public static async Task CancellationAndConcurrencyAsync()
    {
        var started = Completion();
        var cancelled = Completion();
        await using var fixture = new ServerFixture(async (method, _, _, token) =>
        {
            if (method != "slow") return "responsive";
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); }
            return null;
        });
        using var cancel = new CancellationTokenSource();
        var slow = fixture.Client.CallAsync<string>("slow", Array.Empty<object?>(), cancel.Token);
        await started.Task.WaitAsync(fixture.Token);
        var status = await fixture.Client.CallAsync<string>("status", Array.Empty<object?>(), fixture.Token)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Require(status == "responsive", "a slow read must not block another status or stop request");
        cancel.Cancel();
        await ExpectAsync<OperationCanceledException>(() => slow);
        await cancelled.Task.WaitAsync(fixture.Token);
        Require(await fixture.Client.CallAsync<string>("status", Array.Empty<object?>(), fixture.Token) == "responsive",
            "cancelling one request must leave the worker usable");
    }

    public static async Task DisconnectCancelsServerAsync()
    {
        var started = Completion();
        var cancelled = Completion();
        await using var fixture = new ServerFixture(async (_, _, _, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); }
            return null;
        });
        using (var pipe = new NamedPipeClientStream(".", fixture.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await pipe.ConnectAsync(fixture.Token);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { kind = "request", token = fixture.Secret, method = "slow", arguments = Array.Empty<object>() });
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await pipe.WriteAsync(header, fixture.Token);
            await pipe.WriteAsync(payload, fixture.Token);
            await pipe.FlushAsync(fixture.Token);
            await started.Task.WaitAsync(fixture.Token);
        }
        await cancelled.Task.WaitAsync(fixture.Token);
    }

    public static async Task AuthenticationAndMalformedFramesAsync()
    {
        var count = 0;
        await using var fixture = new ServerFixture((_, _, _, _) =>
        {
            Interlocked.Increment(ref count);
            return Task.FromResult<object?>("ok");
        });
        var intruder = new WorkerRpcClient(fixture.PipeName, "wrong-token");
        await ExpectAsync<WorkerRpcException>(() => intruder.CallAsync<object>("status", Array.Empty<object?>(), fixture.Token), "authentication");
        Require(count == 0, "unauthenticated requests must never invoke the handler");
        foreach (var size in new[] { -1, 0, 16 * 1024 * 1024 + 1 })
        {
            using var pipe = new NamedPipeClientStream(".", fixture.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(fixture.Token);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, size);
            await pipe.WriteAsync(header, fixture.Token);
            await pipe.FlushAsync(fixture.Token);
            var buffer = new byte[1];
            try { Require(await pipe.ReadAsync(buffer, fixture.Token) == 0, "malformed request must close its pipe"); }
            catch (IOException) { }
        }
        Require(await fixture.Client.CallAsync<string>("status", Array.Empty<object?>(), fixture.Token) == "ok",
            "malformed requests must not terminate the server");
        Require(count == 1, "only authenticated valid requests may invoke the handler");
    }

    public static async Task BoundRuntimeCatalogAsync()
    {
        var fake = DispatchProxy.Create<IRoadhogRuntime, RecordingRuntime>();
        var recorder = (RecordingRuntime)(object)fake;
        var dispatcher = new RuntimeRpcDispatcher(fake, "account1", "fpga://devindex=7");
        await using var fixture = new ServerFixture(dispatcher.InvokeAsync);
        var proxy = new RemoteRoadhogRuntime(fixture.Client, "account1");
        foreach (var method in typeof(IRoadhogRuntime).GetMethods())
        {
            recorder.LastMethod = null;
            var args = method.GetParameters().Select(parameter => parameter.Name switch
            {
                "accountName" => (object?)"account1",
                "vmmDeviceName" => "fpga://devindex=7",
                _ when parameter.ParameterType == typeof(CancellationToken) => fixture.Token,
                _ when parameter.ParameterType == typeof(IProgress<string>) => new InlineProgress(_ => { }),
                _ => Sample(parameter.ParameterType)
            }).ToArray();
            var returned = method.Invoke(proxy, args);
            if (returned is Task task)
            {
                await task.WaitAsync(fixture.Token);
                if (method.ReturnType.IsGenericType)
                {
                    var actual = method.ReturnType.GetProperty("Result")!.GetValue(task);
                    var expected = Sample(method.ReturnType.GenericTypeArguments[0]);
                    Require(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected),
                        method.Name + " must preserve its result DTO through serialization");
                }
            }
            Require(recorder.LastMethod?.Name == method.Name, method.Name + " must route to its own runtime operation");
            foreach (var parameter in method.GetParameters().Where(p => p.Name == "accountName"))
                Require((string?)recorder.LastArguments![parameter.Position] == "account1", "bound account must survive serialization");
        }
        await proxy.ReadPlayerAsync(null, fixture.Token);
        Require((string?)recorder.LastArguments![0] == "account1", "omitted account must resolve to worker binding");
        await ExpectAsync<InvalidOperationException>(() => proxy.ReadPlayerAsync("account2", fixture.Token), "different account");
        await ExpectAsync<WorkerRpcException>(() => fixture.Client.CallAsync<object>("ReadPlayerAsync", new object?[] { "account2" }, fixture.Token), "another account");
        await ExpectAsync<WorkerRpcException>(() => fixture.Client.CallAsync<object>("ReadPlayerForVmmDeviceAsync", new object?[] { "account1", "fpga://devindex=8" }, fixture.Token), "another DMA");
        await ExpectAsync<WorkerRpcException>(() => fixture.Client.CallAsync<object>("GetType", Array.Empty<object?>(), fixture.Token), "Unknown");
        await ExpectAsync<WorkerRpcException>(() => fixture.Client.CallAsync<object>("ReadPlayerAsync", Array.Empty<object?>(), fixture.Token), "argument count");
    }

    public static async Task LazyClientReconnectsAsync()
    {
        await using var first = new ServerFixture((_, _, _, _) => Task.FromResult<object?>(1));
        await using var second = new ServerFixture((_, _, _, _) => Task.FromResult<object?>(2));
        var current = first.Client;
        var resolutions = 0;
        var lazy = new WorkerRpcClient(token => { token.ThrowIfCancellationRequested(); resolutions++; return Task.FromResult(current); });
        Require(resolutions == 0, "opening a settings proxy must not create a hardware worker");
        Require(await lazy.CallAsync<int>("status", Array.Empty<object?>(), first.Token) == 1, "first request must resolve current worker");
        current = second.Client;
        Require(await lazy.CallAsync<int>("status", Array.Empty<object?>(), second.Token) == 2 && resolutions == 2,
            "requests after worker restart must resolve a new endpoint");
    }

    public static Task NotificationDoesNotStartWorkerAsync()
    {
        var resolved = false;
        var notifications = new List<string>();
        RadarObstacleScriptSettings? queued = null;
        var lazy = new WorkerRpcClient(_ => { resolved = true; throw new InvalidOperationException("No hardware configured"); });
        var proxy = new RemoteRoadhogRuntime(lazy, "account1", (method, arguments) =>
        {
            notifications.Add(method);
            if (method == "ApplyRadarObstacleSettings") queued = (RadarObstacleScriptSettings)arguments[1]!;
        });
        var settings = new RadarObstacleScriptSettings { Enabled = true };
        proxy.ApplyRadarObstacleSettings("account1", settings);
        settings.Enabled = false;
        proxy.NotifyRadarMapSaved(47);
        Require(!resolved && notifications.SequenceEqual(new[] { "ApplyRadarObstacleSettings", "NotifyRadarMapSaved" }),
            "saving settings must enqueue notifications without resolving or launching a hardware worker");
        Require(queued?.Enabled == true, "queued settings must be a snapshot, unaffected by subsequent UI edits");
        return Task.CompletedTask;
    }

    private static object? Sample(Type type, int depth = 0)
    {
        if (depth > 12) throw new InvalidOperationException("Recursive test DTO: " + type);
        if (type == typeof(string)) return "sample";
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        if (type == typeof(DateTime)) return new DateTime(2026, 9, 22);
        if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(2);
        if (type == typeof(BagCleanupTradeItemConfig)) return new BagCleanupTradeItemConfig { Name = "sample", UnitPrice = 17 };
        if (Nullable.GetUnderlyingType(type) is not null) return null;
        if (type == typeof(bool)) return true;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type.IsPrimitive) return Convert.ChangeType(7, type);
        if (type == typeof(OperationResult)) return OperationResult.Ok();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(OperationResult<>))
            return type.GetMethod("Ok")!.Invoke(null, new[] { Sample(type.GenericTypeArguments[0], depth + 1) });
        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, 1);
            array.SetValue(Sample(type.GetElementType()!, depth + 1), 0);
            return array;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var array = Array.CreateInstance(type.GenericTypeArguments[0], 1);
            array.SetValue(Sample(type.GenericTypeArguments[0], depth + 1), 0);
            return array;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GenericTypeArguments));
        var constructor = type.GetConstructors().OrderBy(value => value.GetParameters().Length).FirstOrDefault()
            ?? throw new InvalidOperationException("No sample constructor for " + type);
        return constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType, depth + 1)).ToArray());
    }

    public class RecordingRuntime : DispatchProxy
    {
        public MethodInfo? LastMethod { get; set; }
        public object?[]? LastArguments { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            LastMethod = targetMethod!;
            LastArguments = args;
            foreach (var progress in args!.OfType<IProgress<string>>()) progress.Report("runtime progress");
            if (targetMethod!.ReturnType == typeof(void)) return null;
            var valueType = targetMethod.ReturnType.GenericTypeArguments[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(valueType)
                .Invoke(null, new[] { Sample(valueType) });
        }
    }

    private sealed class ServerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(15));
        private readonly Task _server;
        public string PipeName { get; } = "roadhog-rpc-test-" + Guid.NewGuid().ToString("N");
        public string Secret { get; } = Guid.NewGuid().ToString("N");
        public CancellationToken Token => _lifetime.Token;
        public WorkerRpcClient Client { get; }
        public ServerFixture(Func<string, JsonElement[], IProgress<string>, CancellationToken, Task<object?>> handler)
        {
            _server = new WorkerRpcServer(PipeName, Secret, handler).RunAsync(_lifetime.Token);
            Client = new WorkerRpcClient(PipeName, Secret);
        }
        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await _server.WaitAsync(TimeSpan.FromSeconds(3));
            _lifetime.Dispose();
        }
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static async Task ExpectAsync<T>(Func<Task> action, string? contains = null) where T : Exception
    {
        try { await action(); }
        catch (T exception)
        {
            Require(contains is null || exception.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), "Exception lost expected diagnostic: " + exception.Message);
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
