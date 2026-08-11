using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.App.ViewModels;

namespace LiveAudioBoard.App;

public partial class MainWindow : Window
{
    private const int EmergencyStopHotkeyId = 0x4C41;

    private WindowsGlobalHotkeyService? _globalHotkeyService;

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

        if (DataContext is not MainViewModel viewModel || !viewModel.EnableEmergencyStopHotkey)
        {
            return;
        }

        _globalHotkeyService = new WindowsGlobalHotkeyService(new WindowInteropHelper(this).Handle);
        var registered = _globalHotkeyService.TryRegister(
            EmergencyStopHotkeyId,
            GlobalHotkeyDefinition.EmergencyStop,
            () => viewModel.StopAllCommand.Execute(null),
            out var errorCode);

        viewModel.SetEmergencyHotkeyRegistration(registered, errorCode);
    }

    protected override void OnClosed(EventArgs e)
    {
        StateChanged -= OnWindowStateChanged;
        _globalHotkeyService?.Dispose();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
