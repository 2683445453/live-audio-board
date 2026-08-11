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

    public static GlobalHotkeyDefinition EmergencyStop { get; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
        VirtualKeyF10,
        "Ctrl+Shift+F10");
}
