using System.Reflection;
using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Steam;

internal sealed class SteamNativeRemoteStorage : ISteamRemoteStorage
{
    internal const int MaximumFileBytes = 64 * 1024 * 1024;
    private IntPtr _remoteStorage;
    private bool _initialized;

    public void Initialize(int appId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appId);

        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!SteamNativeApi.Init())
        {
            throw new InvalidOperationException("SteamAPI_Init failed. The Steam client may not be running.");
        }

        _initialized = true;
        var accessor = SteamNativeApi.GetRemoteStorageAccessor();
        _remoteStorage = accessor();
        if (_remoteStorage == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Steam ISteamRemoteStorage interface is unavailable.");
        }
    }

    public IReadOnlyList<SteamCloudFile> ListFiles()
    {
        EnsureConnected();
        SteamNativeApi.RunCallbacks();
        var count = SteamNativeApi.GetFileCount(_remoteStorage);
        if (count < 0 || count > 100_000)
        {
            throw new InvalidDataException("Steam RemoteStorage returned an invalid file count.");
        }

        var files = new List<SteamCloudFile>(count);
        for (var index = 0; index < count; index++)
        {
            var namePointer = SteamNativeApi.GetFileNameAndSize(_remoteStorage, index, out var size);
            if (namePointer == IntPtr.Zero)
            {
                continue;
            }

            var name = Marshal.PtrToStringUTF8(namePointer);
            if (string.IsNullOrEmpty(name) || size < 0)
            {
                continue;
            }

            files.Add(new SteamCloudFile(
                name,
                size,
                SteamNativeApi.GetFileTimestamp(_remoteStorage, name),
                SteamNativeApi.FilePersisted(_remoteStorage, name),
                SteamNativeApi.FileExists(_remoteStorage, name)));
        }

        return files.OrderByDescending(file => file.Timestamp).ToArray();
    }

    public void RunCallbacks()
    {
        EnsureConnected();
        SteamNativeApi.RunCallbacks();
    }

    public byte[] ReadFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        EnsureConnected();
        SteamNativeApi.RunCallbacks();
        if (!SteamNativeApi.FileExists(_remoteStorage, fileName))
        {
            throw new FileNotFoundException("Steam RemoteStorage file does not exist.", fileName);
        }

        var size = SteamNativeApi.GetFileSize(_remoteStorage, fileName);
        if (size < 0 || size > MaximumFileBytes)
        {
            throw new InvalidDataException("Steam RemoteStorage returned an invalid or oversized file.");
        }

        var bytes = new byte[size];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var read = SteamNativeApi.ReadFile(_remoteStorage, fileName, pin.AddrOfPinnedObject(), size);
            if (read != size)
            {
                throw new EndOfStreamException($"Steam RemoteStorage returned {read} of {size} bytes.");
            }
        }
        finally
        {
            pin.Free();
        }

        return bytes;
    }

    public void WriteFile(string fileName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(data);
        EnsureConnected();
        if (data.Length is 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException("Steam RemoteStorage write size is outside the supported range.");
        }

        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            if (!SteamNativeApi.WriteFile(
                _remoteStorage,
                fileName,
                pin.AddrOfPinnedObject(),
                data.Length))
            {
                throw new IOException($"Steam RemoteStorage rejected the write for {fileName}.");
            }
        }
        finally
        {
            pin.Free();
        }

        SteamNativeApi.RunCallbacks();
    }

    private void EnsureConnected()
    {
        if (!_initialized || _remoteStorage == IntPtr.Zero)
        {
            throw new InvalidOperationException("Steam RemoteStorage is not connected.");
        }
    }

    public void Dispose()
    {
        _remoteStorage = IntPtr.Zero;
        if (_initialized)
        {
            SteamNativeApi.Shutdown();
            _initialized = false;
        }
    }
}

internal static class SteamNativeApi
{
    private const string NativeLibraryName = "steam_api";
    private static readonly IntPtr LibraryHandle;
    private static readonly RemoteStorageAccessor Accessor;
    private static readonly GetFileCountDelegate GetFileCountFunction;
    private static readonly GetFileNameAndSizeDelegate GetFileNameAndSizeFunction;
    private static readonly GetFileTimestampDelegate GetFileTimestampFunction;
    private static readonly FileExistsDelegate FileExistsFunction;
    private static readonly FilePersistedDelegate FilePersistedFunction;
    private static readonly GetFileSizeDelegate GetFileSizeFunction;
    private static readonly FileReadDelegate FileReadFunction;
    private static readonly FileWriteDelegate FileWriteFunction;

    static SteamNativeApi()
    {
        var path = SteamLibraryLocator.FindLibraryPath()
            ?? throw new DllNotFoundException("Steam libsteam_api library was not found.");
        LibraryHandle = NativeLibrary.Load(path);
        var assembly = typeof(SteamNativeApi).Assembly;
        NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);

        var accessorName = Enumerable.Range(14, 7)
            .Reverse()
            .Select(version => $"SteamAPI_SteamRemoteStorage_v{version:000}")
            .FirstOrDefault(name => NativeLibrary.TryGetExport(LibraryHandle, name, out _));
        if (accessorName is null)
        {
            throw new EntryPointNotFoundException("Steam ISteamRemoteStorage accessor was not found.");
        }

        Accessor = Marshal.GetDelegateForFunctionPointer<RemoteStorageAccessor>(
            NativeLibrary.GetExport(LibraryHandle, accessorName));
        GetFileCountFunction = Bind<GetFileCountDelegate>("SteamAPI_ISteamRemoteStorage_GetFileCount");
        GetFileNameAndSizeFunction = Bind<GetFileNameAndSizeDelegate>("SteamAPI_ISteamRemoteStorage_GetFileNameAndSize");
        GetFileTimestampFunction = Bind<GetFileTimestampDelegate>("SteamAPI_ISteamRemoteStorage_GetFileTimestamp");
        FileExistsFunction = Bind<FileExistsDelegate>("SteamAPI_ISteamRemoteStorage_FileExists");
        FilePersistedFunction = Bind<FilePersistedDelegate>("SteamAPI_ISteamRemoteStorage_FilePersisted");
        GetFileSizeFunction = Bind<GetFileSizeDelegate>("SteamAPI_ISteamRemoteStorage_GetFileSize");
        FileReadFunction = Bind<FileReadDelegate>("SteamAPI_ISteamRemoteStorage_FileRead");
        FileWriteFunction = Bind<FileWriteDelegate>("SteamAPI_ISteamRemoteStorage_FileWrite");
    }

    [DllImport(NativeLibraryName, EntryPoint = "SteamAPI_Init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool Init();

    [DllImport(NativeLibraryName, EntryPoint = "SteamAPI_Shutdown", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Shutdown();

    [DllImport(NativeLibraryName, EntryPoint = "SteamAPI_RunCallbacks", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RunCallbacks();

    internal static RemoteStorageAccessor GetRemoteStorageAccessor() => Accessor;

    internal static int GetFileCount(IntPtr remote) => GetFileCountFunction(remote);

    internal static IntPtr GetFileNameAndSize(IntPtr remote, int index, out int size) =>
        GetFileNameAndSizeFunction(remote, index, out size);

    internal static long GetFileTimestamp(IntPtr remote, string name) => GetFileTimestampFunction(remote, name);

    internal static bool FileExists(IntPtr remote, string name) => FileExistsFunction(remote, name);

    internal static bool FilePersisted(IntPtr remote, string name) => FilePersistedFunction(remote, name);

    internal static int GetFileSize(IntPtr remote, string name) => GetFileSizeFunction(remote, name);

    internal static int ReadFile(IntPtr remote, string name, IntPtr buffer, int size) =>
        FileReadFunction(remote, name, buffer, size);

    internal static bool WriteFile(IntPtr remote, string name, IntPtr buffer, int size) =>
        FileWriteFunction(remote, name, buffer, size);

    internal static IntPtr GetLibraryHandle() => LibraryHandle;

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        return libraryName == NativeLibraryName ? LibraryHandle : IntPtr.Zero;
    }

    private static T Bind<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(LibraryHandle, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr RemoteStorageAccessor();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetFileCountDelegate(IntPtr remote);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetFileNameAndSizeDelegate(IntPtr remote, int index, out int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetFileTimestampDelegate(IntPtr remote, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool FileExistsDelegate(IntPtr remote, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool FilePersistedDelegate(IntPtr remote, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetFileSizeDelegate(IntPtr remote, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FileReadDelegate(
        IntPtr remote,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr buffer,
        int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool FileWriteDelegate(
        IntPtr remote,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr buffer,
        int size);
}
