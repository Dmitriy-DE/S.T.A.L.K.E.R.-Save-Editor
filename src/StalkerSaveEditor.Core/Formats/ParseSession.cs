using System.Security.Cryptography;

namespace StalkerSaveEditor.Core.Formats;

/// <summary>
/// One parse per distinct save image during an edit. Preparing an edit reads the same bytes several times: the edit
/// service checks the release, each writer parses its input and re-parses its output, and the next stage starts from
/// that same output. While a session is open on the thread, the readers return the model they already built for
/// identical bytes (keyed by SHA-256), so every image is parsed once. The writers are not changed: outside a session
/// (and for any bytes not seen before) the readers work exactly as they always did. Models are immutable, which is
/// what makes sharing them safe.
/// </summary>
internal sealed class ParseSession : IDisposable
{
    [ThreadStatic]
    private static ParseSession? _current;

    private readonly Dictionary<(string Kind, string Sha256), object> _models = [];
    private readonly bool _owner;

    private ParseSession(bool owner) => _owner = owner;

    internal delegate T Parser<out T>(ReadOnlySpan<byte> data);

    /// <summary>Parses performed and parses avoided since the session was opened.</summary>
    public int Misses { get; private set; }

    public int Hits { get; private set; }

    internal static ParseSession? Current => _current;

    /// <summary>Opens a session on this thread; inside an open one it joins it (disposing the inner handle does nothing).</summary>
    public static ParseSession Begin()
    {
        if (_current is not null) return new ParseSession(owner: false);
        return _current = new ParseSession(owner: true);
    }

    internal static T GetOrParse<T>(string kind, ReadOnlySpan<byte> data, Parser<T> parse)
        where T : class
    {
        var session = _current;
        if (session is null) return parse(data);
        var key = (kind, Convert.ToHexString(SHA256.HashData(data)));
        if (session._models.TryGetValue(key, out var known))
        {
            session.Hits++;
            return (T)known;
        }

        // A failed parse throws and is not remembered.
        var model = parse(data);
        session._models[key] = model;
        session.Misses++;
        return model;
    }

    public void Dispose()
    {
        if (_owner && ReferenceEquals(_current, this)) _current = null;
    }
}
