using System.Diagnostics;
using lizi_typeless.Windows.Infrastructure;

namespace lizi_typeless.Windows.Inference;

internal sealed class InferenceServiceManager : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(4);

    private readonly InferenceClient _client;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _startedProcess;

    public InferenceServiceManager(InferenceClient client)
    {
        _client = client;
    }

    public async Task<InferenceHealth> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var health = await TryGetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health?.Ready == true)
        {
            return health;
        }

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            health = await TryGetHealthAsync(cancellationToken).ConfigureAwait(false);
            if (health?.Ready == true)
            {
                return health;
            }

            if (_startedProcess is null || _startedProcess.HasExited)
            {
                _startedProcess?.Dispose();
                _startedProcess = StartServiceProcess();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartupTimeout);
            while (!timeout.IsCancellationRequested)
            {
                if (_startedProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        $"The inference service exited with code {_startedProcess.ExitCode}. " +
                        $"See {AppPaths.LogFile} and inference/logs/service.log.");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
                health = await TryGetHealthAsync(timeout.Token).ConfigureAwait(false);
                if (health?.Ready == true)
                {
                    return health;
                }
            }

            throw new TimeoutException("The local inference service did not become ready in four minutes.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The local inference service did not become ready in four minutes.");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_startedProcess is not null && !_startedProcess.HasExited)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _client.RequestShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DiagnosticLog.Write("Inference service shutdown request failed.", exception);
            }
        }

        _startedProcess?.Dispose();
        _startLock.Dispose();
    }

    private async Task<InferenceHealth?> TryGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static Process StartServiceProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add("Ubuntu");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(
            "cd /home/litong/lizi_typeless && exec ./scripts/start-inference.sh");

        DiagnosticLog.Write("Starting the WSL inference service.");
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start wsl.exe.");
    }
}
