using System.Text.Json;
using System.Text.Json.Serialization;
using TaskTodo.Core.Models;
using TaskTodo.Core.Services;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Backup;

public sealed class JsonDataTransferService(TaskTodoService service)
{
    public const long MaximumImportBytes = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        var archive = await service.GetArchiveAsync(cancellationToken).ConfigureAwait(false);
        await Task.Run(() => StorageExceptionMapper.Execute("export JSON", () => WriteArchive(path, archive)),
            cancellationToken).ConfigureAwait(false);
    }

    private static void WriteArchive(string path, TaskTodoArchive archive)
    {
        // Complete serialization before opening the user's destination; replace only a complete file.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(archive, Options);
        var temporaryPath = Path.GetFullPath(path) + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<TaskTodoImportPreview> PreviewImportAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is 0 or > MaximumImportBytes)
            {
                throw new TaskTodoException(TaskTodoErrorCode.Validation,
                    "Import files must contain between 1 byte and 32 MiB.");
            }

            var archive = await JsonSerializer.DeserializeAsync<TaskTodoArchive>(file, Options, cancellationToken)
                .ConfigureAwait(false);
            return new TaskTodoImportPreview(ArchiveRules.ValidateAndCopy(archive!));
        }
        catch (JsonException exception)
        {
            throw new TaskTodoException(TaskTodoErrorCode.Validation,
                "The import file has invalid JSON or missing fields.", innerException: exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw StorageExceptionMapper.Map("read the import file", exception);
        }
    }

    public Task<string> ImportAsync(TaskTodoImportPreview preview, CancellationToken cancellationToken = default)
        => service.ReplaceDataAsync(preview.Archive, cancellationToken);
}
