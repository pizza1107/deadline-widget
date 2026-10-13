using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DeadlineWidget.Manager;

public partial class MainWindow : Window
{
    private readonly WindowCloseController _closeController;

    public MainWindow(
        ManagerViewModel viewModel,
        ManagerCloseCoordinator closeCoordinator,
        Action requestApplicationExit)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(closeCoordinator);
        ArgumentNullException.ThrowIfNull(requestApplicationExit);
        _closeController = new WindowCloseController(Hide);
        Closing += OnClosing;
    }

    public void AuthorizeApplicationExit() => _closeController.AuthorizeApplicationExit();

    public void RestoreAndActivate()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _closeController.HandleClosing(e);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
