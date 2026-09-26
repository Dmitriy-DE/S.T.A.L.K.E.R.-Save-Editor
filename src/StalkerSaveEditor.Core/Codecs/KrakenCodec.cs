using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Core.Codecs;

public static class KrakenCodec
{
    public const int MaximumUnpackedSize = 512 * 1024 * 1024;
    private const int DecoderSafeSpace = 64;
    private const int BlockSize = 0x40000;

    public static byte[] Decompress(ReadOnlySpan<byte> stream, int expectedSize)
    {
        if (expectedSize <= 0 || expectedSize > MaximumUnpackedSize)
        {
            throw new InvalidDataException($"Invalid Kraken output size: {expectedSize}.");
        }

        if (stream.IsEmpty)
        {
            throw new InvalidDataException("Kraken stream is empty.");
        }

        var source = stream.ToArray();
        var target = GC.AllocateUninitializedArray<byte>(checked(expectedSize + DecoderSafeSpace));
        var written = InvokeNativeDecoder(source, target, expectedSize);
        if (written != expectedSize)
        {
            throw new InvalidDataException(
                $"Kraken decoder returned {written} bytes; expected {expectedSize}.");
        }

        return target.AsSpan(0, expectedSize).ToArray();
    }

    public static byte[] Compress(ReadOnlySpan<byte> payload, int level = 5)
    {
        if (payload.IsEmpty || payload.Length > MaximumUnpackedSize)
        {
            throw new InvalidDataException($"Invalid Kraken input size: {payload.Length}.");
        }

        if (level is < -3 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Kraken level must be between -3 and 9.");
        }

        var quanta = (payload.Length + 0x3FFFF) / BlockSize;
        var capacity = checked(payload.Length + 1024 + 512 * quanta);
        var source = payload.ToArray();
        var target = GC.AllocateUninitializedArray<byte>(capacity);
        var written = InvokeNativeEncoder(source, level, target);
        if (written <= 0 || written > target.Length)
        {
            throw new InvalidDataException($"Kraken encoder returned invalid size {written}.");
        }

        return target.AsSpan(0, written).ToArray();
    }

    private static int InvokeNativeDecoder(byte[] source, byte[] target, int expectedSize)
    {
        try
        {
            return NativeMethods.Decompress(source, source.Length, target, expectedSize);
        }
        catch (Exception exception) when (IsNativeLibraryError(exception))
        {
            throw new InvalidDataException("The native Kraken library could not be loaded.", exception);
        }
    }

    private static int InvokeNativeEncoder(byte[] source, int level, byte[] target)
    {
        try
        {
            return NativeMethods.Compress(source, source.Length, level, target, target.Length);
        }
        catch (Exception exception) when (IsNativeLibraryError(exception))
        {
            throw new InvalidDataException("The native Kraken library could not be loaded.", exception);
        }
    }

    private static bool IsNativeLibraryError(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private static class NativeMethods
    {
        [DllImport(
            "stalker_ooz",
            EntryPoint = "stalker_kraken_decompress",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int Decompress(
            [In] byte[] source,
            int sourceLength,
            [Out] byte[] target,
            int targetLength);

        [DllImport(
            "stalker_ooz",
            EntryPoint = "stalker_kraken_compress",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int Compress(
            [In] byte[] source,
            int sourceLength,
            int level,
            [Out] byte[] target,
            int targetCapacity);
    }
}
