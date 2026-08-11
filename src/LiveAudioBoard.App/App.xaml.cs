using System.Windows;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.App.ViewModels;
using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Infrastructure;

namespace LiveAudioBoard.App;

public partial class App : Application
{
    private IAudioPlaybackService? _playbackService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var repository = SqliteAudioLibraryRepository.CreateDefault();
            await repository.InitializeAsync();

            _playbackService = new NaudioPlaybackService();
            var viewModel = new MainViewModel(
                repository,
                _playbackService,
                new NaudioAudioMetadataReader(),
                new WpfAudioFilePicker());

            await viewModel.InitializeAsync();

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"LiveAudioBoard 启动失败。\n\n{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _playbackService?.Dispose();
        base.OnExit(e);
    }
}
