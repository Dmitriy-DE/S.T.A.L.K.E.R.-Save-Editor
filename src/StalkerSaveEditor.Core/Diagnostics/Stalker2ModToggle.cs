namespace StalkerSaveEditor.Core.Diagnostics;

public enum Stalker2ModState
{
    InstallationMissing,
    NoCustomMods,
    Enabled,
    Disabled,
    Conflict,
}

public sealed record Stalker2ModToggleResult(bool Changed, Stalker2ModState State, string Message);

/// <summary>
/// Reversibly moves S2's custom ~mods directory outside Content/Paks. It never deletes or copies mod files.
/// The action is opt-in and is intended for troubleshooting stale custom mods after game updates.
/// </summary>
public static class Stalker2ModToggle
{
    public static Stalker2ModState GetState(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var paths = Resolve(gameDirectory);
        if (!Directory.Exists(paths.Paks)) return Stalker2ModState.InstallationMissing;
        var enabled = Directory.Exists(paths.Enabled);
        var disabled = Directory.Exists(paths.Disabled);
        return (enabled, disabled) switch
        {
            (true, true) => Stalker2ModState.Conflict,
            (true, false) => Stalker2ModState.Enabled,
            (false, true) => Stalker2ModState.Disabled,
            _ => Stalker2ModState.NoCustomMods,
        };
    }

    public static Stalker2ModToggleResult Disable(string gameDirectory)
    {
        var paths = Resolve(gameDirectory);
        RequireInstall(paths);
        var state = GetState(gameDirectory);
        if (state == Stalker2ModState.Conflict) throw new IOException("Both the active and recovery ~mods folders exist; refusing to move either one.");
        if (state != Stalker2ModState.Enabled)
        {
            return new Stalker2ModToggleResult(false, state, state == Stalker2ModState.Disabled ? "Custom mods are already disabled." : "No custom mods folder was found.");
        }

        EnsurePlainDirectory(paths.Root, paths.Content, paths.Paks, paths.Enabled);
        if (File.Exists(paths.Disabled)) throw new IOException("The recovery path is occupied by a file; refusing to overwrite it.");
        Directory.Move(paths.Enabled, paths.Disabled);
        return new Stalker2ModToggleResult(true, Stalker2ModState.Disabled, "Custom mods moved outside Content/Paks; files were preserved.");
    }

    public static Stalker2ModToggleResult Restore(string gameDirectory)
    {
        var paths = Resolve(gameDirectory);
        RequireInstall(paths);
        var state = GetState(gameDirectory);
        if (state == Stalker2ModState.Conflict) throw new IOException("Both the active and recovery ~mods folders exist; remove or move one manually before restoring.");
        if (state != Stalker2ModState.Disabled)
        {
            return new Stalker2ModToggleResult(false, state, state == Stalker2ModState.Enabled ? "Custom mods are already enabled." : "No disabled custom mods folder was found.");
        }

        EnsurePlainDirectory(paths.Root, paths.Content, paths.Paks, paths.Disabled);
        Directory.Move(paths.Disabled, paths.Enabled);
        return new Stalker2ModToggleResult(true, Stalker2ModState.Enabled, "Custom mods restored to Content/Paks/~mods.");
    }

    private static void RequireInstall(Paths paths)
    {
        if (!Directory.Exists(paths.Root) || !Directory.Exists(paths.Paks))
        {
            throw new DirectoryNotFoundException("The selected directory does not contain Stalker2/Content/Paks.");
        }
    }

    private static void EnsurePlainDirectory(params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Required game directory is missing: " + directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Refusing to move a mod directory through a link or reparse point: " + directory);
            }
        }
    }

    private static Paths Resolve(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        var content = Path.Combine(root, "Stalker2", "Content");
        var paks = Path.Combine(content, "Paks");
        return new Paths(root, content, paks, Path.Combine(paks, "~mods"), Path.Combine(content, "~mods.disabled"));
    }

    private sealed record Paths(string Root, string Content, string Paks, string Enabled, string Disabled);
}
