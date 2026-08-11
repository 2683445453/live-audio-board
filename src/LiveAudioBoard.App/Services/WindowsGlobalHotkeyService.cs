using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LiveAudioBoard.App.Services;

internal sealed class WindowsGlobalHotkeyService : IDisposable
{
    private const int WindowMessageHotkey = 0x0312;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = [];
    private bool _disposed;

    public WindowsGlobalHotkeyService(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法连接到窗口消息循环。");
        _source.AddHook(WindowMessageHook);
    }

    public bool TryRegister(
        int id,
        GlobalHotkeyDefinition hotkey,
        Action callback,
        out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_callbacks.ContainsKey(id))
        {
            throw new InvalidOperationException($"热键编号 {id} 已注册。");
        }

        if (!RegisterHotKey(_windowHandle, id, (uint)hotkey.Modifiers, hotkey.VirtualKey))
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        _callbacks.Add(id, callback);
        errorCode = 0;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _callbacks.Keys)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _callbacks.Clear();
        _source.RemoveHook(WindowMessageHook);
        _disposed = true;
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WindowMessageHotkey)
        {
            return 0;
        }

        var id = wParam.ToInt32();
        if (!_callbacks.TryGetValue(id, out var callback))
        {
            return 0;
        }

        callback();
        handled = true;
        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
