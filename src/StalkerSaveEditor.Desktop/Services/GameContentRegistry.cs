using System.Collections.Concurrent;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Catalogs read from the X-Ray games installed on this machine (mods included). They replace the
/// shipped catalog for that release once loaded; until then, or when a game is not installed, the
/// shipped catalog is used.
/// </summary>
public static class GameContentRegistry
{
    private static readonly ConcurrentDictionary<string, GameContent> Loaded = new(StringComparer.Ordinal);

    /// <summary>Raised (off the UI thread) after at least one installed game was read.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyCollection<GameContentStatus> Statuses => Loaded.Values.Select(content => content.Status).ToArray();

    public static bool TryGetCatalog(string releaseId, out CatalogBundle bundle)
    {
        if (Loaded.TryGetValue(releaseId.Replace("-ee", string.Empty, StringComparison.Ordinal), out var content))
        {
            bundle = content.Bundle;
            return true;
        }

        bundle = null!;
        return false;
    }

    /// <summary>Finds installed games and reads (or re-uses cached) content; failures are logged, never thrown.</summary>
    public static void LoadInstalled(string? modsRoot = null, string uiLanguage = "ru")
    {
        var installer = new CompanionInstaller(modsRoot ?? Path.Combine(AppContext.BaseDirectory, "mods", "companion"));
        var any = false;
        foreach (var game in Enum.GetValues<CompanionGame>())
        {
            try
            {
                var status = installer.GetStatus(game);
                if (!status.GameFound || status.GameDirectory is null) continue;
                var content = GameContentService.Load(game, status.GameDirectory, AppPaths.ContentCache, uiLanguage);
                if (content is null) continue;
                Register(content);
                any = true;
                AppLog.Info($"game content {content.Status.ReleaseId}: {content.Status.ItemCount} items, mod {content.Status.ModName ?? "none"}, cache {content.Status.FromCache}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppLog.Warn($"game content {game} not loaded", exception);
            }
        }

        if (any) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Register(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Loaded[content.Status.ReleaseId] = content;
        ItemIconService.UseGameContent(content);
    }
}
