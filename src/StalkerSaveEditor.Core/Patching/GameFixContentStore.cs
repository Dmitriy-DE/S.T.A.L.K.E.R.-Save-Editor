using System.Security.Cryptography;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

/// <summary>
/// Content-addressed store for fix-pack files (whole-file overlays): <c>&lt;data&gt;/content/fixpacks/&lt;sha256&gt;</c>.
/// A file is only ever read back when its bytes still hash to its name, so a damaged or tampered cache is never installed.
/// </summary>
public static class GameFixContentStore
{
    public static string Directory => Path.Combine(AppPaths.ContentCache, "fixpacks");

    public static byte[]? Read(string sha256)
    {
        var path = Path.Combine(Directory, sha256);
        if (sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigitLower(c)) || !File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexStringLower(SHA256.HashData(bytes)) == sha256 ? bytes : null;
    }

    /// <summary>Stores <paramref name="bytes"/> under their SHA-256 and returns it.</summary>
    public static string Add(ReadOnlySpan<byte> bytes)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var path = Path.Combine(Directory, sha);
        if (!File.Exists(path)) Storage.AtomicFile.WriteAllBytes(path, bytes);
        return sha;
    }

    public static bool Contains(string sha256) => Read(sha256) is not null;
}
