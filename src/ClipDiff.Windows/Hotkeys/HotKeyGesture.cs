using System.Text.Json.Serialization;

namespace ClipDiff.Windows.Hotkeys;

[Flags]
internal enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

internal sealed record HotKeyGesture(HotKeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VirtualKeyF4 = 0x73;
    private const uint VirtualKeyD = 0x44;
    private const uint VirtualKeyLeftWindows = 0x5B;
    private const uint VirtualKeyRightWindows = 0x5C;
    private static readonly HashSet<uint> ModifierVirtualKeys =
    [
        0x10,
        0x11,
        0x12,
        0xA0,
        0xA1,
        0xA2,
        0xA3,
        0xA4,
        0xA5
    ];

    public static HotKeyGesture Default { get; } = new(
        HotKeyModifiers.Control | HotKeyModifiers.Alt,
        VirtualKeyD);

    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            const HotKeyModifiers supportedModifiers =
                HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift;
            var hasPrimaryModifier = (Modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt)) != 0;
            var hasOnlySupportedModifiers = (Modifiers & ~supportedModifiers) == 0;
            var isUsableKey = VirtualKey is > 0 and <= 0xFE &&
                              VirtualKey != VirtualKeyLeftWindows &&
                              VirtualKey != VirtualKeyRightWindows &&
                              !ModifierVirtualKeys.Contains(VirtualKey);
            var isReservedWindowCommand =
                (Modifiers & HotKeyModifiers.Alt) != 0 && VirtualKey == VirtualKeyF4;
            return hasPrimaryModifier && hasOnlySupportedModifiers && isUsableKey && !isReservedWindowCommand;
        }
    }

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>(4);
            if ((Modifiers & HotKeyModifiers.Control) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((Modifiers & HotKeyModifiers.Alt) != 0)
            {
                parts.Add("Alt");
            }

            if ((Modifiers & HotKeyModifiers.Shift) != 0)
            {
                parts.Add("Shift");
            }

            parts.Add(FormatVirtualKey(VirtualKey));
            return string.Join('+', parts);
        }
    }

    public static HotKeyGesture Normalize(HotKeyGesture? gesture) =>
        gesture is { IsValid: true } ? gesture : Default;

    private static string FormatVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 || virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"Num {virtualKey - 0x60}";
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"Key 0x{virtualKey:X2}"
        };
    }
}

internal enum HotKeyChangeResult
{
    Success,
    Unavailable,
    SaveFailed
}
