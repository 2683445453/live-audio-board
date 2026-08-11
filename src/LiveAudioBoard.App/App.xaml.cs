using System.Windows;
using System.Windows.Threading;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.App.ViewModels;
using LiveAudioBoard.Audio;
using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Infrastructure;
using LiveAudioBoard.Providers;
using Velopack;

namespace LiveAudioBoard.App;

public partial class App : Application
{
    private readonly CrashLogWriter _crashLogWriter = CrashLogWriter.CreateDefault();
    private IAudioPlaybackService? _playbackService;
    private int _isHandlingFatalError;

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterUnhandledExceptionHandlers();
        base.OnStartup(e);

        try
        {
            var repository = SqliteAudioLibraryRepository.CreateDefault();
            await repository.InitializeAsync();
            LibraryBackupResult? backupResult = null;
            var backupFailed = false;
            try
            {
                backupResult = await SqliteLibraryBackupService
                    .CreateDefault(repository.DatabasePath)
                    .CreateBackupIfDueAsync(TimeSpan.FromHours(24));
            }
            catch
            {
                backupFailed = true;
            }

            _playbackService = new NaudioPlaybackService();
            var freesoundApiService = new FreesoundApiService(
                DpapiFreesoundCredentialStore.CreateDefault());
            var providerCatalog = new ProviderCatalog(
                [
                    new FreesoundOriginalDownloadProvider(freesoundApiService),
                    new DirectHttpDownloadProvider()
                ]);
            var audioSearchProvider = new CompositeAudioSearchProvider(
            [
                new OpenverseAudioSearchProvider(),
                new InternetArchiveAudioSearchProvider()
            ]);
            var viewModel = new MainViewModel(
                repository,
                _playbackService,
                new NaudioRecordingService(),
                new NaudioAudioClipRenderer(),
                new NaudioAudioMetadataReader(),
                new WpfAudioFilePicker(),
                JsonAppSettingsStore.CreateDefault(),
                Sha256LibraryMediaStore.CreateDefault(),
                new EbuR128LoudnessAnalyzer(),
                providerCatalog,
                audioSearchProvider,
                new RssAudioFeedProvider(),
                freesoundApiService,
                new VelopackAppUpdateService());

            await viewModel.InitializeAsync();
            if (backupResult?.Created == true)
            {
                viewModel.StatusText += " · 已创建自动备份";
            }
            else if (backupFailed)
            {
                viewModel.StatusText += " · 自动备份失败，资料库仍可使用";
            }

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            var logPath = _crashLogWriter.TryWrite(exception, "Application startup");
            MessageBox.Show(
                $"LiveAudioBoard 启动失败。\n\n{exception.Message}" +
                FormatLogLocation(logPath),
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UnregisterUnhandledExceptionHandlers();
        _playbackService?.Dispose();
        base.OnExit(e);
    }

    private void RegisterUnhandledExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void UnregisterUnhandledExceptionHandlers()
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var logPath = _crashLogWriter.TryWrite(e.Exception, "WPF dispatcher");
        if (Interlocked.Exchange(ref _isHandlingFatalError, 1) != 0)
        {
            Shutdown(1);
            return;
        }

        try
        {
            MessageBox.Show(
                "LiveAudioBoard 遇到无法恢复的界面错误，将安全退出。" +
                FormatLogLocation(logPath),
                "程序异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(1);
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException(
                            $"Unhandled non-exception object: {e.ExceptionObject}");
        _crashLogWriter.TryWrite(exception, "AppDomain");
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        _crashLogWriter.TryWrite(e.Exception, "Unobserved task");
        e.SetObserved();
    }

    private static string FormatLogLocation(string? logPath) =>
        string.IsNullOrWhiteSpace(logPath)
            ? "\n\n崩溃日志写入失败。"
            : $"\n\n诊断日志：{logPath}";
}
