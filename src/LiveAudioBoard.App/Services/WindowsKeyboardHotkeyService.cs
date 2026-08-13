using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LiveAudioBoard.App.Services;

internal sealed class WindowsKeyboardHotkeyService : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyMenu = 0x12;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;

    private readonly Dispatcher _dispatcher;
    private readonly LowLevelKeyboardProcedure _hookProcedure;
    private readonly Dictionary<int, Registration> _registrations = [];
    private readonly HashSet<uint> _activeTriggerKeys = [];
    private readonly HashSet<uint> _suppressedTriggerKeys = [];
    private nint _hookHandle;
    private bool _disposed;

    public WindowsKeyboardHotkeyService(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _hookProcedure = HookCallback;
        _hookHandle = SetWindowsHookEx(
            LowLevelKeyboardHook,
            _hookProcedure,
            GetModuleHandle(null),
            0);
        if (_hookHandle == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "无法启用 Windows 全局键盘监听。");
        }
    }

    public bool PassThrough { get; set; }

    public bool TryRegister(
        int id,
        GlobalHotkeyDefinition hotkey,
        Action callback,
        out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);

        if (_registrations.ContainsKey(id))
        {
            throw new InvalidOperationException($"热键编号 {id} 已注册。");
        }

        if (_registrations.Values.Any(existing => existing.Hotkey.ConflictsWith(hotkey)))
        {
            errorCode = 1409;
            return false;
        }

        _registrations.Add(id, new Registration(hotkey, callback));
        errorCode = 0;
        return true;
    }

    public bool Unregister(int id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _registrations.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookHandle != 0)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }

        _registrations.Clear();
        _activeTriggerKeys.Clear();
        _suppressedTriggerKeys.Clear();
        _disposed = true;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code < 0 || _disposed)
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var messageId = message.ToInt32();
        if (messageId is not (KeyDownMessage or KeyUpMessage or
            SystemKeyDownMessage or SystemKeyUpMessage))
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var keyboard = Marshal.PtrToStructure<LowLevelKeyboardData>(data);
        if (keyboard.Flags.HasFlag(LowLevelKeyboardFlags.Injected))
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var isKeyDown = messageId is KeyDownMessage or SystemKeyDownMessage;
        if (!isKeyDown)
        {
            _activeTriggerKeys.Remove(keyboard.VirtualKey);
            if (_suppressedTriggerKeys.Remove(keyboard.VirtualKey))
            {
                return 1;
            }

            return CallNextHookEx(_hookHandle, code, message, data);
        }

        if (_activeTriggerKeys.Contains(keyboard.VirtualKey))
        {
            return _suppressedTriggerKeys.Contains(keyboard.VirtualKey)
                ? 1
                : CallNextHookEx(_hookHandle, code, message, data);
        }

        var activeModifiers = GetActiveModifiers();
        var registration = _registrations.Values.FirstOrDefault(item =>
            item.Hotkey.Matches(keyboard.VirtualKey, activeModifiers));
        if (registration is null)
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        _activeTriggerKeys.Add(keyboard.VirtualKey);
        _dispatcher.BeginInvoke(registration.Callback, DispatcherPriority.Input);
        if (PassThrough)
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        _suppressedTriggerKeys.Add(keyboard.VirtualKey);
        return 1;
    }

    private static HotkeyModifiers GetActiveModifiers()
    {
        var modifiers = (HotkeyModifiers)0;
        if (IsKeyDown(VirtualKeyControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsKeyDown(VirtualKeyMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(VirtualKeyShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(VirtualKeyLeftWindows) || IsKeyDown(VirtualKeyRightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private sealed record Registration(
        GlobalHotkeyDefinition Hotkey,
        Action Callback);

    [Flags]
    private enum LowLevelKeyboardFlags : uint
    {
        Injected = 0x10
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public LowLevelKeyboardFlags Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private delegate nint LowLevelKeyboardProcedure(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProcedure hookProcedure,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint message,
        nint data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
