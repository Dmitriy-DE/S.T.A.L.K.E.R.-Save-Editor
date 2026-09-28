using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Steam;

internal sealed class SteamNativeUserStats : ISteamUserStats
{
    private const uint MaximumAchievementCount = 100_000;
    private IntPtr _stats;
    private bool _initialized;

    public void Initialize(int appId)
    {
        if (!SteamAchievementsWorkerHost.IsSupportedAppId(appId))
        {
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam achievement app id is unsupported.");
        }

        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!SteamNativeApi.Init())
        {
            throw new InvalidOperationException("SteamAPI_Init failed. The Steam client may not be running.");
        }

        _initialized = true;
        _stats = SteamUserStatsApi.GetAccessor()();
        if (_stats == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Steam ISteamUserStats interface is unavailable.");
        }
    }

    public IReadOnlyList<SteamAchievement> List()
    {
        EnsureConnected();
        var count = WaitForAchievementCount(_stats, TimeSpan.FromSeconds(10));
        if (count == 0)
        {
            throw new InvalidOperationException("Steam returned no achievements for this game or did not load its statistics.");
        }

        if (count > MaximumAchievementCount)
        {
            throw new InvalidDataException("Steam returned an invalid achievement count.");
        }

        var result = new List<SteamAchievement>(checked((int)count));
        for (uint index = 0; index < count; index++)
        {
            var apiNamePointer = SteamUserStatsApi.GetAchievementName(_stats, index);
            if (apiNamePointer == IntPtr.Zero)
            {
                continue;
            }

            var apiName = Marshal.PtrToStringUTF8(apiNamePointer);
            if (string.IsNullOrEmpty(apiName))
            {
                continue;
            }

            if (!SteamUserStatsApi.GetAchievementAndUnlockTime(_stats, apiName, out var achieved, out var unlockTime))
            {
                throw new InvalidOperationException($"Steam did not return the state for achievement {apiName}.");
            }

            var hidden = string.Equals(
                SteamUserStatsApi.GetDisplayAttribute(_stats, apiName, "hidden"),
                "1",
                StringComparison.Ordinal);
            result.Add(new SteamAchievement(
                apiName,
                SteamUserStatsApi.GetDisplayAttribute(_stats, apiName, "name"),
                SteamUserStatsApi.GetDisplayAttribute(_stats, apiName, "desc"),
                achieved,
                unlockTime,
                hidden));
        }

        return result;
    }

    public SteamAchievement Set(string apiName, bool achieved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        EnsureConnected();
        var existing = List().FirstOrDefault(item => string.Equals(item.ApiName, apiName, StringComparison.Ordinal));
        if (existing is null)
        {
            throw new InvalidOperationException($"The game does not define achievement {apiName}.");
        }

        var changed = achieved
            ? SteamUserStatsApi.SetAchievement(_stats, apiName)
            : SteamUserStatsApi.ClearAchievement(_stats, apiName);
        if (!changed || !SteamUserStatsApi.StoreStats(_stats))
        {
            throw new InvalidOperationException($"Steam refused to change achievement {apiName}.");
        }

        for (var callback = 0; callback < 10; callback++)
        {
            SteamNativeApi.RunCallbacks();
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }

        var updated = List().First(item => string.Equals(item.ApiName, apiName, StringComparison.Ordinal));
        if (updated.Achieved != achieved)
        {
            throw new InvalidOperationException($"Steam did not confirm achievement state {apiName}.");
        }

        return updated;
    }

    public void Dispose()
    {
        _stats = IntPtr.Zero;
        if (_initialized)
        {
            SteamNativeApi.Shutdown();
            _initialized = false;
        }
    }

    private static uint WaitForAchievementCount(IntPtr stats, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            SteamNativeApi.RunCallbacks();
            var count = SteamUserStatsApi.GetNumAchievements(stats);
            if (count != 0 || elapsed.Elapsed >= timeout)
            {
                return count;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }
    }

    private void EnsureConnected()
    {
        if (!_initialized || _stats == IntPtr.Zero)
        {
            throw new InvalidOperationException("Steam ISteamUserStats is not connected.");
        }
    }
}

internal static class SteamUserStatsApi
{
    private static readonly IntPtr Library = SteamNativeApi.GetLibraryHandle();
    private static readonly GetNumAchievementsDelegate GetNumAchievementsFunction;
    private static readonly GetAchievementNameDelegate GetAchievementNameFunction;
    private static readonly GetAchievementDisplayAttributeDelegate GetDisplayAttributeFunction;
    private static readonly GetAchievementAndUnlockTimeDelegate GetAchievementAndUnlockTimeFunction;
    private static readonly SetAchievementDelegate SetAchievementFunction;
    private static readonly SetAchievementDelegate ClearAchievementFunction;
    private static readonly StoreStatsDelegate StoreStatsFunction;
    private static readonly UserStatsAccessor Accessor;

    static SteamUserStatsApi()
    {
        var accessorName = new[] { 13, 12, 11 }
            .Select(version => $"SteamAPI_SteamUserStats_v{version:000}")
            .FirstOrDefault(name => NativeLibrary.TryGetExport(Library, name, out _));
        if (accessorName is null)
        {
            throw new EntryPointNotFoundException("Steam ISteamUserStats v013 accessor was not found.");
        }

        Accessor = Marshal.GetDelegateForFunctionPointer<UserStatsAccessor>(NativeLibrary.GetExport(Library, accessorName));
        GetNumAchievementsFunction = Bind<GetNumAchievementsDelegate>("SteamAPI_ISteamUserStats_GetNumAchievements");
        GetAchievementNameFunction = Bind<GetAchievementNameDelegate>("SteamAPI_ISteamUserStats_GetAchievementName");
        GetDisplayAttributeFunction = Bind<GetAchievementDisplayAttributeDelegate>("SteamAPI_ISteamUserStats_GetAchievementDisplayAttribute");
        GetAchievementAndUnlockTimeFunction = Bind<GetAchievementAndUnlockTimeDelegate>("SteamAPI_ISteamUserStats_GetAchievementAndUnlockTime");
        SetAchievementFunction = Bind<SetAchievementDelegate>("SteamAPI_ISteamUserStats_SetAchievement");
        ClearAchievementFunction = Bind<SetAchievementDelegate>("SteamAPI_ISteamUserStats_ClearAchievement");
        StoreStatsFunction = Bind<StoreStatsDelegate>("SteamAPI_ISteamUserStats_StoreStats");
    }

    public static UserStatsAccessor GetAccessor() => Accessor;

    public static uint GetNumAchievements(IntPtr stats) => GetNumAchievementsFunction(stats);

    public static IntPtr GetAchievementName(IntPtr stats, uint index) => GetAchievementNameFunction(stats, index);

    public static string GetDisplayAttribute(IntPtr stats, string apiName, string key) =>
        Marshal.PtrToStringUTF8(GetDisplayAttributeFunction(stats, apiName, key)) ?? string.Empty;

    public static bool GetAchievementAndUnlockTime(IntPtr stats, string apiName, out bool achieved, out uint unlockTime) =>
        GetAchievementAndUnlockTimeFunction(stats, apiName, out achieved, out unlockTime);

    public static bool SetAchievement(IntPtr stats, string apiName) => SetAchievementFunction(stats, apiName);

    public static bool ClearAchievement(IntPtr stats, string apiName) => ClearAchievementFunction(stats, apiName);

    public static bool StoreStats(IntPtr stats) => StoreStatsFunction(stats);

    private static T Bind<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr UserStatsAccessor();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetNumAchievementsDelegate(IntPtr stats);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetAchievementNameDelegate(IntPtr stats, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetAchievementDisplayAttributeDelegate(
        IntPtr stats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string apiName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetAchievementAndUnlockTimeDelegate(
        IntPtr stats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string apiName,
        [MarshalAs(UnmanagedType.I1)] out bool achieved,
        out uint unlockTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetAchievementDelegate(
        IntPtr stats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string apiName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool StoreStatsDelegate(IntPtr stats);
}
