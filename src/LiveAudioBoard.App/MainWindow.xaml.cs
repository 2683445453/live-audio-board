using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.App.ViewModels;

namespace LiveAudioBoard.App;

public partial class MainWindow : Window
{
    private const int EmergencyStopHotkeyId = 0x4C41;
    private const int FirstSoundHotkeyId = 0x5200;
    private const int MaximumHotkeyId = 0xBFFF;

    private WindowsGlobalHotkeyService? _globalHotkeyService;
    private readonly List<int> _soundHotkeyIds = [];

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnWindowStateChanged;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _globalHotkeyService = new WindowsGlobalHotkeyService(new WindowInteropHelper(this).Handle);
        if (viewModel.EnableEmergencyStopHotkey)
        {
            var registered = _globalHotkeyService.TryRegister(
                EmergencyStopHotkeyId,
                GlobalHotkeyDefinition.EmergencyStop,
                () => viewModel.StopAllCommand.Execute(null),
                out var errorCode);

            viewModel.SetEmergencyHotkeyRegistration(registered, errorCode);
        }

        viewModel.HotkeyBindingsChanged += OnHotkeyBindingsChanged;
        RefreshSoundHotkeys(viewModel);
    }

    private void OnHotkeyBindingsChanged(object? sender, EventArgs e)
    {
        if (sender is MainViewModel viewModel)
        {
            RefreshSoundHotkeys(viewModel);
        }
    }

    private void RefreshSoundHotkeys(MainViewModel viewModel)
    {
        if (_globalHotkeyService is null)
        {
            return;
        }

        foreach (var id in _soundHotkeyIds)
        {
            _globalHotkeyService.Unregister(id);
        }

        _soundHotkeyIds.Clear();
        if (!viewModel.AreSoundHotkeysEnabled)
        {
            viewModel.SetSoundHotkeyRegistrationSummary(0, []);
            return;
        }

        var failures = new List<string>();
        var registeredCount = 0;
        var nextId = FirstSoundHotkeyId;

        foreach (var clip in viewModel.Clips)
        {
            if (!GlobalHotkeyDefinition.TryParse(
                    clip.Model.Hotkey,
                    out var definition,
                    out _))
            {
                continue;
            }

            if (nextId > MaximumHotkeyId)
            {
                failures.Add($"{clip.Title}（数量超限）");
                continue;
            }

            var id = nextId++;
            if (_globalHotkeyService.TryRegister(
                    id,
                    definition,
                    () => viewModel.PlayCommand.Execute(clip),
                    out _))
            {
                _soundHotkeyIds.Add(id);
                registeredCount++;
            }
            else
            {
                failures.Add($"{definition.DisplayName}（{clip.Title}）");
            }
        }

        viewModel.SetSoundHotkeyRegistrationSummary(registeredCount, failures);
    }

    private void HotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            viewModel.ReportHotkeyCaptureError("继续按下一个字母、数字、功能键或小键盘键。");
            return;
        }

        if (GlobalHotkeyDefinition.TryCreate(
                key,
                Keyboard.Modifiers,
                out var definition,
                out var error))
        {
            viewModel.CaptureHotkey(definition);
        }
        else
        {
            viewModel.ReportHotkeyCaptureError(error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StateChanged -= OnWindowStateChanged;
        _globalHotkeyService?.Dispose();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.HotkeyBindingsChanged -= OnHotkeyBindingsChanged;
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
