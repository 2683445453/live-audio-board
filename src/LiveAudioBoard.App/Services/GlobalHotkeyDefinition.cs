using System.Windows.Input;

namespace LiveAudioBoard.App.Services;

[Flags]
internal enum HotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

internal readonly record struct GlobalHotkeyDefinition(
    HotkeyModifiers Modifiers,
    uint VirtualKey,
    string DisplayName)
{
    private const uint VirtualKeyF10 = 0x79;
    private const HotkeyModifiers ModifierMask =
        HotkeyModifiers.Control |
        HotkeyModifiers.Alt |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Windows;

    public static GlobalHotkeyDefinition EmergencyStop { get; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        VirtualKeyF10,
        "Ctrl+Shift+F10");

    public bool ConflictsWith(GlobalHotkeyDefinition other) =>
        VirtualKey == other.VirtualKey &&
        (Modifiers & ~HotkeyModifiers.NoRepeat) ==
        (other.Modifiers & ~HotkeyModifiers.NoRepeat);

    public bool Matches(uint virtualKey, HotkeyModifiers activeModifiers) =>
        VirtualKey == virtualKey &&
        (Modifiers & ModifierMask) == (activeModifiers & ModifierMask);

    public static bool TryParse(
        string? text,
        out GlobalHotkeyDefinition definition,
        out string error)
    {
        definition = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "请先录入快捷键。";
            return false;
        }

        var parts = text
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "快捷键格式无效。";
            return false;
        }

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    error = $"无法识别修饰键“{parts[index]}”。";
                    return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key))
        {
            error = $"无法识别按键“{parts[^1]}”。";
            return false;
        }

        return TryCreate(key, modifiers, out definition, out error);
    }

    public static bool TryCreate(
        Key key,
        ModifierKeys modifiers,
        out GlobalHotkeyDefinition definition,
        out string error)
    {
        definition = default;
        if (IsModifierKey(key) || key == Key.None)
        {
            error = "请同时按下一个非修饰键。";
            return false;
        }

        var normalizedModifiers = modifiers &
                                  (ModifierKeys.Control |
                                   ModifierKeys.Alt |
                                   ModifierKeys.Shift |
                                   ModifierKeys.Windows);
        if (normalizedModifiers == ModifierKeys.None && !CanUseWithoutModifier(key))
        {
            error = "字母和数字快捷键至少需要 Ctrl、Alt、Shift 或 Win 中的一个。";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = "该按键不能注册为 Windows 全局快捷键。";
            return false;
        }

        var hotkeyModifiers = HotkeyModifiers.NoRepeat;
        if (normalizedModifiers.HasFlag(ModifierKeys.Control))
        {
            hotkeyModifiers |= HotkeyModifiers.Control;
        }

        if (normalizedModifiers.HasFlag(ModifierKeys.Alt))
        {
            hotkeyModifiers |= HotkeyModifiers.Alt;
        }

        if (normalizedModifiers.HasFlag(ModifierKeys.Shift))
        {
            hotkeyModifiers |= HotkeyModifiers.Shift;
        }

        if (normalizedModifiers.HasFlag(ModifierKeys.Windows))
        {
            hotkeyModifiers |= HotkeyModifiers.Windows;
        }

        definition = new GlobalHotkeyDefinition(
            hotkeyModifiers,
            virtualKey,
            BuildDisplayName(key, normalizedModifiers));
        error = string.Empty;
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        var normalized = text.Trim();
        if (normalized.Length == 1 && char.IsLetter(normalized[0]))
        {
            return Enum.TryParse(normalized.ToUpperInvariant(), out key);
        }

        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
        {
            return Enum.TryParse($"D{normalized}", out key);
        }

        if (normalized.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            normalized.Length == 4 &&
            char.IsDigit(normalized[3]))
        {
            return Enum.TryParse($"NumPad{normalized[3]}", out key);
        }

        normalized = normalized.ToLowerInvariant() switch
        {
            "esc" => nameof(Key.Escape),
            "enter" => nameof(Key.Return),
            "pgup" or "pageup" => nameof(Key.Prior),
            "pgdn" or "pagedown" => nameof(Key.Next),
            _ => normalized
        };

        return Enum.TryParse(normalized, true, out key);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or Key.System;

    private static bool CanUseWithoutModifier(Key key) =>
        key is >= Key.F1 and <= Key.F24 ||
        key is >= Key.NumPad0 and <= Key.NumPad9;

    private static string BuildDisplayName(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyDisplayName(key));
        return string.Join('+', parts);
    }

    private static string GetKeyDisplayName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num{(int)key - (int)Key.NumPad0}";
        }

        return key switch
        {
            Key.Escape => "Esc",
            Key.Return => "Enter",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            _ => key.ToString()
        };
    }
}
