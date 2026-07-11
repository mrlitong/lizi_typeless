using System.Globalization;

namespace lizi_typeless.Windows.Infrastructure;

internal static class DiagnosticLog
{
    private static readonly object Sync = new();

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RootDirectory);
            var entry = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            if (exception is not null)
            {
                entry += exception + Environment.NewLine;
            }

            lock (Sync)
            {
                File.AppendAllText(AppPaths.LogFile, entry);
            }
        }
        catch
        {
            // Diagnostics must never break recording or recovery.
        }
    }
}
