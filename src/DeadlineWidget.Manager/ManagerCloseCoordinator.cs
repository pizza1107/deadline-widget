namespace DeadlineWidget.Manager;

public sealed class ManagerCloseCoordinator
{
    private readonly ManagerViewModel _viewModel;
    private readonly Func<Task> _disposeInstanceCoordinator;
    private readonly object _sync = new();
    private Task<bool>? _closeTask;
    private bool _persistenceErrorPresented;

    public ManagerCloseCoordinator(
        ManagerViewModel viewModel,
        Func<Task> disposeInstanceCoordinator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _disposeInstanceCoordinator = disposeInstanceCoordinator
            ?? throw new ArgumentNullException(nameof(disposeInstanceCoordinator));
    }

    public Task<bool> RequestCloseAsync()
    {
        lock (_sync)
        {
            if (_closeTask is null || _closeTask.IsCompleted)
            {
                _closeTask = CloseAsync();
            }

            return _closeTask;
        }
    }

    private async Task<bool> CloseAsync()
    {
        await _viewModel.BeginCloseAsync();
        if (!_persistenceErrorPresented && !string.IsNullOrWhiteSpace(_viewModel.ErrorMessage))
        {
            _persistenceErrorPresented = true;
            return false;
        }

        await _disposeInstanceCoordinator();
        return true;
    }
}
