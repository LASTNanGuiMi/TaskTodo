using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using TaskTodo.Core.Services;

namespace TaskTodo.Infrastructure.Storage;

internal sealed class SerialDatabaseWorker : IAsyncDisposable
{
    private readonly Channel<IWorkItem> _queue = Channel.CreateUnbounded<IWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Func<SqliteConnection> _connectionFactory;
    private readonly Action<SqliteConnection> _initializer;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _stopRequested;
    private int _workerManagedThreadId;

    public SerialDatabaseWorker(
        Func<SqliteConnection> connectionFactory,
        Action<SqliteConnection> initializer)
    {
        _connectionFactory = connectionFactory;
        _initializer = initializer;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "TaskTodo SQLite Worker",
        };
        _thread.Start();
    }

    public Task StartAsync() => _started.Task;

    public int WorkerManagedThreadId => Volatile.Read(ref _workerManagedThreadId);

    public Task<T> Enqueue<T>(Func<SqliteConnection, T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var workItem = new WorkItem<T>(operation);
        if (!_queue.Writer.TryWrite(workItem))
        {
            throw new TaskTodoException(
                TaskTodoErrorCode.StorageStopping,
                "The database worker is stopping and cannot accept new work.");
        }

        // Once accepted, the operation is allowed to finish so callers never see cancellation
        // after an ambiguous local commit. Retried creates reuse their request/task ID.
        return workItem.Completion;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
        => Enqueue(connection => true, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
        {
            _queue.Writer.TryComplete();
        }

        await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
        => await StopAsync().ConfigureAwait(false);

    private void Run()
    {
        Volatile.Write(ref _workerManagedThreadId, Environment.CurrentManagedThreadId);
        SqliteConnection? connection = null;
        try
        {
            connection = _connectionFactory();
            connection.Open();
            _initializer(connection);
            _started.TrySetResult();

            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var workItem))
                {
                    try
                    {
                        workItem.Execute(connection);
                    }
                    catch (Exception exception)
                    {
                        workItem.Fail(exception);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            var mapped = StorageExceptionMapper.Map("initialize the database", exception);
            _started.TrySetException(mapped);
            _queue.Writer.TryComplete(mapped);
            while (_queue.Reader.TryRead(out var workItem))
            {
                workItem.Fail(mapped);
            }
        }
        finally
        {
            connection?.Dispose();
            _stopped.TrySetResult();
        }
    }

    private interface IWorkItem
    {
        void Execute(SqliteConnection connection);

        void Fail(Exception exception);
    }

    private sealed class WorkItem<T>(Func<SqliteConnection, T> operation) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Completion => _completion.Task;

        public void Execute(SqliteConnection connection)
            => _completion.TrySetResult(operation(connection));

        public void Fail(Exception exception)
            => _completion.TrySetException(exception);
    }
}
