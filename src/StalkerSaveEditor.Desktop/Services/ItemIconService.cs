using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Content;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Inventory icons: the installed game's own atlas when its content pack is loaded, otherwise the
/// icon pack shipped with the editor (X-Ray section keys; S2 via the item knowledge base).
/// Bitmaps are decoded once and cached; missing icons are remembered as missing.
/// </summary>
public static class ItemIconService
{
    private static readonly string IconRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, GameContent> GameContent = new(StringComparer.Ordinal);

    /// <summary>Registers content read from an installed game; its icons win over the shipped pack.</summary>
    public static void UseGameContent(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        GameContent[content.Status.ReleaseId] = content;
        Cache.Clear();
    }

    /// <summary>Relative icon path for an item, or null: <c>xray/&lt;key&gt;.png</c> or the S2 catalog entry.</summary>
    public static string? IconKey(string releaseId, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemKey)) return null;
        if (releaseId.StartsWith("stalker2", StringComparison.Ordinal))
        {
            return Stalker2ItemCatalog.LoadEmbedded().Icon(itemKey);
        }

        return $"xray/{itemKey}.png";
    }

    /// <summary>
    /// Shows the icon in <paramref name="image"/>: at once when it is on disk, otherwise (web host)
    /// fetched from the site's Assets/Icons in the background and set on the UI thread.
    /// </summary>
    public static void Show(Avalonia.Controls.Image image, string releaseId, string itemKey)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.Source = Load(releaseId, itemKey);
        if (image.Source is not null || HostPlatform.FetchAsset is not { } fetch) return;
        var relative = IconKey(releaseId.Replace("-ee", string.Empty, StringComparison.Ordinal), itemKey);
        if (relative is null) return;
        BackgroundTask.Run(ShowFetchedAsync(image, fetch, "Assets/Icons/" + relative, $"{releaseId}|{itemKey}"), "item icon");
    }

    private static async Task ShowFetchedAsync(Avalonia.Controls.Image image, Func<string, Task<byte[]?>> fetch, string path, string cacheKey)
    {
        try
        {
            if (Remote.TryGetValue(cacheKey, out var known))
            {
                image.Source = known;
                return;
            }

            var bytes = await fetch(path);
            Bitmap? bitmap = null;
            if (bytes is { Length: > 0 })
            {
                using var stream = new MemoryStream(bytes);
                bitmap = new Bitmap(stream);
            }

            Remote[cacheKey] = bitmap;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or NotSupportedException or HttpRequestException)
        {
            Remote[cacheKey] = null;
        }
    }

    private static readonly ConcurrentDictionary<string, Bitmap?> Remote = new(StringComparer.Ordinal);

    public static Bitmap? Load(string releaseId, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemKey)) return null;
        var family = releaseId.Replace("-ee", string.Empty, StringComparison.Ordinal);
        return Cache.GetOrAdd($"{family}|{itemKey}", _ => LoadUncached(family, itemKey));
    }

    private static Bitmap? LoadUncached(string family, string itemKey)
    {
        try
        {
            if (GameContent.TryGetValue(family, out var content) && content.IconPng(itemKey) is { } png)
            {
                using var stream = new MemoryStream(png);
                return new Bitmap(stream);
            }

            var relative = IconKey(family, itemKey);
            if (relative is null) return null;
            var path = Path.Combine(IconRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
