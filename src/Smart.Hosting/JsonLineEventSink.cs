using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Smart.Contracts;
namespace Smart.Hosting;

public interface IDeferredEventSink : IEventSink { void WriteDeferred(Func<DiagnosticEvent> create); }

// Bounded, non-blocking producers; disk writes happen only on this background consumer.
public sealed class JsonLineEventSink : IDeferredEventSink, IAsyncDisposable
{
    private readonly record struct PendingEvent(DiagnosticEvent? Value, Func<DiagnosticEvent>? Create);
    private readonly Channel<PendingEvent> _queue;
    private readonly Task _writer;
    private readonly string _directory;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;
    private readonly string _runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    private long _dropped;
    private string? _writeError;
    public long DroppedEvents => Interlocked.Read(ref _dropped);
    public string? WriteError => Volatile.Read(ref _writeError);
    public JsonLineEventSink(string directory, int capacity = 2048, long maximumFileBytes = 4 * 1024 * 1024, int retainedFiles = 8)
    {
        if (capacity <= 0 || maximumFileBytes <= 0 || retainedFiles <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _directory = directory; _maximumBytes = maximumFileBytes; _retainedFiles = retainedFiles;
        _queue = Channel.CreateBounded<PendingEvent>(new BoundedChannelOptions(capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        _writer = Task.Run(ConsumeAsync);
    }
    public void Write(DiagnosticEvent entry)
    {
        if (!_queue.Writer.TryWrite(new(entry, null))) Interlocked.Increment(ref _dropped);
    }
    public void WriteDeferred(Func<DiagnosticEvent> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (!_queue.Writer.TryWrite(new(null, create))) Interlocked.Increment(ref _dropped);
    }
    private async Task ConsumeAsync()
    {
        StreamWriter? output = null;
        long bytes = 0; var sequence = 0;
        try
        {
            Directory.CreateDirectory(_directory);
            long eventSequence = 0;
            await foreach (var pending in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                string line;
                try { line = JsonSerializer.Serialize((pending.Value ?? pending.Create!()) with { Sequence = ++eventSequence }); }
                catch (Exception ex) { Volatile.Write(ref _writeError, "Event serialization failed: " + ex.Message); Interlocked.Increment(ref _dropped); continue; }
                var length = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                if (length > _maximumBytes) { Interlocked.Increment(ref _dropped); continue; }
                if (output is null || bytes + length > _maximumBytes)
                {
                    if (output is not null) await output.DisposeAsync().ConfigureAwait(false);
                    output = new StreamWriter(new FileStream(Path.Combine(_directory, $"smart-{_runId}-{sequence++:D4}.jsonl"),
                        FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, FileOptions.Asynchronous), new UTF8Encoding(false));
                    bytes = 0;
                    foreach (var old in Directory.EnumerateFiles(_directory, "smart-*.jsonl")
                                 .OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(x => x, StringComparer.Ordinal).Skip(_retainedFiles))
                    {
                        // Other live writers may hold a file. Retry retention on the next rotation.
                        try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                await output.WriteLineAsync(line).ConfigureAwait(false);
                bytes += length;
                if (!_queue.Reader.TryPeek(out _)) await output.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _writeError, ex.Message);
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out _)) Interlocked.Increment(ref _dropped);
        }
        finally { if (output is not null) await output.DisposeAsync().ConfigureAwait(false); }
    }
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }
}
