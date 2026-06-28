using System.Windows.Forms;

namespace Hardware.KmBox;

internal readonly struct HidKeyStroke
{
    public HidKeyStroke(byte button, byte modifiers)
    {
        Button = button;
        Modifiers = modifiers;
    }

    public byte Button { get; }

    public byte Modifiers { get; }
}

internal static class HidKeyboardMap
{
    private const byte LeftControl = 0x01;
    private const byte LeftShift = 0x02;
    private const byte LeftAlt = 0x04;
    private const byte LeftGui = 0x08;
    private const byte RightControl = 0x10;
    private const byte RightShift = 0x20;
    private const byte RightAlt = 0x40;
    private const byte RightGui = 0x80;

    private static readonly IReadOnlyDictionary<Keys, byte> KeyCodes = new Dictionary<Keys, byte>
    {
        [Keys.A] = 0x04,
        [Keys.B] = 0x05,
        [Keys.C] = 0x06,
        [Keys.D] = 0x07,
        [Keys.E] = 0x08,
        [Keys.F] = 0x09,
        [Keys.G] = 0x0a,
        [Keys.H] = 0x0b,
        [Keys.I] = 0x0c,
        [Keys.J] = 0x0d,
        [Keys.K] = 0x0e,
        [Keys.L] = 0x0f,
        [Keys.M] = 0x10,
        [Keys.N] = 0x11,
        [Keys.O] = 0x12,
        [Keys.P] = 0x13,
        [Keys.Q] = 0x14,
        [Keys.R] = 0x15,
        [Keys.S] = 0x16,
        [Keys.T] = 0x17,
        [Keys.U] = 0x18,
        [Keys.V] = 0x19,
        [Keys.W] = 0x1a,
        [Keys.X] = 0x1b,
        [Keys.Y] = 0x1c,
        [Keys.Z] = 0x1d,
        [Keys.D1] = 0x1e,
        [Keys.D2] = 0x1f,
        [Keys.D3] = 0x20,
        [Keys.D4] = 0x21,
        [Keys.D5] = 0x22,
        [Keys.D6] = 0x23,
        [Keys.D7] = 0x24,
        [Keys.D8] = 0x25,
        [Keys.D9] = 0x26,
        [Keys.D0] = 0x27,
        [Keys.Return] = 0x28,
        [Keys.Escape] = 0x29,
        [Keys.Back] = 0x2a,
        [Keys.Tab] = 0x2b,
        [Keys.Space] = 0x2c,
        [Keys.OemMinus] = 0x2d,
        [Keys.Oemplus] = 0x2e,
        [Keys.OemOpenBrackets] = 0x2f,
        [Keys.OemCloseBrackets] = 0x30,
        [Keys.OemPipe] = 0x31,
        [Keys.OemSemicolon] = 0x33,
        [Keys.OemQuotes] = 0x34,
        [Keys.Oemtilde] = 0x35,
        [Keys.Oemcomma] = 0x36,
        [Keys.OemPeriod] = 0x37,
        [Keys.OemQuestion] = 0x38,
        [Keys.CapsLock] = 0x39,
        [Keys.F1] = 0x3a,
        [Keys.F2] = 0x3b,
        [Keys.F3] = 0x3c,
        [Keys.F4] = 0x3d,
        [Keys.F5] = 0x3e,
        [Keys.F6] = 0x3f,
        [Keys.F7] = 0x40,
        [Keys.F8] = 0x41,
        [Keys.F9] = 0x42,
        [Keys.F10] = 0x43,
        [Keys.F11] = 0x44,
        [Keys.F12] = 0x45,
        [Keys.PrintScreen] = 0x46,
        [Keys.Scroll] = 0x47,
        [Keys.Pause] = 0x48,
        [Keys.Insert] = 0x49,
        [Keys.Home] = 0x4a,
        [Keys.PageUp] = 0x4b,
        [Keys.Delete] = 0x4c,
        [Keys.End] = 0x4d,
        [Keys.PageDown] = 0x4e,
        [Keys.Right] = 0x4f,
        [Keys.Left] = 0x50,
        [Keys.Down] = 0x51,
        [Keys.Up] = 0x52,
        [Keys.NumLock] = 0x53,
        [Keys.Divide] = 0x54,
        [Keys.Multiply] = 0x55,
        [Keys.Subtract] = 0x56,
        [Keys.Add] = 0x57,
        [Keys.NumPad1] = 0x59,
        [Keys.NumPad2] = 0x5a,
        [Keys.NumPad3] = 0x5b,
        [Keys.NumPad4] = 0x5c,
        [Keys.NumPad5] = 0x5d,
        [Keys.NumPad6] = 0x5e,
        [Keys.NumPad7] = 0x5f,
        [Keys.NumPad8] = 0x60,
        [Keys.NumPad9] = 0x61,
        [Keys.NumPad0] = 0x62,
        [Keys.Decimal] = 0x63,
        [Keys.Apps] = 0x65,
        [Keys.F13] = 0x68,
        [Keys.F14] = 0x69,
        [Keys.F15] = 0x6a,
        [Keys.F16] = 0x6b,
        [Keys.F17] = 0x6c,
        [Keys.F18] = 0x6d,
        [Keys.F19] = 0x6e,
        [Keys.F20] = 0x6f,
        [Keys.F21] = 0x70,
        [Keys.F22] = 0x71,
        [Keys.F23] = 0x72,
        [Keys.F24] = 0x73
    };

    private static readonly IReadOnlyDictionary<char, HidKeyStroke> CharacterCodes = BuildCharacterCodes();

    public static HidKeyStroke FromKeys(Keys key)
    {
        var modifiers = ModifierFlagsFromKeys(key);
        var keyCode = key & Keys.KeyCode;

        if (TryGetModifierForKeyCode(keyCode, out var keyCodeModifier))
        {
            return new HidKeyStroke(0, (byte)(modifiers | keyCodeModifier));
        }

        if (keyCode == Keys.None)
        {
            if (modifiers == 0)
            {
                throw new NotSupportedException("Unsupported empty key.");
            }

            return new HidKeyStroke(0, modifiers);
        }

        if (!KeyCodes.TryGetValue(keyCode, out var button))
        {
            throw new NotSupportedException("Unsupported KMBox key: " + keyCode + ".");
        }

        return new HidKeyStroke(button, modifiers);
    }

    public static HidKeyStroke FromCharacter(char character)
    {
        if (!CharacterCodes.TryGetValue(character, out var stroke))
        {
            throw new NotSupportedException("Unsupported KMBox text character: U+" + ((int)character).ToString("X4") + ".");
        }

        return stroke;
    }

    public static HidKeyStroke FromKeyCode(int keyCode)
    {
        if (keyCode < 0 || keyCode > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(keyCode), keyCode, "KMBox key code must be between 0 and 255.");
        }

        if (keyCode >= KmBoxKeyCodes.KEY_LEFTCONTROL && keyCode <= KmBoxKeyCodes.KEY_RIGHT_GUI)
        {
            var modifier = (byte)(1 << (keyCode - KmBoxKeyCodes.KEY_LEFTCONTROL));
            return new HidKeyStroke(0, modifier);
        }

        return new HidKeyStroke((byte)keyCode, 0);
    }

    private static byte ModifierFlagsFromKeys(Keys key)
    {
        byte modifiers = 0;
        if ((key & Keys.Control) == Keys.Control)
        {
            modifiers |= LeftControl;
        }

        if ((key & Keys.Shift) == Keys.Shift)
        {
            modifiers |= LeftShift;
        }

        if ((key & Keys.Alt) == Keys.Alt)
        {
            modifiers |= LeftAlt;
        }

        return modifiers;
    }

    private static bool TryGetModifierForKeyCode(Keys keyCode, out byte modifier)
    {
        modifier = keyCode switch
        {
            Keys.ControlKey or Keys.LControlKey => LeftControl,
            Keys.RControlKey => RightControl,
            Keys.ShiftKey or Keys.LShiftKey => LeftShift,
            Keys.RShiftKey => RightShift,
            Keys.Menu or Keys.LMenu => LeftAlt,
            Keys.RMenu => RightAlt,
            Keys.LWin => LeftGui,
            Keys.RWin => RightGui,
            _ => 0
        };

        return modifier != 0;
    }

    private static IReadOnlyDictionary<char, HidKeyStroke> BuildCharacterCodes()
    {
        var values = new Dictionary<char, HidKeyStroke>
        {
            ['\r'] = Key(Keys.Return),
            ['\n'] = Key(Keys.Return),
            ['\t'] = Key(Keys.Tab),
            [' '] = Key(Keys.Space),
            ['-'] = Key(Keys.OemMinus),
            ['_'] = Shift(Keys.OemMinus),
            ['='] = Key(Keys.Oemplus),
            ['+'] = Shift(Keys.Oemplus),
            ['['] = Key(Keys.OemOpenBrackets),
            ['{'] = Shift(Keys.OemOpenBrackets),
            [']'] = Key(Keys.OemCloseBrackets),
            ['}'] = Shift(Keys.OemCloseBrackets),
            ['\\'] = Key(Keys.OemPipe),
            ['|'] = Shift(Keys.OemPipe),
            [';'] = Key(Keys.OemSemicolon),
            [':'] = Shift(Keys.OemSemicolon),
            ['\''] = Key(Keys.OemQuotes),
            ['"'] = Shift(Keys.OemQuotes),
            ['`'] = Key(Keys.Oemtilde),
            ['~'] = Shift(Keys.Oemtilde),
            [','] = Key(Keys.Oemcomma),
            ['<'] = Shift(Keys.Oemcomma),
            ['.'] = Key(Keys.OemPeriod),
            ['>'] = Shift(Keys.OemPeriod),
            ['/'] = Key(Keys.OemQuestion),
            ['?'] = Shift(Keys.OemQuestion),
            ['!'] = Shift(Keys.D1),
            ['@'] = Shift(Keys.D2),
            ['#'] = Shift(Keys.D3),
            ['$'] = Shift(Keys.D4),
            ['%'] = Shift(Keys.D5),
            ['^'] = Shift(Keys.D6),
            ['&'] = Shift(Keys.D7),
            ['*'] = Shift(Keys.D8),
            ['('] = Shift(Keys.D9),
            [')'] = Shift(Keys.D0)
        };

        for (var i = 0; i < 26; i++)
        {
            var lower = (char)('a' + i);
            var upper = (char)('A' + i);
            var key = (Keys)((int)Keys.A + i);
            values[lower] = Key(key);
            values[upper] = Shift(key);
        }

        for (var i = 0; i < 10; i++)
        {
            var digit = (char)('0' + i);
            var key = i == 0 ? Keys.D0 : (Keys)((int)Keys.D1 + (i - 1));
            values[digit] = Key(key);
        }

        return values;
    }

    private static HidKeyStroke Key(Keys key)
    {
        return FromKeys(key);
    }

    private static HidKeyStroke Shift(Keys key)
    {
        var stroke = FromKeys(key);
        return new HidKeyStroke(stroke.Button, (byte)(stroke.Modifiers | LeftShift));
    }
}
