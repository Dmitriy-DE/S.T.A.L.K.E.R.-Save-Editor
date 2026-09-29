using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Formats.XRay;

/// <summary>The dynamic weather the level scripts saved: graph ("dynamic_default"), current and next state.</summary>
public sealed record XRayWeather(string Graph, string Current, string Next);

/// <summary>
/// Clear Sky and Call of Pripyat keep the weather in the actor's script storage as
/// "&lt;graph&gt;=&lt;current&gt;,&lt;next&gt;" (level_weathers.script; CoP under the key "weather_state").
/// Shadow of Chernobyl does not save weather, and underground levels have none.
/// </summary>
public static partial class XRayWeatherReader
{
    private static readonly HashSet<string> KnownStates = new(StringComparer.Ordinal)
    {
        "clear", "cloudy", "rain", "thunder", "pasmurno", "groza", "foggy", "storm",
    };

    public static XRayWeather? Read(XRayTrilogySave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.FormatId.StartsWith("stalker-soc", StringComparison.Ordinal)) return null;
        var actor = save.RegistryObjects.FirstOrDefault(item => item.ObjectId == save.ActorId);
        if (actor is null) return null;
        var raw = save.Container.Raw.Span;
        var text = Encoding.Latin1.GetString(raw.Slice(actor.RecordOffset, actor.RecordLength));
        if (actor.ClientDataOffset is { } clientData)
        {
            text += "\0" + Encoding.Latin1.GetString(raw.Slice(clientData, actor.ClientDataLength));
        }
        return Find(text) ?? FindUnique(Encoding.Latin1.GetString(raw));
    }

    /// <summary>
    /// Some Clear Sky saves keep the value outside the actor record (another object's script storage). Accept it
    /// only when the whole save holds exactly one distinct "dynamic_*" weather value, so an ambiguous save stays unknown.
    /// </summary>
    internal static XRayWeather? FindUnique(string text)
    {
        var found = WeatherValue().Matches(text)
            .Select(match => (Graph: match.Groups["graph"].Value, Current: match.Groups["current"].Value, Next: match.Groups["next"].Value))
            .Where(value => value.Graph.StartsWith("dynamic_", StringComparison.Ordinal)
                && KnownStates.Contains(value.Current) && KnownStates.Contains(value.Next))
            .Distinct()
            .ToArray();
        return found.Length == 1 ? new XRayWeather(found[0].Graph, found[0].Current, found[0].Next) : null;
    }

    internal static XRayWeather? Find(string text)
    {
        foreach (Match match in WeatherValue().Matches(text))
        {
            var current = match.Groups["current"].Value;
            var next = match.Groups["next"].Value;
            if (KnownStates.Contains(current) && KnownStates.Contains(next))
            {
                return new XRayWeather(match.Groups["graph"].Value, current, next);
            }
        }

        return null;
    }

    [GeneratedRegex(@"(?<graph>[a-z_]+)=(?<current>[a-z_]+),(?<next>[a-z_]+)\0", RegexOptions.CultureInvariant)]
    private static partial Regex WeatherValue();
}
