using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.WorkerProcesses;

internal sealed record WorkerRpcFrame
{
    public string Kind { get; init; } = string.Empty;
    public string? Token { get; init; }
    public string? Method { get; init; }
    public JsonElement[] Arguments { get; init; } = Array.Empty<JsonElement>();
    public JsonElement? Value { get; init; }
    public string? Message { get; init; }
}

internal static class WorkerRpcProtocol
{
    internal const int MaximumFrameBytes = 16 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new OperationResultJsonConverterFactory());
        return options;
    }

    internal static async Task WriteAsync(Stream stream, WorkerRpcFrame frame, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        if (bytes.Length > MaximumFrameBytes)
            throw new InvalidDataException("Worker RPC response exceeds the maximum frame size.");
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<WorkerRpcFrame?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumFrameBytes)
            throw new InvalidDataException("Worker RPC frame length is invalid.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<WorkerRpcFrame>(bytes, Json)
            ?? throw new InvalidDataException("Worker RPC frame is empty.");
    }
}

internal sealed class OperationResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(OperationResult)
        || typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(OperationResult<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(OperationResult)) return new ResultConverter();
        return (JsonConverter)Activator.CreateInstance(typeof(ResultConverter<>).MakeGenericType(typeToConvert.GenericTypeArguments[0]))!;
    }

    private sealed class ResultConverter : JsonConverter<OperationResult>
    {
        public override OperationResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return root.GetProperty("success").GetBoolean() ? OperationResult.Ok()
                : OperationResult.Fail(root.GetProperty("error").GetString() ?? "Operation failed.");
        }

        public override void Write(Utf8JsonWriter writer, OperationResult value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("success", value.Success);
            writer.WriteString("error", value.Error);
            writer.WriteEndObject();
        }
    }

    private sealed class ResultConverter<T> : JsonConverter<OperationResult<T>>
    {
        public override OperationResult<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return root.GetProperty("success").GetBoolean()
                ? OperationResult<T>.Ok(root.GetProperty("value").Deserialize<T>(options)!)
                : OperationResult<T>.Fail(root.GetProperty("error").GetString() ?? "Operation failed.");
        }

        public override void Write(Utf8JsonWriter writer, OperationResult<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("success", value.Success);
            writer.WriteString("error", value.Error);
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteEndObject();
        }
    }
}
