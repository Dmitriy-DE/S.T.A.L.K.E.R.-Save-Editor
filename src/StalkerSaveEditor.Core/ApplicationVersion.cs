using System.Reflection;

namespace StalkerSaveEditor.Core;

public static class ApplicationVersion
{
    /// <summary>Full SemVer including a prerelease tag (1.0.0-rc.1), without "+commit" build metadata.</summary>
    public static string Current { get; } =
        typeof(ApplicationVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(ApplicationVersion).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException("The Core assembly has no version.");
}
