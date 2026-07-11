using System.Text.Json;
using System.Text.Json.Serialization;

namespace lizi_typeless.Core.Sessions;

public sealed class SessionStore
{
    private const string MetadataFileName = "session.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SessionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    public async Task<SessionRecord> CreateAsync(
        TargetWindowInfo targetWindow,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var id = $"{timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var session = new SessionRecord
        {
            Id = id,
            StartedAt = timestamp,
            UpdatedAt = timestamp,
            Status = SessionStatus.Recording,
            TargetWindow = targetWindow,
        };

        Directory.CreateDirectory(GetSessionDirectory(id));
        await SaveAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task SaveAsync(
        SessionRecord session,
        CancellationToken cancellationToken = default)
    {
        ValidateId(session.Id);
        var directory = GetSessionDirectory(session.Id);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, MetadataFileName);
        var temporary = Path.Combine(directory, $".{MetadataFileName}.{Guid.NewGuid():N}.tmp");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            _writeLock.Release();
        }
    }

    public async Task<SessionRecord> LoadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var path = Path.Combine(GetSessionDirectory(id), MetadataFileName);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SessionRecord>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Session metadata is empty: {path}");
    }

    public async Task<IReadOnlyList<SessionRecord>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var sessions = new List<SessionRecord>();
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = File.OpenRead(path);
            var session = await JsonSerializer.DeserializeAsync<SessionRecord>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return sessions
            .OrderByDescending(session => session.StartedAt)
            .ToArray();
    }

    public string ResolveFile(SessionRecord session, string relativeFile)
    {
        ValidateId(session.Id);
        if (Path.IsPathRooted(relativeFile) || Path.GetFileName(relativeFile) != relativeFile)
        {
            throw new ArgumentException("Session files must use a simple relative file name.", nameof(relativeFile));
        }

        return Path.Combine(GetSessionDirectory(session.Id), relativeFile);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = GetSessionDirectory(id);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string GetSessionDirectory(string id)
    {
        ValidateId(id);
        return Path.Combine(_rootDirectory, id);
    }

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (Path.GetFileName(id) != id || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid session ID.", nameof(id));
        }
    }
}
