using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using StalkerSaveEditor.Desktop;
using StalkerSaveEditor.Desktop.Services;

[assembly: SupportedOSPlatform("browser")]

namespace StalkerSaveEditor.Browser;

internal static partial class Program
{
    private static async Task Main()
    {
        await JSHost.ImportAsync("save-editor", "../download.js");
        // A written save is offered back as a download (the browser has no access to the user's folders).
        HostPlatform.ExportFile = path =>
        {
            Download(Path.GetFileName(path), File.ReadAllBytes(path));
            return Task.CompletedTask;
        };

        // The page ships Latin and Cyrillic fonts only and a browser offers no system fallback,
        // so Japanese, Korean and Chinese fall back to English here (the desktop app uses system fonts).
        if (I18nService.Instance.CurrentLanguage is "ja" or "ko" or "zh-CN" or "zh-TW") I18nService.Instance.SetLanguage("en");

        var site = new Uri(PageUrl());
        var assets = new HttpClient { BaseAddress = site };
        HostPlatform.FetchAsset = async path =>
        {
            using var response = await assets.GetAsync(new Uri(site, path));
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        };

        await ImportLinkedSaveAsync();
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Console.Error.WriteLine("unhandled: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => Console.Error.WriteLine("unobserved: " + args.Exception);
        try
        {
            await AppBuilder.Configure<App>().StartBrowserAppAsync("out");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("startup failed: " + exception);
            throw;
        }
    }

    /// <summary><c>?open=relative/path.sav</c>: a save published next to the page (same site only).</summary>
    private static async Task ImportLinkedSaveAsync()
    {
        var page = new Uri(PageUrl());
        var query = System.Web.HttpUtility.ParseQueryString(page.Query);
        var open = query["open"];
        if (string.IsNullOrWhiteSpace(open)) return;
        var target = new Uri(page, open);
        if (target.Scheme != page.Scheme || target.Authority != page.Authority) return;
        try
        {
            using var client = new HttpClient();
            await using var stream = await client.GetStreamAsync(target);
            HostPlatform.StartupFiles.Add(await HostPlatform.ImportAsync(stream, Path.GetFileName(target.AbsolutePath)));
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine("open link failed: " + exception.Message);
        }
    }

    [JSImport("pageUrl", "save-editor")]
    private static partial string PageUrl();

    [JSImport("download", "save-editor")]
    private static partial void Download(string fileName, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);
}
