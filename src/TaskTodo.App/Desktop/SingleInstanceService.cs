using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace TaskTodo.App.Desktop;

internal sealed class SingleInstanceService : IAsyncDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly Action _activationRequested;
    private readonly CancellationTokenSource _stopping = new();

    private SingleInstanceService(Mutex mutex, string pipeName, Action activationRequested)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _activationRequested = activationRequested;
        Completion = ListenAsync(CreateServer());
    }

    public Task Completion { get; }

    public static async Task<SingleInstanceService?> StartAsync(
        string dataDirectory, Action activationRequested, CancellationToken cancellationToken = default)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        using var identity = WindowsIdentity.GetCurrent();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity.User!.Value}\n{directory}")));
        var name = $"TaskTodo.{key}";
        var mutex = new Mutex(false, name,
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out var created);
        try
        {
            // Keeping the created kernel object alive elects one process, without thread-affine ownership.
            if (created) return new SingleInstanceService(mutex, name, activationRequested);
            mutex.Dispose(); // A secondary must not keep the primary's election object alive.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new NamedPipeClientStream(".", name, PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                var acknowledgement = new byte[1];
                await client.ReadExactlyAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("已有 TaskTodo 实例未响应，请稍后重试。");
            }
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private NamedPipeServerStream CreateServer()
        => new(_pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                while (true)
                {
                    await server.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                    _activationRequested();
                    try
                    {
                        await server.WriteAsync(new byte[] { 1 }, _stopping.Token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // A secondary can close while activation is queued; the listener remains available.
                    }
                    server.Disconnect();
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        // Completion is the listener's error-reporting channel; shutdown must not report the same failure again.
        await Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _mutex.Dispose();
        _stopping.Dispose();
    }
}
