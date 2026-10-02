using System.Buffers;
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

        // The input is passed in place (pinned for the call) instead of being copied, and the padded work buffer the
        // decoder needs is reused between saves.
        var target = RentWork(checked(expectedSize + DecoderSafeSpace), out var pooled);
        try
        {
            var written = InvokeNativeDecoder(stream, target, expectedSize);
            if (written != expectedSize)
            {
                throw new InvalidDataException(
                    $"Kraken decoder returned {written} bytes; expected {expectedSize}.");
            }

            return target.AsSpan(0, expectedSize).ToArray();
        }
        finally
        {
            if (pooled) ArrayPool<byte>.Shared.Return(target);
        }
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
        var target = RentWork(capacity, out var pooled);
        try
        {
            var written = InvokeNativeEncoder(payload, level, target, capacity);
            if (written <= 0 || written > capacity)
            {
                throw new InvalidDataException($"Kraken encoder returned invalid size {written}.");
            }

            return target.AsSpan(0, written).ToArray();
        }
        finally
        {
            if (pooled) ArrayPool<byte>.Shared.Return(target);
        }
    }

    private const int LargestPooledWork = 64 * 1024 * 1024;

    /// <summary>Ordinary saves reuse a pooled buffer; a huge one gets its own so the pool does not keep it afterwards.</summary>
    private static byte[] RentWork(int size, out bool pooled)
    {
        pooled = size <= LargestPooledWork;
        return pooled ? ArrayPool<byte>.Shared.Rent(size) : GC.AllocateUninitializedArray<byte>(size);
    }

    private static int InvokeNativeDecoder(ReadOnlySpan<byte> source, byte[] target, int expectedSize)
    {
        try
        {
            return NativeMethods.Decompress(in MemoryMarshal.GetReference(source), source.Length, target, expectedSize);
        }
        catch (Exception exception) when (IsNativeLibraryError(exception))
        {
            throw new InvalidDataException("The native Kraken library could not be loaded.", exception);
        }
    }

    private static int InvokeNativeEncoder(ReadOnlySpan<byte> source, int level, byte[] target, int capacity)
    {
        try
        {
            return NativeMethods.Compress(in MemoryMarshal.GetReference(source), source.Length, level, target, capacity);
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
            in byte source,
            int sourceLength,
            [Out] byte[] target,
            int targetLength);

        [DllImport(
            "stalker_ooz",
            EntryPoint = "stalker_kraken_compress",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int Compress(
            in byte source,
            int sourceLength,
            int level,
            [Out] byte[] target,
            int targetCapacity);
    }
}
