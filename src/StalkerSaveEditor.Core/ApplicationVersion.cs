namespace StalkerSaveEditor.Core;

public static class ApplicationVersion
{
    public static string Current { get; } =
        typeof(ApplicationVersion).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException("The Core assembly has no version.");
}
