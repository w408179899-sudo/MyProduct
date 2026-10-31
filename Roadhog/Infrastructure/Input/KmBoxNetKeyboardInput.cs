using Hardware.KmBox;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using KmBoxMouseButton = Hardware.KmBox.MouseButton;

namespace Roadhog.Infrastructure.Input;

public sealed class KmBoxNetKeyboardInput : IKeyboardInput, IInputStateReset, IDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private readonly KmBoxNetKeyboardInputOptions _options;
    private readonly IKmBoxDevice _device;
    private bool _connected;
    private bool _disposed;

    public KmBoxNetKeyboardInput(KmBoxNetKeyboardInputOptions options)
        : this(options, new KmBoxNetDevice((options ?? throw new ArgumentNullException(nameof(options))).ToKmBoxOptions())) { }

    internal KmBoxNetKeyboardInput(KmBoxNetKeyboardInputOptions options, IKmBoxDevice device)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    internal Task<OperationResult> TypeLoginTextAsync(string text, CancellationToken token) =>
        ExecuteAsync(device => device.TypeTextAsync(text, token), "登录文本输入失败。", token);

    internal Task<OperationResult> LoginHotkeyAsync(Keys[] keys, CancellationToken token) =>
        ExecuteAsync(device => device.HotkeyAsync(keys, 80, token), "登录组合键输入失败。", token);

    public async Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default)
    {
        if (RoadhogInputKeyMap.TryResolveQuickbarChord(key, out var modifier, out var primary))
        {
            return await ExecuteAsync(async device =>
            {
                // Separate acknowledged reports are required by the game. Keep the entire chord atomic.
                try
                {
                    await device.KeyDownAsync(modifier, cancellationToken).ConfigureAwait(false);
                    await device.KeyDownAsync(primary, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    try { await device.KeyUpAsync(primary, CancellationToken.None).ConfigureAwait(false); }
                    finally { await device.KeyUpAsync(modifier, CancellationToken.None).ConfigureAwait(false); }
                }
            }, "KMBox Net quickbar chord failed.", cancellationToken).ConfigureAwait(false);
        }

        if (!RoadhogInputKeyMap.TryResolveHidCode(key, out var hidCode))
        {
            return UnsupportedKey(key, "KMBox Net skill key");
        }

        var holdMs = ToHoldMilliseconds(holdDuration);
        return await ExecuteAsync(
            device => device.PressKeyAsync(hidCode, holdMs, cancellationToken),
            "KMBox Net key press failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> KeyDownAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!RoadhogInputKeyMap.TryResolveHidCode(key, out var hidCode))
        {
            return UnsupportedKey(key, "KMBox Net key");
        }

        return await ExecuteAsync(
            device => device.KeyDownAsync(hidCode, cancellationToken),
            "KMBox Net key down failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> KeyUpAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!RoadhogInputKeyMap.TryResolveHidCode(key, out var hidCode))
        {
            return UnsupportedKey(key, "KMBox Net key");
        }

        return await ExecuteAsync(
            device => device.KeyUpAsync(hidCode, cancellationToken),
            "KMBox Net key up failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> MouseDownAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        if (!TryConvertMouseButton(button, out var kmButton, out var error))
        {
            return OperationResult.Fail(error);
        }

        return await ExecuteAsync(
            device => device.MouseDownAsync(kmButton, cancellationToken),
            "KMBox Net mouse down failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> MouseUpAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        if (!TryConvertMouseButton(button, out var kmButton, out var error))
        {
            return OperationResult.Fail(error);
        }

        return await ExecuteAsync(
            device => device.MouseUpAsync(kmButton, cancellationToken),
            "KMBox Net mouse up failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> MoveMouseRelativeAsync(
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default)
    {
        if (deltaX < short.MinValue || deltaX > short.MaxValue ||
            deltaY < short.MinValue || deltaY > short.MaxValue)
        {
            return OperationResult.Fail("KMBox Net mouse delta is out of range.");
        }

        return await ExecuteAsync(
            device => device.MoveMouseAsync(deltaX, deltaY, cancellationToken),
            "KMBox Net mouse move failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> ScrollMouseAsync(
        int wheelDelta,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            device => device.WheelAsync(wheelDelta, cancellationToken),
            "KMBox Net mouse wheel failed.",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult> ReleaseAllAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(device => device.ReleaseAllAsync(cancellationToken), "KMBox Net release all failed.", cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (_device as IDisposable)?.Dispose();
        _connectionLock.Dispose();
        _inputLock.Dispose();
    }

    /// <summary>Connects and completes the device handshake without sending keyboard or mouse input.</summary>
    public Task<OperationResult> ConnectAsync(CancellationToken cancellationToken = default) =>
        EnsureConnectedAsync(cancellationToken);

    private async Task<OperationResult> ExecuteAsync(
        Func<IKmBoxDevice, Task> action,
        string errorPrefix,
        CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            ThrowIfDisposed();
            await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            var connect = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (!connect.Success)
            {
                return connect;
            }

            await action(_device).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _connected = false;
            return OperationResult.Fail(
                errorPrefix +
                " endpoint=" + _options.EndpointText() +
                " error=" + ex.Message);
        }
        finally { if (entered) _inputLock.Release(); }
    }

    private async Task<OperationResult> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connected)
        {
            return OperationResult.Ok();
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_connected)
            {
                return OperationResult.Ok();
            }

            if (!await _device.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Fail(
                    "KMBox Net connect failed. endpoint=" + _options.EndpointText());
            }

            _connected = true;
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _connected = false;
            return OperationResult.Fail(
                "KMBox Net connect failed. endpoint=" + _options.EndpointText() +
                " error=" + ex.Message);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KmBoxNetKeyboardInput));
        }
    }

    private static OperationResult UnsupportedKey(string key, string label)
    {
        return OperationResult.Fail(
            "Unsupported " + label + ": " + key +
            ". Allowed keys: " + RoadhogInputKeyMap.FormatSupportedKeys());
    }

    private static int ToHoldMilliseconds(TimeSpan holdDuration)
    {
        if (holdDuration <= TimeSpan.Zero)
        {
            return 0;
        }

        if (holdDuration.TotalMilliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Ceiling(holdDuration.TotalMilliseconds);
    }

    private static bool TryConvertMouseButton(
        RoadhogMouseButton button,
        out KmBoxMouseButton kmButton,
        out string error)
    {
        switch (button)
        {
            case RoadhogMouseButton.Left:
                kmButton = KmBoxMouseButton.Left;
                error = string.Empty;
                return true;
            case RoadhogMouseButton.Right:
                kmButton = KmBoxMouseButton.Right;
                error = string.Empty;
                return true;
            case RoadhogMouseButton.Middle:
                kmButton = KmBoxMouseButton.Middle;
                error = string.Empty;
                return true;
            default:
                kmButton = default;
                error = "KMBox Net does not support mouse button: " + button + ".";
                return false;
        }
    }
}
