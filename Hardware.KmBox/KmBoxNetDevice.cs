using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Hardware.KmBox;

public sealed class KmBoxNetDevice : IKmBoxDevice, IDisposable
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly KmBoxOptions _options;
    private readonly IPAddress _ipAddress;
    private readonly uint _mac;
    private readonly Random _random = new();
    private readonly HashSet<byte> _pressedKeyboardButtons = new();
    private UdpClient? _udpClient;
    private uint _index;
    private int _pressedMouseButtons;
    private byte _pressedKeyboardModifiers;
    private bool _connected;
    private bool _disposed;

    public KmBoxNetDevice(KmBoxOptions options)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _ipAddress = IPAddress.Parse(_options.IpAddress);
        _mac = KmBoxProtocol.MacToUInt32(_options.Mac);
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            CloseUdpClient();

            _udpClient = new UdpClient();
            _udpClient.Client.SendTimeout = _options.SendTimeoutMs;
            _udpClient.Client.ReceiveTimeout = _options.ReceiveTimeoutMs;
            _udpClient.Connect(_ipAddress, _options.Port);

            var request = NextHeader(KmBoxCommand.Connect);
            var response = await SendCommandUnlockedAsync(request, Array.Empty<byte>(), ct).ConfigureAwait(false);
            _connected = KmBoxProtocol.IsMatchingResponse(request, response);
            if (!_connected)
            {
                CloseUdpClient();
            }

            return _connected;
        }
        catch (OperationCanceledException)
        {
            CloseUdpClient();
            throw;
        }
        catch (TimeoutException)
        {
            CloseUdpClient();
            return false;
        }
        catch (SocketException)
        {
            CloseUdpClient();
            return false;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await ReleaseAllAsync().ConfigureAwait(false);
        }
        finally
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                CloseUdpClient();
            }
            finally
            {
                _ioLock.Release();
            }
        }
    }

    public Task MoveMouseAsync(int x, int y, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                ValidateMouseDelta(x, nameof(x));
                ValidateMouseDelta(y, nameof(y));

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    await SendMouseActionUnlockedAsync(
                        KmBoxCommand.MouseMove,
                        _pressedMouseButtons,
                        x,
                        y,
                        0,
                        0,
                        token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox mouse move failed.",
            releaseAllOnFailure: false,
            ct);
    }

    public Task MoveMouseSmoothAsync(int x, int y, int durationMs, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                ValidateMouseDelta(x, nameof(x));
                ValidateMouseDelta(y, nameof(y));
                if (durationMs < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(durationMs), "durationMs must be greater than or equal to 0.");
                }

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    await SendMouseActionUnlockedAsync(
                        KmBoxCommand.MouseAutoMove,
                        _pressedMouseButtons,
                        x,
                        y,
                        0,
                        (uint)durationMs,
                        token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox smooth mouse move failed.",
            releaseAllOnFailure: false,
            ct);
    }

    public Task MouseDownAsync(MouseButton button, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                var flag = ToMouseFlag(button);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    _pressedMouseButtons |= flag;
                    await SendMouseStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox mouse down failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public Task MouseUpAsync(MouseButton button, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                var flag = ToMouseFlag(button);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    _pressedMouseButtons &= ~flag;
                    await SendMouseStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox mouse up failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public Task ClickAsync(MouseButton button, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                var flag = ToMouseFlag(button);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    _pressedMouseButtons |= flag;
                    await SendMouseStateUnlockedAsync(token).ConfigureAwait(false);

                    if (_options.DefaultClickHoldMs > 0)
                    {
                        await Task.Delay(_options.DefaultClickHoldMs, token).ConfigureAwait(false);
                    }

                    _pressedMouseButtons &= ~flag;
                    await SendMouseStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox mouse click failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public Task WheelAsync(int delta, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    await SendMouseActionUnlockedAsync(
                        KmBoxCommand.MouseWheel,
                        _pressedMouseButtons,
                        0,
                        0,
                        delta,
                        0,
                        token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox mouse wheel failed.",
            releaseAllOnFailure: false,
            ct);
    }

    public Task KeyDownAsync(Keys key, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                var stroke = HidKeyboardMap.FromKeys(key);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    ApplyKeyDown(stroke);
                    await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox key down failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public Task KeyUpAsync(Keys key, CancellationToken ct = default)
    {
        return ExecuteAsync(
            async token =>
            {
                var stroke = HidKeyboardMap.FromKeys(key);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    ApplyKeyUp(stroke);
                    await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox key up failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public Task KeyDownAsync(int keyCode, CancellationToken ct = default)
    {
        return ExecuteKeyCodeAsync(keyCode, keyDown: true, "KMBox key code down failed.", ct);
    }

    public Task KeyUpAsync(int keyCode, CancellationToken ct = default)
    {
        return ExecuteKeyCodeAsync(keyCode, keyDown: false, "KMBox key code up failed.", ct);
    }

    public async Task PressKeyAsync(Keys key, int holdMs = 30, CancellationToken ct = default)
    {
        if (holdMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMs), "holdMs must be greater than or equal to 0.");
        }

        await KeyDownAsync(key, ct).ConfigureAwait(false);
        try
        {
            if (holdMs > 0)
            {
                await Task.Delay(holdMs, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await KeyUpAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task PressKeyAsync(int keyCode, int holdMs = 30, CancellationToken ct = default)
    {
        if (holdMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMs), "holdMs must be greater than or equal to 0.");
        }

        await KeyDownAsync(keyCode, ct).ConfigureAwait(false);
        try
        {
            if (holdMs > 0)
            {
                await Task.Delay(holdMs, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await KeyUpAsync(keyCode, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task HotkeyAsync(IReadOnlyList<Keys> keys, int holdMs = 30, CancellationToken ct = default)
    {
        if (keys is null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one key is required.", nameof(keys));
        }

        if (holdMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMs), "holdMs must be greater than or equal to 0.");
        }

        for (var i = 0; i < keys.Count; i++)
        {
            await KeyDownAsync(keys[i], ct).ConfigureAwait(false);
        }

        try
        {
            if (holdMs > 0)
            {
                await Task.Delay(holdMs, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            for (var i = keys.Count - 1; i >= 0; i--)
            {
                await KeyUpAsync(keys[i], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task HotkeyAsync(IReadOnlyList<int> keyCodes, int holdMs = 30, CancellationToken ct = default)
    {
        if (keyCodes is null)
        {
            throw new ArgumentNullException(nameof(keyCodes));
        }

        if (keyCodes.Count == 0)
        {
            throw new ArgumentException("At least one key code is required.", nameof(keyCodes));
        }

        if (holdMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMs), "holdMs must be greater than or equal to 0.");
        }

        for (var i = 0; i < keyCodes.Count; i++)
        {
            await KeyDownAsync(keyCodes[i], ct).ConfigureAwait(false);
        }

        try
        {
            if (holdMs > 0)
            {
                await Task.Delay(holdMs, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            for (var i = keyCodes.Count - 1; i >= 0; i--)
            {
                await KeyUpAsync(keyCodes[i], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public Task TypeTextAsync(string text, CancellationToken ct = default)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return ExecuteAsync(
            async token =>
            {
                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    ClearKeyboardState();
                    await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);

                    foreach (var character in text)
                    {
                        var stroke = HidKeyboardMap.FromCharacter(character);
                        _pressedKeyboardModifiers = stroke.Modifiers;
                        _pressedKeyboardButtons.Clear();
                        if (stroke.Button != 0)
                        {
                            _pressedKeyboardButtons.Add(stroke.Button);
                        }

                        await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);

                        if (_options.TypeKeyDelayMs > 0)
                        {
                            await Task.Delay(_options.TypeKeyDelayMs, token).ConfigureAwait(false);
                        }

                        ClearKeyboardState();
                        await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            "KMBox type text failed.",
            releaseAllOnFailure: true,
            ct);
    }

    public async Task ReleaseAllAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_udpClient is null)
            {
                ClearInputState();
                return;
            }

            ClearInputState();
            await SendMouseStateUnlockedAsync(ct).ConfigureAwait(false);
            await SendKeyboardStateUnlockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            ReleaseAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }

        CloseUdpClient();
        _ioLock.Dispose();
        _disposed = true;
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        string errorMessage,
        bool releaseAllOnFailure,
        CancellationToken ct)
    {
        try
        {
            await action(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (releaseAllOnFailure)
            {
                await TryReleaseAllAfterFailureAsync().ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (releaseAllOnFailure)
            {
                await TryReleaseAllAfterFailureAsync().ConfigureAwait(false);
            }

            if (ex is KmBoxException)
            {
                throw;
            }

            throw new KmBoxException(errorMessage, ex);
        }
    }

    private Task ExecuteKeyCodeAsync(int keyCode, bool keyDown, string errorMessage, CancellationToken ct)
    {
        return ExecuteAsync(
            async token =>
            {
                var stroke = HidKeyboardMap.FromKeyCode(keyCode);

                await _ioLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    EnsureConnected();
                    if (keyDown)
                    {
                        ApplyKeyDown(stroke);
                    }
                    else
                    {
                        ApplyKeyUp(stroke);
                    }

                    await SendKeyboardStateUnlockedAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    _ioLock.Release();
                }
            },
            errorMessage,
            releaseAllOnFailure: true,
            ct);
    }

    private async Task TryReleaseAllAfterFailureAsync()
    {
        try
        {
            await ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task SendMouseStateUnlockedAsync(CancellationToken ct)
    {
        await SendMouseActionUnlockedAsync(
            KmBoxCommand.MouseButton,
            _pressedMouseButtons,
            0,
            0,
            0,
            0,
            ct).ConfigureAwait(false);
    }

    private async Task SendMouseActionUnlockedAsync(
        KmBoxCommand command,
        int buttons,
        int x,
        int y,
        int wheel,
        uint rand,
        CancellationToken ct)
    {
        var request = NextHeader(command);
        if (rand != 0)
        {
            request = request.WithRand(rand);
        }

        var payload = KmBoxProtocol.BuildMousePayload(buttons, x, y, wheel);
        var response = await SendCommandUnlockedAsync(request, payload, ct).ConfigureAwait(false);
        if (!KmBoxProtocol.IsMatchingResponse(request, response))
        {
            throw new KmBoxException("KMBox returned an unexpected mouse response.");
        }
    }

    private async Task SendKeyboardStateUnlockedAsync(CancellationToken ct)
    {
        var buttons = _pressedKeyboardButtons.Take(KmBoxProtocol.MaxKeyboardButtons).ToArray();
        if (_pressedKeyboardButtons.Count > KmBoxProtocol.MaxKeyboardButtons)
        {
            throw new InvalidOperationException("KMBox supports at most 10 simultaneous keyboard buttons.");
        }

        var request = NextHeader(KmBoxCommand.KeyboardAll);
        var payload = KmBoxProtocol.BuildKeyboardPayload(_pressedKeyboardModifiers, buttons);
        var response = await SendCommandUnlockedAsync(request, payload, ct).ConfigureAwait(false);
        if (!KmBoxProtocol.IsMatchingResponse(request, response))
        {
            throw new KmBoxException("KMBox returned an unexpected keyboard response.");
        }
    }

    private async Task<KmBoxResponseHeader> SendCommandUnlockedAsync(
        KmBoxRequestHeader request,
        byte[] payload,
        CancellationToken ct)
    {
        var client = _udpClient ?? throw new InvalidOperationException("KMBox is not connected.");
        var packet = KmBoxProtocol.BuildPacket(request, payload);

        await WithTimeoutAsync(
            client.SendAsync(packet, packet.Length),
            _options.SendTimeoutMs,
            "KMBox UDP send timed out.",
            ct).ConfigureAwait(false);

        var result = await WithTimeoutAsync(
            client.ReceiveAsync(),
            _options.ReceiveTimeoutMs,
            "KMBox UDP receive timed out.",
            ct).ConfigureAwait(false);

        return KmBoxProtocol.ParseResponseHeader(result.Buffer);
    }

    private static async Task<T> WithTimeoutAsync<T>(
        Task<T> task,
        int timeoutMs,
        string timeoutMessage,
        CancellationToken ct)
    {
        var delayTask = Task.Delay(timeoutMs, ct);
        var completed = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
        if (completed == delayTask)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(timeoutMessage);
        }

        return await task.ConfigureAwait(false);
    }

    private KmBoxRequestHeader NextHeader(KmBoxCommand command)
    {
        return new KmBoxRequestHeader(
            _mac,
            NextRandomUInt32(),
            _index++,
            command);
    }

    private uint NextRandomUInt32()
    {
        var bytes = new byte[4];
        lock (_random)
        {
            _random.NextBytes(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!_connected || _udpClient is null)
        {
            throw new InvalidOperationException("KMBox is not connected. Call ConnectAsync first.");
        }
    }

    private void CloseUdpClient()
    {
        _connected = false;
        try
        {
            _udpClient?.Close();
        }
        finally
        {
            _udpClient?.Dispose();
            _udpClient = null;
            ClearInputState();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KmBoxNetDevice));
        }
    }

    private void ApplyKeyDown(HidKeyStroke stroke)
    {
        _pressedKeyboardModifiers |= stroke.Modifiers;
        if (stroke.Button != 0)
        {
            _pressedKeyboardButtons.Add(stroke.Button);
        }
    }

    private void ApplyKeyUp(HidKeyStroke stroke)
    {
        _pressedKeyboardModifiers = (byte)(_pressedKeyboardModifiers & ~stroke.Modifiers);
        if (stroke.Button != 0)
        {
            _pressedKeyboardButtons.Remove(stroke.Button);
        }
    }

    private void ClearInputState()
    {
        _pressedMouseButtons = 0;
        ClearKeyboardState();
    }

    private void ClearKeyboardState()
    {
        _pressedKeyboardModifiers = 0;
        _pressedKeyboardButtons.Clear();
    }

    private static int ToMouseFlag(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 1 << 1,
            MouseButton.Middle => 1 << 2,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button.")
        };
    }

    private static void ValidateMouseDelta(int value, string argumentName)
    {
        if (value < short.MinValue || value > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(argumentName, value, "KMBox mouse movement must be between -32768 and 32767.");
        }
    }
}
