using System.IO.Pipes;
using System.IO;
using System.Text;

namespace DeadlineWidget.Manager;

public interface ISingleInstancePlatform
{
    bool TryAcquireMutex(string name);
    Task ListenAsync(string pipeName, Func<string, Task> onMessage, CancellationToken cancellationToken);
    Task SendAsync(string pipeName, string message, CancellationToken cancellationToken);
    void ReleaseMutex();
}

public sealed class SingleInstanceCoordinator : IDisposable, IAsyncDisposable
{
    public const string MutexName = "Local\\DeadlineWidgetV2.Manager";
    public const string PipeName = "DeadlineWidgetV2.Activate";
    public const string ActivationMessage = "ACTIVATE";

    private readonly ISingleInstancePlatform _platform;
    private readonly Func<Task> _activate;
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(ISingleInstancePlatform platform, Func<Task> activate)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listenerTask is not null || _ownsMutex)
        {
            throw new InvalidOperationException("The coordinator has already been started.");
        }

        _ownsMutex = _platform.TryAcquireMutex(MutexName);
        if (!_ownsMutex)
        {
            await _platform.SendAsync(PipeName, ActivationMessage, cancellationToken);
            return false;
        }

        _listenerCancellation = new CancellationTokenSource();
        _listenerTask = _platform.ListenAsync(PipeName, OnMessageAsync, _listenerCancellation.Token);
        return true;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation?.Cancel();
        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception) when (_listenerCancellation?.IsCancellationRequested == true)
            {
            }
        }

        _listenerCancellation?.Dispose();
        if (_ownsMutex)
        {
            _platform.ReleaseMutex();
            _ownsMutex = false;
        }

        GC.SuppressFinalize(this);
    }

    private Task OnMessageAsync(string message)
    {
        if (!string.Equals(message, ActivationMessage, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        try
        {
            var activation = _activate();
            if (!activation.IsCompletedSuccessfully)
            {
                _ = ObserveActivationAsync(activation);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private static async Task ObserveActivationAsync(Task activation)
    {
        try
        {
            await activation.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

public sealed class WindowsSingleInstancePlatform : ISingleInstancePlatform
{
    private Mutex? _mutex;

    public bool TryAcquireMutex(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    public async Task ListenAsync(
        string pipeName,
        Func<string, Task> onMessage,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var message = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await onMessage(message).ConfigureAwait(false);
        }
    }

    public async Task SendAsync(string pipeName, string message, CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(message);
        await client.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await client.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ReleaseMutex()
    {
        if (_mutex is null)
        {
            return;
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
