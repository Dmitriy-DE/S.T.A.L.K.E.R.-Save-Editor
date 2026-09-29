using System.Text.Json;

namespace StalkerSaveEditor.Core.Catalogs;

/// <summary>What an S.T.A.L.K.E.R. 2 armour upgrade does: effect key (e.g. "psy", "fire_chem") and tier.</summary>
public sealed record Stalker2ArmorUpgrade(string Effect, int Tier);

/// <summary>What an S2 weapon upgrade changes: the weapon part (Barrel, Stock…) and its effect icon (Recoil…).</summary>
public sealed record Stalker2WeaponUpgrade(string Part, string Effect);

/// <summary>
/// Maps armour upgrade prototype SIDs stored in S2 saves (e.g. Exoskeleton_Monolith_Armor_PSY_Left_2_2) to the
/// effect and tier of their localization key (sid_upgrades_ArmUpg_psy_02_name). Official texts are not shipped;
/// the UI composes a readable name from the effect and tier.
/// </summary>
public static class Stalker2ArmorUpgrades
{
    private static readonly Lazy<Dictionary<string, Stalker2ArmorUpgrade>> Map = new(Load);
    private static readonly Lazy<Dictionary<string, Stalker2WeaponUpgrade>> WeaponMap = new(LoadWeapons);

    public static Stalker2WeaponUpgrade? FindWeapon(string sid) =>
        WeaponMap.Value.TryGetValue(sid, out var upgrade) ? upgrade : null;

    public static Stalker2ArmorUpgrade? Find(string sid) =>
        Map.Value.TryGetValue(sid, out var upgrade) ? upgrade : null;

    public static int Count => Map.Value.Count;

    private static Dictionary<string, Stalker2WeaponUpgrade> LoadWeapons()
    {
        using var document = Open();
        var result = new Dictionary<string, Stalker2WeaponUpgrade>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("weapon_upgrades").EnumerateObject())
        {
            result[entry.Name] = new Stalker2WeaponUpgrade(
                entry.Value.GetProperty("part").GetString()!,
                entry.Value.GetProperty("effect").GetString()!);
        }

        return result;
    }

    private static JsonDocument Open()
    {
        using var stream = typeof(Stalker2ArmorUpgrades).Assembly.GetManifestResourceStream(
            "StalkerSaveEditor.Core.Catalogs.Data.s2_upgrades.json")
            ?? throw new InvalidOperationException("s2_upgrades.json is not embedded.");
        return JsonDocument.Parse(stream);
    }

    private static Dictionary<string, Stalker2ArmorUpgrade> Load()
    {
        using var document = Open();
        var result = new Dictionary<string, Stalker2ArmorUpgrade>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("upgrades").EnumerateObject())
        {
            result[entry.Name] = new Stalker2ArmorUpgrade(
                entry.Value.GetProperty("effect").GetString()!,
                entry.Value.GetProperty("tier").GetInt32());
        }

        return result;
    }
}
