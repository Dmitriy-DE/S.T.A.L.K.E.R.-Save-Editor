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

    public static bool TryGetContent(string releaseId, out GameContent content) =>
        Loaded.TryGetValue(releaseId.Replace("-ee", string.Empty, StringComparison.Ordinal), out content!);

    public static IReadOnlyList<GameContent> GetLoadedContents() =>
        Loaded.Values.OrderBy(content => content.Status.ReleaseId, StringComparer.Ordinal).ToArray();

    /// <summary>Finds installed games and reads (or re-uses cached) content; failures are logged, never thrown.</summary>
    public static void LoadInstalled(string? modsRoot = null, string uiLanguage = "ru")
    {
        var installer = new CompanionInstaller(modsRoot ?? Path.Combine(AppContext.BaseDirectory, "mods", "companion"));
        foreach (var game in Enum.GetValues<CompanionGame>())
        {
            try
            {
                var status = installer.GetStatus(game);
                if (!status.GameFound || status.GameDirectory is null) continue;
                var content = GameContentService.Load(game, status.GameDirectory, AppPaths.ContentCache, uiLanguage);
                if (content is null) continue;
                Register(content);
                AppLog.Info($"game content {content.Status.ReleaseId}: {content.Status.ItemCount} items, mod {content.Status.ModName ?? "none"}, cache {content.Status.FromCache}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppLog.Warn($"game content {game} not loaded", exception);
            }
        }
    }

    public static void Register(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Loaded[content.Status.ReleaseId] = content;
        ItemIconService.UseGameContent(content);
    }
}
