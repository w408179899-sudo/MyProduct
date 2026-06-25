using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tool
{
    public sealed class KmBoxOptions
    {
        public string PortName { get; set; } = "COM4";
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

    public enum KmMouseButton
    {
        Left,
        Right,
        Middle,
        Side1,
        Side2
    }

    public sealed class KmBoxClient : IDisposable
    {
        private const double HumanizedWaveAmplitude = 5.0;
        private const double HumanizedSpeedScale = 2.6;
        private const int HumanizedMaxDurationMs = 480;
        private readonly object _syncRoot = new object();
        private readonly SemaphoreSlim _ioSemaphore = new SemaphoreSlim(1, 1);
        private readonly KmBoxOptions _options;
        private readonly Random _random = new Random();
        private SerialPort _serialPort;
        private bool _disposed;

        public KmBoxClient(KmBoxOptions options = null)
        {
            _options = CopyAndValidateOptions(options ?? new KmBoxOptions());
        }

        public bool IsOpen
        {
            get
            {
                lock (_syncRoot)
                {
                    return _serialPort != null && _serialPort.IsOpen;
                }
            }
        }

        public void Open()
        {
            ThrowIfDisposed();
            _ioSemaphore.Wait();
            try
            {
                ThrowIfDisposed();

                lock (_syncRoot)
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        return;
                    }

                    ClosePortLocked();

                    var port = new SerialPort(_options.PortName, _options.BaudRate, _options.Parity, _options.DataBits, _options.StopBits)
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
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public Task OpenAsync(CancellationToken ct = default(CancellationToken))
        {
            ct.ThrowIfCancellationRequested();
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Open();
            }, ct);
        }

        public void Close()
        {
            ThrowIfDisposed();
            _ioSemaphore.Wait();
            try
            {
                lock (_syncRoot)
                {
                    ClosePortLocked();
                }
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public void SendRaw(string pythonCommand)
        {
            SendRawAsync(pythonCommand, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task SendRawAsync(string pythonCommand, CancellationToken ct = default(CancellationToken))
        {
            ThrowIfDisposed();
            if (pythonCommand == null)
            {
                throw new ArgumentNullException(nameof(pythonCommand));
            }

            await _ioSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var port = GetOpenPortOrThrow();
                await SendCommandOnPortAsync(port, pythonCommand, ct).ConfigureAwait(false);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public bool TrySendRaw(string pythonCommand, out Exception error)
        {
            try
            {
                SendRaw(pythonCommand);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void Move(int dx, int dy)
        {
            MoveAsync(dx, dy, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MoveAsync(int dx, int dy, CancellationToken ct = default(CancellationToken))
        {
            ValidateRange(dx, -32768, 32767, nameof(dx));
            ValidateRange(dy, -32768, 32767, nameof(dy));
            return SendRawAsync($"km.move({dx},{dy})", ct);
        }

        public void MoveRelative(int deltaX, int deltaY)
        {
            MoveRelativeAsync(deltaX, deltaY, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MoveRelativeAsync(int deltaX, int deltaY, CancellationToken ct = default(CancellationToken))
        {
            return MoveAsync(deltaX, deltaY, ct);
        }

        public bool TryMove(int dx, int dy, out Exception error)
        {
            try
            {
                Move(dx, dy);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public bool TryMoveRelative(int deltaX, int deltaY, out Exception error)
        {
            try
            {
                MoveRelative(deltaX, deltaY);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void MoveRelativeHumanLike(int targetDx, int targetDy)
        {
            MoveRelativeHumanLikeAsync(targetDx, targetDy, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task MoveRelativeHumanLikeAsync(int targetDx, int targetDy, CancellationToken ct = default(CancellationToken))
        {
            if (targetDx == 0 && targetDy == 0)
            {
                return;
            }

            ValidateRange(targetDx, -32768, 32767, nameof(targetDx));
            ValidateRange(targetDy, -32768, 32767, nameof(targetDy));

            int steps = ComputeHumanizedStepCount(targetDx, targetDy);
            int waveCycles = NextRandomInt(2, 4);
            var stopwatch = Stopwatch.StartNew();

            double c1x = targetDx * NextRandomDouble(0.18, 0.35);
            double c2x = targetDx * NextRandomDouble(0.65, 0.88);

            int movedX = 0;
            int movedY = 0;

            for (int i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                double t = i / (double)steps;

                double x = CubicBezier(0.0, c1x, c2x, targetDx, t);
                double yBase = targetDy * t;
                double yWave = HumanizedWaveAmplitude * Math.Sin(2.0 * Math.PI * waveCycles * t);
                double y = yBase + yWave;

                int nextX = (int)Math.Round(x, MidpointRounding.AwayFromZero);
                int nextY = (int)Math.Round(y, MidpointRounding.AwayFromZero);

                int stepX = nextX - movedX;
                int stepY = nextY - movedY;
                if (stepX != 0 || stepY != 0)
                {
                    await MoveRelativeAsync(stepX, stepY, ct).ConfigureAwait(false);
                    movedX += stepX;
                    movedY += stepY;
                }

                if (i < steps)
                {
                    int delayMs = ComputeHumanizedDelayMs(t);
                    delayMs = BoundHumanizedDelay(stopwatch.ElapsedMilliseconds, steps - i, delayMs);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }
                }
            }

            int remainX = targetDx - movedX;
            int remainY = targetDy - movedY;
            if (remainX != 0 || remainY != 0)
            {
                await MoveRelativeAsync(remainX, remainY, ct).ConfigureAwait(false);
            }
        }

        public bool TryMoveRelativeHumanLike(int targetDx, int targetDy, out Exception error)
        {
            try
            {
                MoveRelativeHumanLike(targetDx, targetDy);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void MoveAuto(int x, int y, int ms)
        {
            MoveAutoAsync(x, y, ms, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MoveAutoAsync(int x, int y, int ms, CancellationToken ct = default(CancellationToken))
        {
            ValidateRange(x, -32768, 32767, nameof(x));
            ValidateRange(y, -32768, 32767, nameof(y));
            ValidateRange(ms, 0, 65535, nameof(ms));
            return SendRawAsync($"km.move_auto({x},{y},{ms})", ct);
        }

        public bool TryMoveAuto(int x, int y, int ms, out Exception error)
        {
            try
            {
                MoveAuto(x, y, ms);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void Scroll(int delta)
        {
            ScrollAsync(delta, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task ScrollAsync(int delta, CancellationToken ct = default(CancellationToken))
        {
            ValidateRange(delta, -127, 127, nameof(delta));
            return SendRawAsync($"km.wheel({delta})", ct);
        }

        public void MouseWheel(int delta)
        {
            Scroll(delta);
        }

        public Task MouseWheelAsync(int delta, CancellationToken ct = default(CancellationToken))
        {
            return ScrollAsync(delta, ct);
        }

        public void WheelUp(int amount = 1)
        {
            ValidateRange(amount, 1, 127, nameof(amount));
            Scroll(amount);
        }

        public Task WheelUpAsync(int amount = 1, CancellationToken ct = default(CancellationToken))
        {
            ValidateRange(amount, 1, 127, nameof(amount));
            return ScrollAsync(amount, ct);
        }

        public void WheelDown(int amount = 1)
        {
            ValidateRange(amount, 1, 127, nameof(amount));
            Scroll(-amount);
        }

        public Task WheelDownAsync(int amount = 1, CancellationToken ct = default(CancellationToken))
        {
            ValidateRange(amount, 1, 127, nameof(amount));
            return ScrollAsync(-amount, ct);
        }

        public bool TryScroll(int delta, out Exception error)
        {
            try
            {
                Scroll(delta);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public bool TryMouseWheel(int delta, out Exception error)
        {
            return TryScroll(delta, out error);
        }

        public bool TryWheelUp(int amount, out Exception error)
        {
            try
            {
                WheelUp(amount);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public bool TryWheelDown(int amount, out Exception error)
        {
            try
            {
                WheelDown(amount);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void MouseDown(KmMouseButton button)
        {
            MouseDownAsync(button, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MouseDownAsync(KmMouseButton button, CancellationToken ct = default(CancellationToken))
        {
            string name = GetMouseFunctionName(button);
            return SendRawAsync($"km.{name}(1)", ct);
        }

        public void MouseUp(KmMouseButton button)
        {
            MouseUpAsync(button, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MouseUpAsync(KmMouseButton button, CancellationToken ct = default(CancellationToken))
        {
            string name = GetMouseFunctionName(button);
            return SendRawAsync($"km.{name}(0)", ct);
        }

        public void MouseClick(KmMouseButton button, int holdMs = 20)
        {
            MouseClickAsync(button, holdMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task MouseClickAsync(KmMouseButton button, int holdMs = 20, CancellationToken ct = default(CancellationToken))
        {
            if (holdMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(holdMs), holdMs, "holdMs must be greater than or equal to 0.");
            }

            string name = GetMouseFunctionName(button);
            await _ioSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var port = GetOpenPortOrThrow();
                await SendCommandOnPortAsync(port, $"km.{name}(1)", ct).ConfigureAwait(false);
                if (holdMs > 0)
                {
                    await Task.Delay(holdMs, ct).ConfigureAwait(false);
                }

                await SendCommandOnPortAsync(port, $"km.{name}(0)", ct).ConfigureAwait(false);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public bool TryMouseClick(KmMouseButton button, int holdMs, out Exception error)
        {
            try
            {
                MouseClick(button, holdMs);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void KeyDown(int vk)
        {
            KeyDownAsync(vk, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyDownAsync(int vk, CancellationToken ct = default(CancellationToken))
        {
            ValidateVk(vk, nameof(vk));
            return SendRawAsync($"km.down({vk})", ct);
        }

        public void KeyDown(Keys key)
        {
            KeyDownAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyDownAsync(Keys key, CancellationToken ct = default(CancellationToken))
        {
            return KeyDownAsync((int)key, ct);
        }

        public void KeyUp(int vk)
        {
            KeyUpAsync(vk, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyUpAsync(int vk, CancellationToken ct = default(CancellationToken))
        {
            ValidateVk(vk, nameof(vk));
            return SendRawAsync($"km.up({vk})", ct);
        }

        public void KeyUp(Keys key)
        {
            KeyUpAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyUpAsync(Keys key, CancellationToken ct = default(CancellationToken))
        {
            return KeyUpAsync((int)key, ct);
        }

        public void KeyPress(int vk)
        {
            KeyPressAsync(vk, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyPressAsync(int vk, CancellationToken ct = default(CancellationToken))
        {
            ValidateVk(vk, nameof(vk));
            return SendRawAsync($"km.press({vk})", ct);
        }

        public void KeyPress(Keys key)
        {
            KeyPressAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task KeyPressAsync(Keys key, CancellationToken ct = default(CancellationToken))
        {
            return KeyPressAsync((int)key, ct);
        }

        public bool TryKeyPress(int vk, out Exception error)
        {
            try
            {
                KeyPress(vk);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public bool TryKeyPress(Keys key, out Exception error)
        {
            return TryKeyPress((int)key, out error);
        }

        public void Hotkey(params Keys[] keys)
        {
            HotkeyAsync(keys, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task HotkeyAsync(params Keys[] keys)
        {
            return HotkeyAsync(keys, CancellationToken.None);
        }

        public async Task HotkeyAsync(Keys[] keys, CancellationToken ct)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (keys.Length == 0)
            {
                throw new ArgumentException("At least one key is required.", nameof(keys));
            }

            foreach (var key in keys)
            {
                ValidateVk((int)key, nameof(keys));
            }

            await _ioSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var port = GetOpenPortOrThrow();
                for (int i = 0; i < keys.Length; i++)
                {
                    await SendCommandOnPortAsync(port, $"km.down({(int)keys[i]})", ct).ConfigureAwait(false);
                }

                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    await SendCommandOnPortAsync(port, $"km.up({(int)keys[i]})", ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public bool TryHotkey(Keys[] keys, out Exception error)
        {
            try
            {
                Hotkey(keys);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void TypeText(string text)
        {
            TypeTextAsync(text, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task TypeTextAsync(string text, CancellationToken ct = default(CancellationToken))
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return SendRawAsync($"km.say_string(\"{EscapePythonString(text)}\")", ct);
        }

        public bool TryTypeText(string text, out Exception error)
        {
            try
            {
                TypeText(text);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public void Dispose()
        {
            bool disposeSemaphore = false;
            _ioSemaphore.Wait();
            try
            {
                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    ClosePortLocked();
                    _disposed = true;
                    disposeSemaphore = true;
                }
            }
            finally
            {
                _ioSemaphore.Release();
                if (disposeSemaphore)
                {
                    _ioSemaphore.Dispose();
                }
            }
        }

        private static KmBoxOptions CopyAndValidateOptions(KmBoxOptions source)
        {
            var options = new KmBoxOptions
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
                Encoding = source.Encoding
            };

            if (string.IsNullOrWhiteSpace(options.PortName))
            {
                throw new ArgumentException("PortName cannot be empty.", nameof(source));
            }

            if (options.BaudRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "BaudRate must be greater than 0.");
            }

            if (options.DataBits < 5 || options.DataBits > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "DataBits must be between 5 and 8.");
            }

            if (options.WriteTimeoutMs < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "WriteTimeoutMs must be >= -1.");
            }

            if (options.ReadTimeoutMs < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "ReadTimeoutMs must be >= -1.");
            }

            if (options.InterCommandDelayMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "InterCommandDelayMs must be >= 0.");
            }

            if (options.OpenReadyDelayMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(source), "OpenReadyDelayMs must be >= 0.");
            }

            if (options.Encoding == null)
            {
                throw new ArgumentNullException(nameof(source), "Encoding cannot be null.");
            }

            return options;
        }

        private SerialPort GetOpenPortOrThrow()
        {
            lock (_syncRoot)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    throw new InvalidOperationException("KmBox serial port is not open. Call Open() before sending commands.");
                }

                return _serialPort;
            }
        }

        private void SendCommandOnPort(SerialPort port, string pythonCommand)
        {
            string command = NormalizeCommand(pythonCommand);
            byte[] bytes = _options.Encoding.GetBytes(command);
            port.Write(bytes, 0, bytes.Length);

            if (_options.InterCommandDelayMs > 0)
            {
                Thread.Sleep(_options.InterCommandDelayMs);
            }
        }

        private Task SendCommandOnPortAsync(SerialPort port, string pythonCommand, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SendCommandOnPort(port, pythonCommand);
            return Task.CompletedTask;
        }

        private void ClosePortLocked()
        {
            if (_serialPort == null)
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(KmBoxClient));
            }
        }

        private static string NormalizeCommand(string pythonCommand)
        {
            if (pythonCommand == null)
            {
                throw new ArgumentNullException(nameof(pythonCommand));
            }

            string normalized = pythonCommand.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Command cannot be empty.", nameof(pythonCommand));
            }

            return normalized + "\r\n";
        }

        private static string GetMouseFunctionName(KmMouseButton button)
        {
            switch (button)
            {
                case KmMouseButton.Left:
                    return "left";
                case KmMouseButton.Right:
                    return "right";
                case KmMouseButton.Middle:
                    return "middle";
                case KmMouseButton.Side1:
                    return "side1";
                case KmMouseButton.Side2:
                    return "side2";
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button.");
            }
        }

        private static void ValidateVk(int vk, string argumentName)
        {
            ValidateRange(vk, 0, 255, argumentName);
        }

        private static void ValidateRange(int value, int min, int max, string argumentName)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(argumentName, value, $"Value must be between {min} and {max}.");
            }
        }

        private static string EscapePythonString(string value)
        {
            var sb = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            sb.Append("\\x");
                            sb.Append(((int)c).ToString("X2"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        private int ComputeHumanizedDelayMs(double t)
        {
            // Slower at start/end and faster in the middle to mimic hand motion acceleration.
            double speedFactor = 1.0 - Math.Abs(2.0 * t - 1.0);
            int baseDelay = (int)Math.Round(((1.0 - speedFactor) * 2.0) / HumanizedSpeedScale);
            return baseDelay + NextRandomInt(0, 2);
        }

        private static int ComputeHumanizedStepCount(int dx, int dy)
        {
            double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
            int steps = (int)Math.Round(distance * (0.10 / HumanizedSpeedScale));
            if (steps < 5)
            {
                return 5;
            }

            if (steps > 26)
            {
                return 26;
            }

            return steps;
        }

        private static int BoundHumanizedDelay(long elapsedMs, int remainingSegments, int desiredDelayMs)
        {
            if (desiredDelayMs <= 0 || remainingSegments <= 0)
            {
                return 0;
            }

            int remainingBudget = HumanizedMaxDurationMs - (int)elapsedMs;
            if (remainingBudget <= 0)
            {
                return 0;
            }

            // Reserve at least ~1ms budget for each remaining serial write.
            int maxDelayPerSegment = (remainingBudget / remainingSegments) - 1;
            if (maxDelayPerSegment <= 0)
            {
                return 0;
            }

            return desiredDelayMs > maxDelayPerSegment ? maxDelayPerSegment : desiredDelayMs;
        }

        private int NextRandomInt(int minValue, int maxExclusive)
        {
            lock (_random)
            {
                return _random.Next(minValue, maxExclusive);
            }
        }

        private double NextRandomDouble(double minValue, double maxValue)
        {
            lock (_random)
            {
                return minValue + (_random.NextDouble() * (maxValue - minValue));
            }
        }

        private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
        {
            double u = 1.0 - t;
            return (u * u * u * p0) +
                   (3.0 * u * u * t * p1) +
                   (3.0 * u * t * t * p2) +
                   (t * t * t * p3);
        }
    }
}
