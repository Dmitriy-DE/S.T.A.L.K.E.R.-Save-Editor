using System.Security.Cryptography;
using System.Text;

namespace StalkerSaveEditor.Updater;

/// <summary>
/// ECDSA P-256 / SHA-256 signature over the exact bytes of latest.json, published next to it as latest.json.sig
/// (base64 of an IEEE P1363 or DER signature). The manifest carries every artifact's SHA-256, so a valid signature covers the downloads too:
/// someone who can change files on the download host still cannot make the app accept them.
/// The private key lives only on the publisher's machine (~/.config/stalker-save-editor/update-signing-key.pem).
/// </summary>
public static class UpdateSignature
{
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEugfpzNXArEpRIoUsxrmG6KWIWMFc
        DuEkmd6oGnQq6qsZmILc2fYC0wfqEMk/NB88BSFAC1N6fmziJf11RVtlLQ==
        -----END PUBLIC KEY-----
        """;

    public const int MaximumSignatureBytes = 1024;

    public static bool Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureFile, string publicKeyPem = PublicKeyPem)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        // P1363 (this class) or DER (openssl dgst -sign, used by the release tool).
        return key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation) ||
            key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>Signature file content for <paramref name="manifest"/> (release tooling and tests).</summary>
    public static string Sign(ReadOnlySpan<byte> manifest, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) + "\n";
    }
}
