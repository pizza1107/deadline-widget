using System.Windows;
using DeadlineWidget.Core;

namespace DeadlineWidget.Manager;

public partial class App : Application
{
    private readonly Action _requestApplicationExit;
    private SingleInstanceCoordinator? _coordinator;
    private MainWindow? _mainWindow;
    private WakeupFileWatcher? _wakeupWatcher;
    private bool _activationPending;

    public App() : this(() => Environment.Exit(0))
    {
    }

    internal App(Action requestApplicationExit)
    {
        _requestApplicationExit = requestApplicationExit
            ?? throw new ArgumentNullException(nameof(requestApplicationExit));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _coordinator = new SingleInstanceCoordinator(
            new WindowsSingleInstancePlatform(),
            ActivateMainWindowAsync);

        try
        {
            if (!await _coordinator.StartAsync())
            {
                await _coordinator.DisposeAsync();
                Shutdown();
                return;
            }

            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var paths = ManagerPaths.FromAppDataRoots(
                localApplicationData,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            _wakeupWatcher = new WakeupFileWatcher(paths.WakeupFilePath, ActivateMainWindowAsync);
            await new LegacyDeadlineImporter(localApplicationData).ImportOnceAsync();

            var viewModel = new ManagerViewModel(
                new DeadlineRepositoryStore(new DeadlineRepository(paths.RepositoryDirectory)),
                new SnapshotPublisher(paths.SnapshotDirectory),
                new RainmeterRefresher());
            await viewModel.InitializeAsync();

            var closeCoordinator = new ManagerCloseCoordinator(viewModel, DisposeCoordinatorAsync);
            _mainWindow = new MainWindow(viewModel, closeCoordinator, _requestApplicationExit);
            MainWindow = _mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.Show();
            }
            if (_activationPending)
            {
                _activationPending = false;
                _mainWindow.RestoreAndActivate();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"截止日期管理器无法启动：{exception.Message}",
                "DeadlineWidget V2",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await DisposeCoordinatorAsync();
            Shutdown();
        }
    }

    private Task ActivateMainWindowAsync() => Dispatcher.InvokeAsync(() =>
    {
        if (_mainWindow is null)
        {
            _activationPending = true;
        }
        else
        {
            _mainWindow.RestoreAndActivate();
        }
    }).Task;

    private async Task DisposeCoordinatorAsync()
    {
        var coordinator = _coordinator;
        _coordinator = null;
        if (coordinator is not null)
        {
            await coordinator.DisposeAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wakeupWatcher?.Dispose();
        _wakeupWatcher = null;
        base.OnExit(e);
    }
}
