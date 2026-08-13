using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.App.ViewModels;

namespace LiveAudioBoard.App;

public partial class MainWindow : Window
{
    private const string AudioClipDragFormat = "LiveAudioBoard.AudioClipId";
    private const int EmergencyStopHotkeyId = 0x4C41;
    private const int FirstSoundHotkeyId = 0x5200;
    private const int MaximumHotkeyId = 0xBFFF;

    private WindowsGlobalHotkeyService? _globalHotkeyService;
    private WindowsKeyboardHotkeyService? _soundHotkeyService;
    private readonly List<int> _soundHotkeyIds = [];
    private Point _audioCardDragStart;
    private AudioClipViewModel? _audioCardDragSource;

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

    private void FreesoundClientSecretBox_PasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.DownloadCenter.FreesoundClientSecret = passwordBox.Password;
        }
    }

    private void BeginFreesoundAuthorizationButton_Click(
        object sender,
        RoutedEventArgs e) =>
        FreesoundClientSecretBox.Clear();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel viewModel ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            return;
        }

        await viewModel.ImportDroppedPathsAsync(paths);
    }

    private void AudioCard_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _audioCardDragStart = e.GetPosition(this);
        _audioCardDragSource = (sender as FrameworkElement)?.DataContext as AudioClipViewModel;
    }

    private void AudioCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _audioCardDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _audioCardDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _audioCardDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(AudioClipDragFormat, _audioCardDragSource.Model.Id.ToString("D"));
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        _audioCardDragSource = null;
    }

    private void AudioCard_DragOver(object sender, DragEventArgs e) =>
        SetAudioClipDragEffect(e);

    private async void AudioCard_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel viewModel ||
            (sender as FrameworkElement)?.DataContext is not AudioClipViewModel target ||
            !TryGetDraggedClipId(e, out var sourceId))
        {
            return;
        }

        await viewModel.MoveClipBeforeAsync(sourceId, target.Model.Id);
    }

    private void Category_DragOver(object sender, DragEventArgs e) =>
        SetAudioClipDragEffect(e);

    private async void Category_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel viewModel ||
            (sender as FrameworkElement)?.DataContext is not string category ||
            !TryGetDraggedClipId(e, out var clipId))
        {
            return;
        }

        await viewModel.MoveClipToCategoryAsync(clipId, category);
    }

    private static void SetAudioClipDragEffect(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(AudioClipDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetDraggedClipId(DragEventArgs e, out Guid clipId)
    {
        clipId = Guid.Empty;
        return e.Data.GetDataPresent(AudioClipDragFormat) &&
               Guid.TryParse(e.Data.GetData(AudioClipDragFormat) as string, out clipId);
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
        try
        {
            _soundHotkeyService = new WindowsKeyboardHotkeyService(Dispatcher);
        }
        catch (Exception exception)
        {
            viewModel.SetSoundHotkeyRegistrationSummary(
                0,
                [$"Windows 键盘监听不可用（{exception.Message}）"]);
        }
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
        if (_soundHotkeyService is null)
        {
            return;
        }

        foreach (var id in _soundHotkeyIds)
        {
            _soundHotkeyService.Unregister(id);
        }

        _soundHotkeyIds.Clear();
        _soundHotkeyService.PassThrough = viewModel.PassSoundHotkeysToForeground;
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
            if (!clip.Model.HotkeyEnabled)
            {
                continue;
            }

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
            if (_soundHotkeyService.TryRegister(
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
        _soundHotkeyService?.Dispose();
        _globalHotkeyService?.Dispose();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.HotkeyBindingsChanged -= OnHotkeyBindingsChanged;
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
