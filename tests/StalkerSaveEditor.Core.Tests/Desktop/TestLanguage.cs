using System.Runtime.CompilerServices;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Core.Tests.Desktop;

internal static class TestLanguage
{
    /// <summary>Interface assertions are written against the Russian source text, whatever the machine's language.</summary>
    [ModuleInitializer]
    internal static void UseRussian() => I18nService.Instance.SetLanguage("ru");
}
