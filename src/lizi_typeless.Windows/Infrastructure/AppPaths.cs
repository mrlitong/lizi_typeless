namespace lizi_typeless.Windows.Infrastructure;

internal static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "lizi_typeless");

    public static string SessionsDirectory { get; } = Path.Combine(RootDirectory, "sessions");

    public static string LogFile { get; } = Path.Combine(RootDirectory, "diagnostics.log");
}
