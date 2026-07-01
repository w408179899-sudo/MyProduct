using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using Hardware.KmBox;

var options = LoadOptions();

try
{
    using var device = new KmBoxNetDevice(options);

    Console.WriteLine("Connecting to KMBox Net " + options.IpAddress + ":" + options.Port + " ...");
    if (!await device.ConnectAsync())
    {
        Console.WriteLine("Connect failed. Check KmBox settings in appsettings.json or KMBOX_* environment variables.");
        return 1;
    }

    var testMode = Environment.GetEnvironmentVariable("KMBOX_TEST_MODE") ?? "wheel";
    Console.WriteLine("Connected. Running " + testMode + " test in 2 seconds.");
    await Task.Delay(TimeSpan.FromSeconds(2));

    try
    {
        if (IsLeftClickLoopMode(testMode))
        {
            await RunLeftClickLoopAsync(device);
        }
        else if (IsMoveToSequenceMode(testMode))
        {
            await RunMoveToSequenceAsync(device);
        }
        else if (IsOriginMoveToSequenceMode(testMode))
        {
            await RunOriginMoveToSequenceAsync(device);
        }
        else if (IsOriginMoveSingleMode(testMode))
        {
            await RunOriginMoveSingleAsync(device);
        }
        else if (IsOriginMoveClickMode(testMode))
        {
            await RunOriginMoveClickAsync(device);
        }
        else if (IsRelativeMoveSequenceMode(testMode))
        {
            await RunRelativeMoveSequenceAsync(device);
        }
        else if (IsFaceTargetTurnMode(testMode))
        {
            await RunFaceTargetTurnTestAsync(device);
        }
        else if (IsKeyboardTypeMode(testMode))
        {
            await RunKeyboardTypeTestAsync(device);
        }
        else if (IsWheelMode(testMode))
        {
            await RunWheelTestAsync(device);
        }
        else if (string.Equals(testMode, "connect_only", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Connect-only test finished.");
        }
        else
        {
            await RunSmokeTestAsync(device);
        }

        return 0;
    }
    finally
    {
        await device.ReleaseAllAsync();
        await device.DisconnectAsync();
        Console.WriteLine("Released all KMBox input states.");
    }
}
catch (ArgumentException ex)
{
    Console.WriteLine("Invalid KMBox config: " + ex.Message);
    Console.WriteLine("Set Hardware.KmBox.Test/appsettings.json or KMBOX_IP, KMBOX_PORT, KMBOX_MAC.");
    return 1;
}

static KmBoxOptions LoadOptions()
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    KmBoxOptions options;

    if (File.Exists(configPath))
    {
        using var stream = File.OpenRead(configPath);
        var document = JsonSerializer.Deserialize<AppSettingsDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        options = document?.KmBox ?? new KmBoxOptions();
    }
    else
    {
        options = new KmBoxOptions();
    }

    ApplyEnvironmentOverrides(options);
    return options;
}

static async Task RunSmokeTestAsync(KmBoxNetDevice device)
{
    await device.MoveMouseAsync(80, 0);
    await Task.Delay(150);

    await device.MoveMouseSmoothAsync(-80, 0, 300);
    await Task.Delay(150);

    await device.ClickAsync(MouseButton.Left);
    await Task.Delay(150);

    await device.WheelAsync(1);
    await Task.Delay(150);

    await device.TypeTextAsync("KMBox Net smoke test");
    await Task.Delay(150);

    await device.KeyDownAsync(Keys.Enter);
    await Task.Delay(60);
    await device.KeyUpAsync(Keys.Enter);

    await device.PressKeyAsync(KmBoxKeyCodes.KEY_ENTER);
    await device.HotkeyAsync(new[]
    {
        KmBoxKeyCodes.KEY_LEFTCONTROL,
        KmBoxKeyCodes.KEY_A
    });

    Console.WriteLine("Smoke test finished.");
}

static async Task RunLeftClickLoopAsync(KmBoxNetDevice device)
{
    var clickCount = ClampInt(ReadIntFromEnv("KMBOX_LEFT_CLICK_COUNT", 20), 1, 10000);
    var holdMs = ClampInt(ReadIntFromEnv("KMBOX_LEFT_CLICK_HOLD_MS", 30), 0, 5000);
    var intervalMs = ClampInt(ReadIntFromEnv("KMBOX_LEFT_CLICK_INTERVAL_MS", 100), 0, 60000);

    Console.WriteLine(
        "Left click loop: count=" + clickCount +
        " holdMs=" + holdMs +
        " intervalMs=" + intervalMs + ".");

    for (var i = 1; i <= clickCount; i++)
    {
        Console.WriteLine("Left click " + i + "/" + clickCount + ".");
        await device.MouseDownAsync(MouseButton.Left);
        if (holdMs > 0)
        {
            await Task.Delay(holdMs);
        }

        await device.MouseUpAsync(MouseButton.Left);
        if (i < clickCount && intervalMs > 0)
        {
            await Task.Delay(intervalMs);
        }
    }

    Console.WriteLine("Left click loop finished.");
}

static async Task RunMoveToSequenceAsync(KmBoxNetDevice device)
{
    var points = ParseMoveToPoints(Environment.GetEnvironmentVariable("KMBOX_MOVE_TO_POINTS") ?? "100,200;300,400");
    var settleMs = ClampInt(ReadIntFromEnv("KMBOX_MOVE_TO_SETTLE_MS", 250), 0, 10000);
    var smoothMs = ClampInt(ReadIntFromEnv("KMBOX_MOVE_TO_SMOOTH_MS", 0), 0, 60000);

    Console.WriteLine(
        "Move-to sequence: points=" + FormatMoveToPoints(points) +
        " smoothMs=" + smoothMs +
        " settleMs=" + settleMs + ".");

    for (var i = 0; i < points.Count; i++)
    {
        var point = points[i];
        var before = Cursor.Position;
        var deltaX = point.X - before.X;
        var deltaY = point.Y - before.Y;

        Console.WriteLine(
            "Move " + (i + 1) + "/" + points.Count +
            ": before=" + before.X + "," + before.Y +
            " target=" + point.X + "," + point.Y +
            " delta=" + deltaX + "," + deltaY + ".");

        if (smoothMs > 0)
        {
            await device.MoveMouseSmoothAsync(deltaX, deltaY, smoothMs);
        }
        else
        {
            await device.MoveMouseAsync(deltaX, deltaY);
        }

        if (settleMs > 0)
        {
            await Task.Delay(settleMs);
        }

        var after = Cursor.Position;
        Console.WriteLine("Move " + (i + 1) + " after=" + after.X + "," + after.Y + ".");
    }

    Console.WriteLine("Move-to sequence finished.");
}

static async Task RunOriginMoveToSequenceAsync(KmBoxNetDevice device)
{
    var targets = ParseMoveToPoints(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_MOVE_TARGETS") ?? "100,200;300,400");
    var origin = ParseSinglePoint(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_MOVE_ORIGIN") ?? "1,1", "KMBOX_ORIGIN_MOVE_ORIGIN");
    var resetDelta = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_DELTA", -32768), short.MinValue, short.MaxValue);
    var options = new KmBoxAbsoluteMoveOptions
    {
        OriginX = origin.X,
        OriginY = origin.Y,
        ResetDeltaX = resetDelta,
        ResetDeltaY = resetDelta,
        ResetCount = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_COUNT", 3), 1, 20),
        StepDelayMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SETTLE_MS", 0), 0, 10000),
        TargetMoveDurationMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SMOOTH_MS", 0), 0, 60000)
    };

    Console.WriteLine(
        "Origin move-to sequence: targets=" + FormatMoveToPoints(targets) +
        " origin=" + options.OriginX + "," + options.OriginY +
        " resetDelta=" + options.ResetDeltaX + "," + options.ResetDeltaY +
        " resetCount=" + options.ResetCount +
        " smoothMs=" + options.TargetMoveDurationMs +
        " settleMs=" + options.StepDelayMs + ".");

    for (var i = 0; i < targets.Count; i++)
    {
        var target = targets[i];
        Console.WriteLine(
            "Origin move " + (i + 1) + "/" + targets.Count +
            ": target=" + target.X + "," + target.Y +
            " via MoveMouseToAsync.");
        await device.MoveMouseToAsync(target.X, target.Y, options);
    }

    Console.WriteLine("Origin move-to sequence finished.");
}

static async Task RunOriginMoveSingleAsync(KmBoxNetDevice device)
{
    var target = ParseSinglePoint(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_MOVE_TARGET") ?? "670,450", "KMBOX_ORIGIN_MOVE_TARGET");
    var origin = ParseSinglePoint(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_MOVE_ORIGIN") ?? "1,1", "KMBOX_ORIGIN_MOVE_ORIGIN");
    var resetDelta = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_DELTA", -32768), short.MinValue, short.MaxValue);
    var options = new KmBoxAbsoluteMoveOptions
    {
        OriginX = origin.X,
        OriginY = origin.Y,
        ResetDeltaX = resetDelta,
        ResetDeltaY = resetDelta,
        ResetCount = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_COUNT", 3), 1, 20),
        StepDelayMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SETTLE_MS", 0), 0, 10000),
        TargetMoveDurationMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SMOOTH_MS", 0), 0, 60000)
    };

    Console.WriteLine(
        "Origin move single: target=" + target.X + "," + target.Y +
        " origin=" + options.OriginX + "," + options.OriginY +
        " resetDelta=" + options.ResetDeltaX + "," + options.ResetDeltaY +
        " resetCount=" + options.ResetCount +
        " smoothMs=" + options.TargetMoveDurationMs +
        " settleMs=" + options.StepDelayMs + ".");

    await device.MoveMouseToAsync(target.X, target.Y, options);
    Console.WriteLine("Origin move single finished.");
}

static async Task RunOriginMoveClickAsync(KmBoxNetDevice device)
{
    var target = ParseSinglePoint(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_CLICK_TARGET") ?? "600,400", "KMBOX_ORIGIN_CLICK_TARGET");
    var origin = ParseSinglePoint(Environment.GetEnvironmentVariable("KMBOX_ORIGIN_MOVE_ORIGIN") ?? "1,1", "KMBOX_ORIGIN_MOVE_ORIGIN");
    var resetDelta = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_DELTA", -32768), short.MinValue, short.MaxValue);
    var beforeClickMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_CLICK_BEFORE_CLICK_MS", 150), 0, 10000);
    var options = new KmBoxAbsoluteMoveOptions
    {
        OriginX = origin.X,
        OriginY = origin.Y,
        ResetDeltaX = resetDelta,
        ResetDeltaY = resetDelta,
        ResetCount = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_RESET_COUNT", 3), 1, 20),
        StepDelayMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SETTLE_MS", 0), 0, 10000),
        TargetMoveDurationMs = ClampInt(ReadIntFromEnv("KMBOX_ORIGIN_MOVE_SMOOTH_MS", 0), 0, 60000)
    };

    Console.WriteLine(
        "Origin move click: target=" + target.X + "," + target.Y +
        " origin=" + options.OriginX + "," + options.OriginY +
        " resetDelta=" + options.ResetDeltaX + "," + options.ResetDeltaY +
        " resetCount=" + options.ResetCount +
        " smoothMs=" + options.TargetMoveDurationMs +
        " settleMs=" + options.StepDelayMs +
        " beforeClickMs=" + beforeClickMs + ".");

    await device.MoveMouseToAsync(target.X, target.Y, options);
    if (beforeClickMs > 0)
    {
        await Task.Delay(beforeClickMs);
    }

    Console.WriteLine("Left click at target.");
    await device.ClickAsync(MouseButton.Left);
    Console.WriteLine("Origin move click finished.");
}

static async Task RunRelativeMoveSequenceAsync(KmBoxNetDevice device)
{
    var deltas = ParseMoveToPoints(Environment.GetEnvironmentVariable("KMBOX_RELATIVE_MOVE_DELTAS") ?? "100,200;300,400");
    var settleMs = ClampInt(ReadIntFromEnv("KMBOX_RELATIVE_MOVE_SETTLE_MS", 500), 0, 10000);
    var smoothMs = ClampInt(ReadIntFromEnv("KMBOX_RELATIVE_MOVE_SMOOTH_MS", 0), 0, 60000);
    var holdRight = ReadBoolFromEnv("KMBOX_RELATIVE_MOVE_HOLD_RIGHT", false);
    var rightWarmupMs = ClampInt(ReadIntFromEnv("KMBOX_RELATIVE_MOVE_RIGHT_WARMUP_MS", 8), 0, 1000);
    var verbose = ReadBoolFromEnv("KMBOX_RELATIVE_MOVE_VERBOSE", true);

    Console.WriteLine(
        "Relative move sequence: deltas=" + FormatMoveToPoints(deltas) +
        " smoothMs=" + smoothMs +
        " settleMs=" + settleMs +
        " holdRight=" + holdRight +
        " verbose=" + verbose + ".");

    if (holdRight)
    {
        Console.WriteLine("Right mouse down before relative move sequence.");
        await device.MouseUpAsync(MouseButton.Right);
        if (rightWarmupMs > 0)
        {
            await Task.Delay(rightWarmupMs);
        }

        await device.MouseDownAsync(MouseButton.Right);
        if (rightWarmupMs > 0)
        {
            await Task.Delay(rightWarmupMs);
        }
    }

    try
    {
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < deltas.Count; i++)
        {
            var delta = deltas[i];
            var before = Cursor.Position;

            if (verbose)
            {
                Console.WriteLine(
                    "Relative move " + (i + 1) + "/" + deltas.Count +
                    ": before=" + before.X + "," + before.Y +
                    " delta=" + delta.X + "," + delta.Y + ".");
            }

            if (smoothMs > 0)
            {
                await device.MoveMouseSmoothAsync(delta.X, delta.Y, smoothMs);
            }
            else
            {
                await device.MoveMouseAsync(delta.X, delta.Y);
            }

            if (settleMs > 0)
            {
                await Task.Delay(settleMs);
            }

            var after = Cursor.Position;
            if (verbose)
            {
                Console.WriteLine("Relative move " + (i + 1) + " after=" + after.X + "," + after.Y + ".");
            }
        }

        stopwatch.Stop();
        Console.WriteLine(
            "Relative move timing: commands=" + deltas.Count +
            " elapsedMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) +
            " avgMs=" + (stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, deltas.Count)).ToString("0.###", CultureInfo.InvariantCulture) + ".");
    }
    finally
    {
        if (holdRight)
        {
            await device.MouseUpAsync(MouseButton.Right);
            Console.WriteLine("Right mouse up after relative move sequence.");
        }
    }

    Console.WriteLine("Relative move sequence finished.");
}

static async Task RunFaceTargetTurnTestAsync(KmBoxNetDevice device)
{
    var player = ParseVector3(
        Environment.GetEnvironmentVariable("KMBOX_FACE_PLAYER_POS") ?? "0,0,0",
        "KMBOX_FACE_PLAYER_POS");
    var target = ParseVector3(
        Environment.GetEnvironmentVariable("KMBOX_FACE_TARGET_POS") ?? "5,0,0",
        "KMBOX_FACE_TARGET_POS");
    var currentYaw = ReadDoubleFromEnv("KMBOX_FACE_CURRENT_YAW", 120.0D);
    var currentPitch = ReadDoubleFromEnv("KMBOX_FACE_CURRENT_PITCH", 20.0D);
    var options = LoadFaceTargetTurnOptions();
    var snapshot = BuildFaceTargetSnapshot(player, target, currentYaw, currentPitch, options);
    var needsTurn =
        Math.Abs(snapshot.YawError) > options.YawToleranceDegrees ||
        Math.Abs(snapshot.PitchError) > options.PitchToleranceDegrees;

    Console.WriteLine(
        "Face target turn: player=" + FormatVector3(player) +
        " target=" + FormatVector3(target) +
        " currentYaw=" + FormatDouble(snapshot.CurrentYaw) +
        " targetYaw=" + FormatDouble(snapshot.TargetYaw) +
        " yawError=" + FormatDouble(snapshot.YawError) +
        " currentPitch=" + FormatDouble(snapshot.CurrentPitch) +
        " worldPitch=" + FormatDouble(snapshot.WorldPitch) +
        " targetPitch=" + FormatDouble(snapshot.TargetPitch) +
        " pitchError=" + FormatDouble(snapshot.PitchError) + ".");
    Console.WriteLine(
        "Face target options: yawPxPerDeg=" + FormatDouble(options.PixelsPerDegreeAbs) +
        " pitchPxPerDeg=" + FormatDouble(options.PitchPixelsPerDegreeAbs) +
        " yawTolerance=" + FormatDouble(options.YawToleranceDegrees) +
        " pitchTolerance=" + FormatDouble(options.PitchToleranceDegrees) +
        " stepPx=" + options.DragStepPixels +
        " primeTail=" + options.DragPrimePixels + "/" + options.DragTailPixels +
        " stepDelayMs=" + options.DragStepDelayMs +
        " warmupMs=" + options.MouseDownWarmupMs +
        " holdAfterMoveMs=" + options.MouseHoldAfterMoveMs + ".");

    if (!needsTurn)
    {
        Console.WriteLine("Face target already inside tolerance; no mouse movement sent.");
        return;
    }

    var dx = CalculateCameraDragDx(
        snapshot.YawError,
        options.PixelsPerDegreeAbs,
        options,
        applyMinCorrection: false,
        out var rawDx,
        out _);
    var dy = CalculateCameraDragDy(
        snapshot.PitchError,
        options.PitchPixelsPerDegreeAbs,
        options,
        applyMinCorrection: false,
        out var rawDy,
        out _);
    var xChunks = BuildSignedCameraChunks(dx, options);
    var yChunks = BuildSignedCameraChunks(dy, options);
    var count = Math.Max(xChunks.Length, yChunks.Length);
    var leaveRightDown = ReadBoolFromEnv("KMBOX_FACE_LEAVE_RIGHT_DOWN", false);

    Console.WriteLine(
        "Face target planned drag: rawDx=" + FormatDouble(rawDx) +
        " rawDy=" + FormatDouble(rawDy) +
        " dx=" + dx +
        " dy=" + dy +
        " moveCommands=" + count + ".");
    Console.WriteLine("Face target chunks X=" + FormatChunks(xChunks));
    Console.WriteLine("Face target chunks Y=" + FormatChunks(yChunks));

    Console.WriteLine("Right mouse down.");
    await device.MouseUpAsync(MouseButton.Right);
    await Task.Delay(8);
    await device.MouseDownAsync(MouseButton.Right);
    if (options.MouseDownWarmupMs > 0)
    {
        await Task.Delay(options.MouseDownWarmupMs);
    }

    try
    {
        for (var i = 0; i < count; i++)
        {
            var stepX = i < xChunks.Length ? xChunks[i] : 0;
            var stepY = i < yChunks.Length ? yChunks[i] : 0;
            if (stepX == 0 && stepY == 0)
            {
                continue;
            }

            Console.WriteLine(
                "Face target move " + (i + 1) + "/" + count +
                ": dx=" + stepX +
                " dy=" + stepY + ".");
            await device.MoveMouseAsync(stepX, stepY);
            if (options.DragStepDelayMs > 0)
            {
                await Task.Delay(options.DragStepDelayMs);
            }
        }

        if (options.MouseHoldAfterMoveMs > 0)
        {
            await Task.Delay(options.MouseHoldAfterMoveMs);
        }
    }
    finally
    {
        if (!leaveRightDown)
        {
            await device.MouseUpAsync(MouseButton.Right);
            Console.WriteLine("Right mouse up.");
        }
        else
        {
            Console.WriteLine("Right mouse left down by KMBOX_FACE_LEAVE_RIGHT_DOWN.");
        }
    }

    Console.WriteLine("Face target turn test finished.");
}

static async Task RunKeyboardTypeTestAsync(KmBoxNetDevice device)
{
    var text = Environment.GetEnvironmentVariable("KMBOX_KEYBOARD_TEXT") ?? "kmbox keyboard test 123";
    var pressEnter = ReadBoolFromEnv("KMBOX_KEYBOARD_ENTER", false);

    Console.WriteLine("Keyboard type text: " + text);
    await device.TypeTextAsync(text);

    if (pressEnter)
    {
        Console.WriteLine("Keyboard press: Enter.");
        await device.PressKeyAsync(KmBoxKeyCodes.KEY_ENTER);
    }

    Console.WriteLine("Keyboard type test finished.");
}

static async Task RunWheelTestAsync(KmBoxNetDevice device)
{
    var delta = ClampInt(ReadIntFromEnv("KMBOX_WHEEL_DELTA", 1), -120, 120);
    var count = ClampInt(ReadIntFromEnv("KMBOX_WHEEL_COUNT", 1), 1, 100);
    var intervalMs = ClampInt(ReadIntFromEnv("KMBOX_WHEEL_INTERVAL_MS", 100), 0, 60000);

    Console.WriteLine(
        "Wheel test: delta=" + delta +
        " count=" + count +
        " intervalMs=" + intervalMs + ".");

    for (var i = 1; i <= count; i++)
    {
        Console.WriteLine("Wheel " + i + "/" + count + ": delta=" + delta + ".");
        await device.WheelAsync(delta);

        if (i < count && intervalMs > 0)
        {
            await Task.Delay(intervalMs);
        }
    }

    Console.WriteLine("Wheel test finished.");
}

static bool IsLeftClickLoopMode(string testMode)
{
    return string.Equals(testMode, "left_click_loop", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "left_clicks", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "click_loop", StringComparison.OrdinalIgnoreCase);
}

static bool IsMoveToSequenceMode(string testMode)
{
    return string.Equals(testMode, "move_to", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "moveto", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "absolute_move", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "cursor_move", StringComparison.OrdinalIgnoreCase);
}

static bool IsOriginMoveToSequenceMode(string testMode)
{
    return string.Equals(testMode, "origin_move_to", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "edge_move_to", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "calibrated_move_to", StringComparison.OrdinalIgnoreCase);
}

static bool IsOriginMoveSingleMode(string testMode)
{
    return string.Equals(testMode, "origin_move_single", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "absolute_move_single", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "move_to_single", StringComparison.OrdinalIgnoreCase);
}

static bool IsOriginMoveClickMode(string testMode)
{
    return string.Equals(testMode, "origin_move_click", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "absolute_click", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "move_to_click", StringComparison.OrdinalIgnoreCase);
}

static bool IsRelativeMoveSequenceMode(string testMode)
{
    return string.Equals(testMode, "relative_move", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "move_relative", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "relative_moves", StringComparison.OrdinalIgnoreCase);
}

static bool IsFaceTargetTurnMode(string testMode)
{
    return string.Equals(testMode, "face_target", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "face_target_turn", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "turn_to_target", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "camera_turn", StringComparison.OrdinalIgnoreCase);
}

static bool IsKeyboardTypeMode(string testMode)
{
    return string.Equals(testMode, "keyboard_type", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "type_text", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "keyboard", StringComparison.OrdinalIgnoreCase);
}

static bool IsWheelMode(string testMode)
{
    return string.Equals(testMode, "wheel", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "mouse_wheel", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(testMode, "scroll", StringComparison.OrdinalIgnoreCase);
}

static void ApplyEnvironmentOverrides(KmBoxOptions options)
{
    var ipAddress = Environment.GetEnvironmentVariable("KMBOX_IP");
    if (!string.IsNullOrWhiteSpace(ipAddress))
    {
        options.IpAddress = ipAddress;
    }

    var port = Environment.GetEnvironmentVariable("KMBOX_PORT");
    if (int.TryParse(port, out var parsedPort))
    {
        options.Port = parsedPort;
    }

    var mac = Environment.GetEnvironmentVariable("KMBOX_MAC");
    if (!string.IsNullOrWhiteSpace(mac))
    {
        options.Mac = mac;
    }
}

static int ReadIntFromEnv(string name, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        ? value
        : fallback;
}

static int ReadRawIntFromEnv(string name, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
}

static double ReadDoubleFromEnv(string name, double fallback)
{
    return double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
}

static bool ReadBoolFromEnv(string name, bool fallback)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("on", StringComparison.OrdinalIgnoreCase);
}

static int ClampInt(int value, int min, int max)
{
    if (value < min)
    {
        return min;
    }

    return value > max ? max : value;
}

static double ClampDouble(double value, double min, double max)
{
    if (value < min)
    {
        return min;
    }

    return value > max ? max : value;
}

static List<(int X, int Y)> ParseMoveToPoints(string pointsText)
{
    var points = new List<(int X, int Y)>();
    var segments = pointsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var segment in segments)
    {
        var parts = segment.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var x) ||
            !int.TryParse(parts[1], out var y))
        {
            throw new ArgumentException(
                "Invalid KMBOX_MOVE_TO_POINTS segment '" + segment +
                "'. Expected format like 100,200;300,400.");
        }

        points.Add((x, y));
    }

    if (points.Count == 0)
    {
        throw new ArgumentException("KMBOX_MOVE_TO_POINTS must include at least one x,y point.");
    }

    return points;
}

static (int X, int Y) ParseSinglePoint(string pointText, string environmentName)
{
    var points = ParseMoveToPoints(pointText);
    if (points.Count != 1)
    {
        throw new ArgumentException(environmentName + " must include exactly one x,y point.");
    }

    return points[0];
}

static string FormatMoveToPoints(IReadOnlyList<(int X, int Y)> points)
{
    return string.Join(";", points.Select(point => point.X + "," + point.Y));
}

static FaceTargetTurnOptions LoadFaceTargetTurnOptions()
{
    var fixedTargetPitch = ReadDoubleFromEnv("AION_CAMERA_FIXED_PITCH_DEG", 20.0D);
    var targetPitch = ClampDouble(ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_DEG", fixedTargetPitch), -65.0D, 85.0D);
    var minTargetPitch = ClampDouble(ReadDoubleFromEnv("AION_CAMERA_TARGET_PITCH_MIN_DEG", -65.0D), -89.0D, 89.0D);
    var maxTargetPitch = ClampDouble(ReadDoubleFromEnv("AION_CAMERA_TARGET_PITCH_MAX_DEG", 85.0D), -89.0D, 89.0D);
    if (minTargetPitch > maxTargetPitch)
    {
        (minTargetPitch, maxTargetPitch) = (maxTargetPitch, minTargetPitch);
    }

    var yawPixelsPerDegree = Math.Abs(
        ReadDoubleFromEnv(
            "KMBOX_FACE_YAW_PIXELS_PER_DEG",
            ReadDoubleFromEnv("AION_FACE_TARGET_PIXELS_PER_DEG_ABS", 11.0D)));
    if (yawPixelsPerDegree < 0.0001D)
    {
        yawPixelsPerDegree = 11.0D;
    }

    var pitchPixelsPerDegree = Math.Abs(
        ReadDoubleFromEnv(
            "KMBOX_FACE_PITCH_PIXELS_PER_DEG",
            ReadDoubleFromEnv("AION_CAMERA_PITCH_PIXELS_PER_DEG_ABS", 13.0D)));
    if (pitchPixelsPerDegree < 0.0001D)
    {
        pitchPixelsPerDegree = 13.0D;
    }

    return new FaceTargetTurnOptions
    {
        MouseDownWarmupMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_DOWN_WARMUP_MS", 0), 0, 1000),
        MouseHoldAfterMoveMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MOUSE_HOLD_AFTER_MOVE_MS", 0), 0, 1000),
        MinCorrectionPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_MIN_CORRECTION_PIXELS", 70), 0, 500),
        YawToleranceDegrees = Math.Max(0.1D, ReadDoubleFromEnv("AION_PATH_FOLLOW_YAW_TOLERANCE_DEG", 20.0D)),
        PitchToleranceDegrees = Math.Max(0.5D, ReadDoubleFromEnv("AION_PATH_FOLLOW_PITCH_TOLERANCE_DEG", 5.0D)),
        TargetPitchDegrees = targetPitch,
        UseWorldTargetPitch = ReadBoolFromEnv("AION_CAMERA_USE_WORLD_TARGET_PITCH", true),
        MinTargetPitchDegrees = minTargetPitch,
        MaxTargetPitchDegrees = maxTargetPitch,
        PixelsPerDegreeAbs = yawPixelsPerDegree,
        PitchPixelsPerDegreeAbs = pitchPixelsPerDegree,
        DragPrimePixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_PRIME_PIXELS", 5), 0, 50),
        DragTailPixels = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_TAIL_PIXELS", 5), 0, 50),
        DragStepPixels = ClampInt(Math.Abs(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_PX", 20)), 1, 500),
        DragStepDelayMs = ClampInt(ReadRawIntFromEnv("AION_FACE_TARGET_DRAG_STEP_DELAY_MS", 0), 0, 50),
        PitchInvertMouse = ReadBoolFromEnv("AION_CAMERA_PITCH_INVERT_MOUSE", false)
    };
}

static FaceTargetSnapshot BuildFaceTargetSnapshot(
    Vector3D player,
    Vector3D target,
    double currentYaw,
    double currentPitch,
    FaceTargetTurnOptions options)
{
    var worldPitch = CalculateWorldPitchDegrees(player, target);
    var targetPitch = ResolveTargetPitchDegrees(worldPitch, options);
    var targetYaw = CalculateTargetYawDegrees(player, target);
    return new FaceTargetSnapshot(
        currentYaw,
        currentPitch,
        targetYaw,
        worldPitch,
        targetPitch,
        NormalizeSignedDegrees(targetYaw - currentYaw),
        targetPitch - currentPitch);
}

static double CalculateWorldPitchDegrees(Vector3D source, Vector3D target)
{
    var horizontalDistance = HorizontalDistance(source, target);
    var dz = source.Z - target.Z;
    return Math.Atan2(dz, Math.Max(0.001D, horizontalDistance)) * 180.0D / Math.PI;
}

static double ResolveTargetPitchDegrees(double worldPitchDegrees, FaceTargetTurnOptions options)
{
    if (!options.UseWorldTargetPitch)
    {
        return options.TargetPitchDegrees;
    }

    return ClampDouble(
        worldPitchDegrees + 10.0D,
        options.MinTargetPitchDegrees,
        options.MaxTargetPitchDegrees);
}

static double CalculateTargetYawDegrees(Vector3D source, Vector3D target)
{
    var dx = target.X - source.X;
    var dy = target.Y - source.Y;
    var mode = (Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE") ?? "y-x").Trim().ToLowerInvariant();
    double angleRadians = mode switch
    {
        "xy" => Math.Atan2(dy, dx),
        "negxy" or "-xy" => Math.Atan2(-dy, dx),
        "xnegy" or "x-y" => Math.Atan2(dy, -dx),
        "negyx" or "-yx" => Math.Atan2(-dx, dy),
        "ynegx" or "y-x" => Math.Atan2(dx, -dy),
        _ => Math.Atan2(dx, dy)
    };
    return NormalizeSignedDegrees(angleRadians * 180.0D / Math.PI + ReadDoubleFromEnv("AION_FACE_TARGET_YAW_OFFSET_DEG", 0.0D));
}

static int CalculateCameraDragDx(
    double errorDegrees,
    double pixelsPerDegreeAbs,
    FaceTargetTurnOptions options,
    bool applyMinCorrection,
    out double rawDx,
    out bool minApplied)
{
    rawDx = -errorDegrees * pixelsPerDegreeAbs;
    minApplied = false;

    var dx = (int)Math.Round(rawDx, MidpointRounding.AwayFromZero);
    if (dx == 0)
    {
        dx = errorDegrees > 0.0D ? -1 : 1;
    }

    var sign = dx < 0 ? -1 : 1;
    var absDx = Math.Abs(dx);
    if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDx < options.MinCorrectionPixels)
    {
        dx = sign * options.MinCorrectionPixels;
        minApplied = true;
    }

    return dx;
}

static int CalculateCameraDragDy(
    double errorDegrees,
    double pixelsPerDegreeAbs,
    FaceTargetTurnOptions options,
    bool applyMinCorrection,
    out double rawDy,
    out bool minApplied)
{
    rawDy = errorDegrees * pixelsPerDegreeAbs;
    if (options.PitchInvertMouse)
    {
        rawDy = -rawDy;
    }

    minApplied = false;
    var dy = (int)Math.Round(rawDy, MidpointRounding.AwayFromZero);
    if (dy == 0)
    {
        dy = errorDegrees > 0.0D ? 1 : -1;
        if (options.PitchInvertMouse)
        {
            dy = -dy;
        }
    }

    var sign = dy < 0 ? -1 : 1;
    var absDy = Math.Abs(dy);
    if (applyMinCorrection && options.MinCorrectionPixels > 0 && absDy < options.MinCorrectionPixels)
    {
        dy = sign * options.MinCorrectionPixels;
        minApplied = true;
    }

    return dy;
}

static int[] BuildSignedCameraChunks(int pixels, FaceTargetTurnOptions options)
{
    if (pixels == 0)
    {
        return Array.Empty<int>();
    }

    var sign = pixels < 0 ? -1 : 1;
    var remaining = Math.Abs(pixels);
    var chunks = new List<int>();
    var prime = Math.Min(Math.Max(0, options.DragPrimePixels), remaining);
    for (var i = 0; i < prime; i++)
    {
        chunks.Add(sign);
        remaining--;
    }

    var tail = Math.Min(Math.Max(0, options.DragTailPixels), remaining);
    var chunkRemaining = remaining - tail;
    var middleChunks = BuildGradientChunks(chunkRemaining, Math.Max(1, options.DragStepPixels));
    for (var i = 0; i < middleChunks.Length; i++)
    {
        chunks.Add(sign * middleChunks[i]);
    }

    for (var i = 0; i < tail; i++)
    {
        chunks.Add(sign);
    }

    return chunks.ToArray();
}

static int[] BuildGradientChunks(int totalPixels, int maxStep)
{
    if (totalPixels <= 0)
    {
        return Array.Empty<int>();
    }

    maxStep = Math.Max(1, maxStep);
    var length = 1;
    while (GetMaxGradientSum(length, maxStep) < totalPixels)
    {
        length++;
    }

    var chunks = new int[length];
    for (var i = 0; i < chunks.Length; i++)
    {
        chunks[i] = 1;
    }

    var remaining = totalPixels - chunks.Length;
    var centerOutOrder = BuildCenterOutIndexOrder(chunks.Length);
    while (remaining > 0)
    {
        var raised = false;
        for (var i = 0; i < centerOutOrder.Length && remaining > 0; i++)
        {
            var index = centerOutOrder[i];
            if (!CanRaiseGradientChunk(chunks, index, maxStep))
            {
                continue;
            }

            chunks[index]++;
            remaining--;
            raised = true;
        }

        if (!raised)
        {
            break;
        }
    }

    return chunks;
}

static int GetMaxGradientSum(int length, int maxStep)
{
    var sum = 0;
    for (var i = 0; i < length; i++)
    {
        var distanceToEdge = Math.Min(i, length - 1 - i);
        sum += Math.Min(maxStep, distanceToEdge + 1);
    }

    return sum;
}

static int[] BuildCenterOutIndexOrder(int length)
{
    var order = new List<int>();
    var leftCenter = (length - 1) / 2;
    var rightCenter = length / 2;
    for (var offset = 0; order.Count < length; offset++)
    {
        var left = leftCenter - offset;
        if (left >= 0)
        {
            order.Add(left);
        }

        var right = rightCenter + offset;
        if (right != left && right < length)
        {
            order.Add(right);
        }
    }

    return order.ToArray();
}

static bool CanRaiseGradientChunk(int[] chunks, int index, int maxStep)
{
    if (chunks[index] >= maxStep)
    {
        return false;
    }

    if (chunks.Length > 1 && (index == 0 || index == chunks.Length - 1))
    {
        return false;
    }

    var nextValue = chunks[index] + 1;
    if (index > 0 && Math.Abs(nextValue - chunks[index - 1]) > 1)
    {
        return false;
    }

    if (index + 1 < chunks.Length && Math.Abs(nextValue - chunks[index + 1]) > 1)
    {
        return false;
    }

    return true;
}

static Vector3D ParseVector3(string value, string environmentName)
{
    var parts = value.Split(',', StringSplitOptions.TrimEntries);
    if (parts.Length != 3 ||
        !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
        !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
        !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
    {
        throw new ArgumentException(environmentName + " must use x,y,z format, for example 0,0,0.");
    }

    return new Vector3D(x, y, z);
}

static double HorizontalDistance(Vector3D left, Vector3D right)
{
    var dx = left.X - right.X;
    var dy = left.Y - right.Y;
    return Math.Sqrt(dx * dx + dy * dy);
}

static double NormalizeSignedDegrees(double value)
{
    while (value <= -180.0D)
    {
        value += 360.0D;
    }

    while (value > 180.0D)
    {
        value -= 360.0D;
    }

    return value;
}

static string FormatVector3(Vector3D value)
{
    return FormatDouble(value.X) + "," + FormatDouble(value.Y) + "," + FormatDouble(value.Z);
}

static string FormatDouble(double value)
{
    return value.ToString("0.##", CultureInfo.InvariantCulture);
}

static string FormatChunks(IReadOnlyList<int> chunks)
{
    if (chunks.Count == 0)
    {
        return "(none)";
    }

    return chunks.Count <= 80
        ? string.Join(",", chunks)
        : string.Join(",", chunks.Take(40)) + ",...," + string.Join(",", chunks.Skip(chunks.Count - 20));
}

sealed class AppSettingsDocument
{
    public KmBoxOptions? KmBox { get; set; }
}

sealed record Vector3D(double X, double Y, double Z);

sealed record FaceTargetSnapshot(
    double CurrentYaw,
    double CurrentPitch,
    double TargetYaw,
    double WorldPitch,
    double TargetPitch,
    double YawError,
    double PitchError);

sealed class FaceTargetTurnOptions
{
    public int MouseDownWarmupMs { get; set; }

    public int MouseHoldAfterMoveMs { get; set; }

    public int MinCorrectionPixels { get; set; }

    public double YawToleranceDegrees { get; set; }

    public double PitchToleranceDegrees { get; set; }

    public double TargetPitchDegrees { get; set; }

    public bool UseWorldTargetPitch { get; set; }

    public double MinTargetPitchDegrees { get; set; }

    public double MaxTargetPitchDegrees { get; set; }

    public double PixelsPerDegreeAbs { get; set; }

    public double PitchPixelsPerDegreeAbs { get; set; }

    public int DragPrimePixels { get; set; }

    public int DragTailPixels { get; set; }

    public int DragStepPixels { get; set; }

    public int DragStepDelayMs { get; set; }

    public bool PitchInvertMouse { get; set; }
}
