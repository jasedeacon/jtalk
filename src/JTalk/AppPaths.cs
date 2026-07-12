namespace JTalk;

/// <summary>Root of all per-user jtalk state: %APPDATA%\jtalk.</summary>
internal static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jtalk");
}
