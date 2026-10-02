using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Names of levels and stash boxes as the games show them. Saves store object names (<c>zat_a2_actor_treasure</c>,
/// <c>L02_Garbage</c>); the names come from the games' own string tables (<c>catalog_names.json</c>, kinds
/// <c>levels</c> and <c>stashes</c>). What has no official name is described by its kind, never invented.
/// </summary>
internal static class PlaceNames
{
    private static readonly string[] Families = ["stalker-cop", "stalker-cs", "stalker-soc"];

    /// <summary>Object-name prefix → level ids, as each game spells them.</summary>
    private static readonly Dictionary<string, string[]> PrefixLevels = new(StringComparer.Ordinal)
    {
        ["esc"] = ["escape", "l01_escape"],
        ["gar"] = ["garbage", "l02_garbage"],
        ["agr"] = ["agroprom", "l03_agroprom"],
        ["agru"] = ["l03u_agr_underground"],
        ["val"] = ["darkvalley", "l04_darkvalley"],
        ["x18"] = ["l04u_labx18"],
        ["bar"] = ["l05_bar"],
        ["ros"] = ["l06_rostok"],
        ["mil"] = ["military", "l07_military"],
        ["yan"] = ["yantar", "l08_yantar"],
        ["rad"] = ["l10_radar"],
        ["mar"] = ["marsh"],
        ["red"] = ["red_forest"],
        ["lim"] = ["limansk"],
        ["hos"] = ["hospital"],
        ["katacomb"] = ["katacomb"],
        ["aes"] = ["stancia_2", "l12_stancia"],
        ["zat"] = ["zaton"],
        ["zaton"] = ["zaton"],
        ["jup"] = ["jupiter"],
        ["pas"] = ["jupiter_underground"],
        ["labx8"] = ["labx8"],
        ["pri"] = ["pripyat", "l11_pripyat"],
        ["pripyat"] = ["pripyat", "l11_pripyat"],
    };

    /// <summary>Levels one game has no name for although another names the same place.</summary>
    private static readonly Dictionary<string, string> SameLevel = new(StringComparer.Ordinal)
    {
        ["agroprom_underground"] = "l03u_agr_underground",
    };

    /// <summary>The player's own boxes in Call of Pripyat, by the base they stand in.</summary>
    private static readonly Dictionary<string, string> PersonalBoxes = new(StringComparer.Ordinal)
    {
        ["zat_a2_actor_treasure"] = "Личный ящик на «Скадовске»",
        ["jup_b202_actor_treasure"] = "Личный ящик на «Янове»",
        ["pri_a16_actor_treasure"] = "Личный ящик в прачечной",
    };

    /// <summary>The level's name in the interface language, or the id itself when the games have no name for it.</summary>
    public static string Level(string? releaseId, string? levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId)) return L.T("Неизвестно");
        var id = levelId.ToLowerInvariant();
        return Official(releaseId, "levels", id)
            ?? (SameLevel.TryGetValue(id, out var other) ? Official(releaseId, "levels", other) : null)
            ?? levelId;
    }

    /// <summary>The level an object belongs to, judged by the prefix of its name; null when the prefix says nothing.</summary>
    public static string? LevelOfObject(string? releaseId, string? objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        var prefix = objectName.Split('_', 2)[0].ToLowerInvariant();
        if (!PrefixLevels.TryGetValue(prefix, out var ids)) return null;
        foreach (var id in ids)
        {
            if (Official(releaseId, "levels", id) is { } name) return name;
        }

        return null;
    }

    /// <summary>A stash box: its official name, else what kind of box it is with the number from its name.</summary>
    public static string Stash(string? releaseId, string? objectName, ushort handle)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return L.T("Тайник 0x{0:X4}", handle);
        var name = objectName.ToLowerInvariant();
        if (Official(releaseId, "stashes", name) is { } official) return official;
        if (PersonalBoxes.TryGetValue(name, out var personal)) return L.T(personal);
        if (name.EndsWith("_actor_treasure", StringComparison.Ordinal)) return L.T("Личный ящик");
        var number = TrailingNumber(name);
        if (name.Contains("smart_terrain", StringComparison.Ordinal)) return L.T("Ящик лагеря {0}", CampNumber(name));
        if (name.Contains("_treasure", StringComparison.Ordinal) || name.Contains("_secret", StringComparison.Ordinal))
            return number is null ? L.T("Тайник") : L.T("Тайник № {0}", number);
        if (name.Contains("inventory_box", StringComparison.Ordinal) || name.Contains("inv_box", StringComparison.Ordinal))
            return number is null ? L.T("Ящик") : L.T("Ящик № {0}", number);
        return L.T("Контейнер «{0}»", objectName);
    }

    private static string? Official(string? releaseId, string kind, string key)
    {
        var language = SaveNaming.NamesLanguage;
        if (SaveNaming.OfficialNames.Resolve(releaseId, kind, key, language) is { } own) return own;
        // Mods bring levels of the other games; their names are the same places.
        foreach (var family in Families)
        {
            if (SaveNaming.OfficialNames.Resolve(family, kind, key, language) is { } other) return other;
        }

        return null;
    }

    private static string? TrailingNumber(string name)
    {
        var end = name.Length;
        while (end > 0 && char.IsAsciiDigit(name[end - 1])) end--;
        if (end == name.Length) return null;
        var digits = name[end..].TrimStart('0');
        // Level editors number unnamed copies from 0000: the first copy is box 1.
        return name[end - 1] == '_' && name.Length - end == 4
            ? (int.Parse(name[end..], System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : digits.Length == 0 ? "0" : digits;
    }

    /// <summary><c>esc_smart_terrain_5_7_box</c> → <c>5-7</c>, <c>red_smart_terrain_bridge_box</c> → <c>bridge</c>.</summary>
    private static string CampNumber(string name)
    {
        var start = name.IndexOf("smart_terrain_", StringComparison.Ordinal) + "smart_terrain_".Length;
        var rest = name[start..];
        if (rest.EndsWith("_box", StringComparison.Ordinal)) rest = rest[..^4];
        return rest.Replace('_', '-');
    }
}
