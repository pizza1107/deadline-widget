using System.ComponentModel;

namespace DeadlineWidget.Manager;

public sealed class WindowCloseController
{
    private readonly Action _hideWindow;
    private bool _applicationExitAuthorized;

    public WindowCloseController(Action hideWindow)
    {
        _hideWindow = hideWindow ?? throw new ArgumentNullException(nameof(hideWindow));
    }

    public void HandleClosing(CancelEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        if (_applicationExitAuthorized)
        {
            return;
        }

        eventArgs.Cancel = true;
        _hideWindow();
    }

    public void AuthorizeApplicationExit() => _applicationExitAuthorized = true;
}
