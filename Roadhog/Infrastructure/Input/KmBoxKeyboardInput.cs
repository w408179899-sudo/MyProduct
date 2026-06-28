using System.IO.Ports;
using System.Text;
using Roadhog.Core.Common;
using Roadhog.Core.Input;

namespace Roadhog.Infrastructure.Input;

public sealed class KmBoxKeyboardInputOptions
{
    public string PortName { get; set; } = Environment.GetEnvironmentVariable("KMBOX_PORT") ?? "COM11";

    public int BaudRate { get; set; } = 115200;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public int WriteTimeoutMs { get; set; } = 300;

    public int ReadTimeoutMs { get; set; } = 100;

    public bool AutoImportKmOnOpen { get; set; } = true;

    public int OpenReadyDelayMs { get; set; } = 1000;

    public int InterCommandDelayMs { get; set; } = 1;

    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
}

public sealed class KmBoxKeyboardInput : IKeyboardInput, IDisposable
{
    private static readonly IReadOnlyDictionary<string, int> SkillKeyHidCodes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = 0x06,
            ["W"] = 0x1A,
            ["X"] = 0x1B,
            ["D1"] = 0x1E,
            ["D2"] = 0x1F,
            ["D3"] = 0x20,
            ["D4"] = 0x21,
            ["D5"] = 0x22,
            ["D6"] = 0x23,
            ["D7"] = 0x24,
            ["D8"] = 0x25,
            ["D9"] = 0x26,
            ["D0"] = 0x27,
            ["OemMinus"] = 0x2D,
            ["OemPlus"] = 0x2E,
            ["OemComma"] = 0x36,
            ["Tab"] = 0x2B,
            ["NumPad1"] = 0x59,
            ["NumPad2"] = 0x5A,
            ["NumPad3"] = 0x5B,
            ["NumPad4"] = 0x5C,
            ["NumPad5"] = 0x5D,
            ["NumPad6"] = 0x5E,
            ["NumPad7"] = 0x5F,
            ["NumPad8"] = 0x60,
            ["NumPad9"] = 0x61,
            ["NumPad0"] = 0x62
        };

    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _ioSemaphore = new(1, 1);
    private readonly KmBoxKeyboardInputOptions _options;
    private SerialPort? _serialPort;
    private bool _disposed;

    public KmBoxKeyboardInput(KmBoxKeyboardInputOptions options)
    {
        _options = CopyAndValidateOptions(options);
    }

    public async Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveSkillKey(key, out var hidCode))
        {
            return OperationResult.Fail(
                "Unsupported KMbox skill key: " + key +
                ". Allowed keys: " + string.Join(", ", SkillKeyHidCodes.Keys));
        }

        var keyDownSent = false;
        await _ioSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var port = GetOrOpenPort();
            SendCommandOnPort(port, "km.down(" + hidCode + ")");
            keyDownSent = true;

            if (holdDuration > TimeSpan.Zero)
            {
                await Task.Delay(holdDuration, cancellationToken).ConfigureAwait(false);
            }

            SendCommandOnPort(port, "km.up(" + hidCode + ")");
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (keyDownSent)
            {
                TrySendKeyUp(hidCode);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (keyDownSent)
            {
                TrySendKeyUp(hidCode);
            }

            ClosePort();
            return OperationResult.Fail(
                "KMbox key press failed. port=" + _options.PortName +
                " availablePorts=" + GetAvailablePortNamesText() +
                " key=" + key +
                " hid=0x" + hidCode.ToString("X2") +
                " error=" + ex.Message);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public Task<OperationResult> KeyDownAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return SendResolvedKeyCommandAsync(key, "down", cancellationToken);
    }

    public Task<OperationResult> KeyUpAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return SendResolvedKeyCommandAsync(key, "up", cancellationToken);
    }

    public Task<OperationResult> MouseDownAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        return SendMouseButtonCommandAsync(button, true, cancellationToken);
    }

    public Task<OperationResult> MouseUpAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        return SendMouseButtonCommandAsync(button, false, cancellationToken);
    }

    public async Task<OperationResult> MoveMouseRelativeAsync(
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default)
    {
        if (deltaX < -32768 || deltaX > 32767 || deltaY < -32768 || deltaY > 32767)
        {
            return OperationResult.Fail("KMbox mouse delta is out of range.");
        }

        return await SendRawCommandAsync(
            "km.move(" + deltaX + "," + deltaY + ")",
            "KMbox mouse move failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClosePort();
        _ioSemaphore.Dispose();
    }

    private SerialPort GetOrOpenPort()
    {
        lock (_syncRoot)
        {
            if (_serialPort is { IsOpen: true })
            {
                return _serialPort;
            }

            ClosePortLocked();

            var port = new SerialPort(
                _options.PortName,
                _options.BaudRate,
                _options.Parity,
                _options.DataBits,
                _options.StopBits)
            {
                ReadTimeout = _options.ReadTimeoutMs,
                WriteTimeout = _options.WriteTimeoutMs,
                Encoding = _options.Encoding
            };

            try
            {
                port.Open();
                _serialPort = port;

                if (_options.OpenReadyDelayMs > 0)
                {
                    Thread.Sleep(_options.OpenReadyDelayMs);
                }

                if (_options.AutoImportKmOnOpen)
                {
                    SendCommandOnPort(port, "import km");
                }

                return port;
            }
            catch
            {
                try
                {
                    port.Dispose();
                }
                catch
                {
                }

                _serialPort = null;
                throw;
            }
        }
    }

    private void TrySendKeyUp(int hidCode)
    {
        try
        {
            SerialPort? port;
            lock (_syncRoot)
            {
                port = _serialPort is { IsOpen: true } ? _serialPort : null;
            }

            if (port is not null)
            {
                SendCommandOnPort(port, "km.up(" + hidCode + ")");
            }
        }
        catch
        {
        }
    }

    private async Task<OperationResult> SendResolvedKeyCommandAsync(
        string key,
        string command,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSkillKey(key, out var hidCode))
        {
            return OperationResult.Fail(
                "Unsupported KMbox key: " + key +
                ". Allowed keys: " + string.Join(", ", SkillKeyHidCodes.Keys));
        }

        return await SendRawCommandAsync(
            "km." + command + "(" + hidCode + ")",
            "KMbox key command failed.",
            cancellationToken).ConfigureAwait(false);
    }

    private Task<OperationResult> SendMouseButtonCommandAsync(
        RoadhogMouseButton button,
        bool down,
        CancellationToken cancellationToken)
    {
        var name = button switch
        {
            RoadhogMouseButton.Left => "left",
            RoadhogMouseButton.Right => "right",
            RoadhogMouseButton.Middle => "middle",
            RoadhogMouseButton.Side1 => "side1",
            RoadhogMouseButton.Side2 => "side2",
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button.")
        };

        return SendRawCommandAsync(
            "km." + name + "(" + (down ? "1" : "0") + ")",
            "KMbox mouse button command failed.",
            cancellationToken);
    }

    private async Task<OperationResult> SendRawCommandAsync(
        string pythonCommand,
        string errorPrefix,
        CancellationToken cancellationToken)
    {
        await _ioSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var port = GetOrOpenPort();
            SendCommandOnPort(port, pythonCommand);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClosePort();
            return OperationResult.Fail(
                errorPrefix +
                " port=" + _options.PortName +
                " availablePorts=" + GetAvailablePortNamesText() +
                " command=\"" + pythonCommand + "\"" +
                " error=" + ex.Message);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    private void ClosePort()
    {
        lock (_syncRoot)
        {
            ClosePortLocked();
        }
    }

    private void ClosePortLocked()
    {
        if (_serialPort is null)
        {
            return;
        }

        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        finally
        {
            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    private void SendCommandOnPort(SerialPort port, string pythonCommand)
    {
        var command = NormalizeCommand(pythonCommand);
        var bytes = _options.Encoding.GetBytes(command);
        port.Write(bytes, 0, bytes.Length);

        if (_options.InterCommandDelayMs > 0)
        {
            Thread.Sleep(_options.InterCommandDelayMs);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KmBoxKeyboardInput));
        }
    }

    private static bool TryResolveSkillKey(string key, out int hidCode)
    {
        hidCode = 0;
        return !string.IsNullOrWhiteSpace(key) &&
               SkillKeyHidCodes.TryGetValue(key.Trim(), out hidCode);
    }

    private static KmBoxKeyboardInputOptions CopyAndValidateOptions(KmBoxKeyboardInputOptions source)
    {
        var options = new KmBoxKeyboardInputOptions
        {
            PortName = source.PortName,
            BaudRate = source.BaudRate,
            DataBits = source.DataBits,
            Parity = source.Parity,
            StopBits = source.StopBits,
            WriteTimeoutMs = source.WriteTimeoutMs,
            ReadTimeoutMs = source.ReadTimeoutMs,
            AutoImportKmOnOpen = source.AutoImportKmOnOpen,
            OpenReadyDelayMs = source.OpenReadyDelayMs,
            InterCommandDelayMs = source.InterCommandDelayMs,
            Encoding = source.Encoding ?? new UTF8Encoding(false)
        };

        if (string.IsNullOrWhiteSpace(options.PortName))
        {
            throw new ArgumentException("KMbox port name cannot be empty.", nameof(source));
        }

        if (options.BaudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "KMbox baud rate must be greater than 0.");
        }

        if (options.DataBits < 5 || options.DataBits > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "KMbox data bits must be between 5 and 8.");
        }

        if (options.WriteTimeoutMs < -1 || options.ReadTimeoutMs < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "KMbox timeouts must be >= -1.");
        }

        if (options.OpenReadyDelayMs < 0 || options.InterCommandDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "KMbox delays must be >= 0.");
        }

        return options;
    }

    private static string ResolveDefaultPortName()
    {
        var envPort = Environment.GetEnvironmentVariable("KMBOX_PORT");
        if (!string.IsNullOrWhiteSpace(envPort))
        {
            return envPort.Trim();
        }

        var ports = GetAvailablePortNames();
        foreach (var preferred in new[] { "COM11", "COM8", "COM4" })
        {
            if (ports.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }

        return ports.Length > 0 ? ports[0] : "COM11";
    }

    private static string[] GetAvailablePortNames()
    {
        try
        {
            return SerialPort.GetPortNames()
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Select(port => port.Trim())
                .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GetAvailablePortNamesText()
    {
        var ports = GetAvailablePortNames();
        return ports.Length == 0 ? "(none)" : string.Join(",", ports);
    }

    private static string NormalizeCommand(string pythonCommand)
    {
        if (pythonCommand is null)
        {
            throw new ArgumentNullException(nameof(pythonCommand));
        }

        var normalized = pythonCommand.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("KMbox command cannot be empty.", nameof(pythonCommand));
        }

        return normalized + "\r\n";
    }
}
