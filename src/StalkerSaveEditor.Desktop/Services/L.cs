namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Interface text: the Russian source string is the key (as in the Python editor), translated into
/// the language chosen in Settings; {0}-style placeholders are filled after translation.
/// </summary>
public static class L
{
    public static string T(string russian, params object?[] args) => I18nService.Instance.Tr(russian, args);
}
