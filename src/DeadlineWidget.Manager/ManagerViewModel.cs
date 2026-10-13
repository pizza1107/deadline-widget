using System.Collections.ObjectModel;
using DeadlineWidget.Core;

namespace DeadlineWidget.Manager;

public sealed class ManagerViewModel : ObservableObject
{
    private readonly IDeadlineStore _store;
    private readonly ISnapshotPublisher _snapshotPublisher;
    private readonly IRainmeterRefresher _rainmeterRefresher;
    private readonly DeadlineService _deadlineService;
    private readonly Func<DateTime> _now;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _mutationStateLock = new();
    private IReadOnlyList<Deadline> _deadlines = [];
    private TaskCompletionSource _idleCompletion = CompletedSource();
    private int _pendingMutationCount;
    private Guid? _editingId;
    private long _editorSessionRevision;
    private bool _isClosing;
    private bool _isEditorOpen;
    private string? _errorMessage;
    private string? _warningMessage;
    private string? _statusMessage;

    public ManagerViewModel(
        IDeadlineStore store,
        ISnapshotPublisher snapshotPublisher,
        IRainmeterRefresher rainmeterRefresher,
        DeadlineService? deadlineService = null,
        Func<DateTime>? now = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _snapshotPublisher = snapshotPublisher ?? throw new ArgumentNullException(nameof(snapshotPublisher));
        _rainmeterRefresher = rainmeterRefresher ?? throw new ArgumentNullException(nameof(rainmeterRefresher));
        _deadlineService = deadlineService ?? new DeadlineService();
        _now = now ?? (() => DateTime.Now);

        AddCommand = new RelayCommand(_ => BeginAdd(), _ => !IsClosing);
        EditCommand = new RelayCommand(
            parameter => BeginEdit(parameter as Deadline),
            parameter => !IsClosing && parameter is Deadline);
        CompleteCommand = new AsyncRelayCommand(
            parameter => CompleteAsync(parameter as Deadline),
            parameter => !IsClosing && parameter is Deadline);
        RestoreCommand = new AsyncRelayCommand(
            parameter => RestoreAsync(parameter as Deadline),
            parameter => !IsClosing && parameter is Deadline);
        DeleteCommand = new AsyncRelayCommand(
            parameter => DeleteAsync(parameter as Deadline),
            parameter => !IsClosing && parameter is Deadline);
        SaveCommand = new AsyncRelayCommand(_ => SaveEditorAsync(), _ => !IsClosing && IsEditorOpen);
        CancelCommand = new RelayCommand(_ => CancelEditor(), _ => !IsClosing && IsEditorOpen);
    }

    public ObservableCollection<Deadline> ActiveDeadlines { get; } = [];
    public ObservableCollection<Deadline> CompletedDeadlines { get; } = [];
    public EditorViewModel Editor { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public AsyncRelayCommand CompleteCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set
        {
            if (SetProperty(ref _isEditorOpen, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string? WarningMessage
    {
        get => _warningMessage;
        private set => SetProperty(ref _warningMessage, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsClosing
    {
        get => _isClosing;
        private set => SetProperty(ref _isClosing, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _deadlines = await _store.LoadAsync(cancellationToken);
        RefreshCollections();

        try
        {
            await _snapshotPublisher.PublishAsync(_deadlines, _now(), cancellationToken);
        }
        catch (Exception exception)
        {
            WarningMessage = $"显示快照发布失败：{exception.Message}";
            return;
        }

        var refresh = await _rainmeterRefresher.RefreshAsync(cancellationToken);
        WarningMessage = refresh.Warning;
    }

    public Task BeginCloseAsync()
    {
        Task idle;
        lock (_mutationStateLock)
        {
            IsClosing = true;
            idle = _idleCompletion.Task;
        }

        NotifyCommandStatesChanged();
        return idle;
    }

    private void BeginAdd()
    {
        ClearMessages();
        _editingId = null;
        _editorSessionRevision++;
        Editor.BeginNew(_now().Date);
        IsEditorOpen = true;
    }

    private void BeginEdit(Deadline? deadline)
    {
        if (deadline is null || !_deadlines.Any(item => item.Id == deadline.Id))
        {
            return;
        }

        ClearMessages();
        _editingId = deadline.Id;
        _editorSessionRevision++;
        Editor.BeginEdit(deadline.Title, deadline.DueAt);
        IsEditorOpen = true;
    }

    private void CancelEditor()
    {
        _editingId = null;
        _editorSessionRevision++;
        IsEditorOpen = false;
        Editor.BeginNew(_now().Date);
    }

    private async Task SaveEditorAsync()
    {
        ClearMessages();
        if (!Editor.TryBuild(out var title, out var dueAt))
        {
            ErrorMessage = Editor.ValidationMessage;
            return;
        }

        var editingId = _editingId;
        var editorSessionRevision = _editorSessionRevision;
        await PersistMutationAsync((current, now) =>
        {
            if (editingId is not Guid id)
            {
                return current.Append(_deadlineService.Create(title, dueAt, now)).ToArray();
            }

            var existing = current.SingleOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return null;
            }

            var updated = _deadlineService.Update(existing, title, dueAt, now);
            return current.Select(item => item.Id == id ? updated : item).ToArray();
        }, editorSessionRevision);
    }

    private Task CompleteAsync(Deadline? deadline)
    {
        if (deadline is null)
        {
            return Task.CompletedTask;
        }

        return PersistMutationAsync((current, now) =>
        {
            var existing = current.SingleOrDefault(item => item.Id == deadline.Id);
            if (existing is null)
            {
                return null;
            }

            var updated = _deadlineService.Complete(existing, now);
            return current.Select(item => item.Id == deadline.Id ? updated : item).ToArray();
        });
    }

    private Task RestoreAsync(Deadline? deadline)
    {
        if (deadline is null)
        {
            return Task.CompletedTask;
        }

        return PersistMutationAsync((current, now) =>
        {
            var existing = current.SingleOrDefault(item => item.Id == deadline.Id);
            if (existing is null)
            {
                return null;
            }

            var updated = _deadlineService.Restore(existing, now);
            return current.Select(item => item.Id == deadline.Id ? updated : item).ToArray();
        });
    }

    private Task DeleteAsync(Deadline? deadline) => deadline is null
        ? Task.CompletedTask
        : PersistMutationAsync((current, _) => current.Any(item => item.Id == deadline.Id)
            ? _deadlineService.Delete(current, deadline.Id)
            : null);

    private async Task PersistMutationAsync(
        Func<IReadOnlyList<Deadline>, DateTime, IReadOnlyList<Deadline>?> buildCandidate,
        long? editorSessionToClose = null)
    {
        if (!TryBeginMutation())
        {
            return;
        }

        try
        {
            await _mutationGate.WaitAsync();
            try
            {
                ClearMessages();
                var candidate = buildCandidate(_deadlines, _now());
                if (candidate is null)
                {
                    ErrorMessage = "要修改的截止日期已不存在。";
                    return;
                }

                try
                {
                    await _store.SaveAsync(candidate);
                }
                catch (Exception exception)
                {
                    ErrorMessage = $"保存失败：{exception.Message}";
                    return;
                }

                _deadlines = candidate;
                RefreshCollections();
                if (editorSessionToClose is long revision && revision == _editorSessionRevision)
                {
                    _editingId = null;
                    _editorSessionRevision++;
                    IsEditorOpen = false;
                }

                try
                {
                    await _snapshotPublisher.PublishAsync(candidate, _now());
                }
                catch (Exception exception)
                {
                    ErrorMessage = $"显示快照发布失败：{exception.Message}";
                    return;
                }

                var refresh = await _rainmeterRefresher.RefreshAsync();
                WarningMessage = refresh.Warning;
                StatusMessage = "已保存";
            }
            finally
            {
                _mutationGate.Release();
            }
        }
        finally
        {
            EndMutation();
        }
    }

    private void RefreshCollections()
    {
        ActiveDeadlines.Clear();
        foreach (var deadline in _deadlineService.GetActiveOrdered(_deadlines, _now()))
        {
            ActiveDeadlines.Add(deadline);
        }

        CompletedDeadlines.Clear();
        foreach (var deadline in _deadlineService.GetCompletedOrdered(_deadlines))
        {
            CompletedDeadlines.Add(deadline);
        }
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = null;
    }

    private bool TryBeginMutation()
    {
        lock (_mutationStateLock)
        {
            if (IsClosing)
            {
                return false;
            }

            if (_pendingMutationCount == 0)
            {
                _idleCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingMutationCount++;
            return true;
        }
    }

    private void EndMutation()
    {
        TaskCompletionSource? completion = null;
        lock (_mutationStateLock)
        {
            _pendingMutationCount--;
            if (_pendingMutationCount == 0)
            {
                completion = _idleCompletion;
            }
        }

        completion?.TrySetResult();
    }

    private void NotifyCommandStatesChanged()
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        CompleteCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
