using System.Text;
using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Core.Companion;

/// <summary>
/// The companion's spawn list (<c>save_editor_catalog.script</c>) written from the installed game's own
/// configs, so items added by mods appear and sections the engine cannot spawn as inventory items
/// (abstract bases, first-person models, multiplayer copies, mounted weapons, helicopter parts,
/// scripted explosives) stay out.
/// </summary>
public static class CompanionCatalogScript
{
    public const string RelativePath = "scripts/save_editor_catalog.script";

    private static readonly string[] Order = ["weapon", "ammo", "outfit", "artifact", "grenade", "consumable", "item"];

    public static bool IsSpawnable(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var key = item.Key.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(item.DisplayName)) return false; // no inv_name: not an inventory item
        return !(key.StartsWith("mp_", StringComparison.Ordinal)
                 || key.EndsWith("_hud", StringComparison.Ordinal)
                 || key.EndsWith("_base", StringComparison.Ordinal)
                 || key.EndsWith("_heli", StringComparison.Ordinal)
                 || key.Contains("probability", StringComparison.Ordinal)
                 || key.StartsWith("stationary", StringComparison.Ordinal)
                 || key.StartsWith("helicopter", StringComparison.Ordinal)
                 || key.StartsWith("wpn_fake", StringComparison.Ordinal)
                 || key.StartsWith("explosive_", StringComparison.Ordinal)
                 || key.StartsWith("up_", StringComparison.Ordinal));
    }

    public static string Render(ItemCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var groups = Order.ToDictionary(name => name, _ => new SortedSet<string>(StringComparer.Ordinal));
        foreach (var item in catalog.Items.Where(IsSpawnable))
        {
            var category = item.Category is null or "device" ? "item" : item.Category;
            groups[groups.ContainsKey(category) ? category : "item"].Add(item.Key);
        }

        var builder = new StringBuilder();
        builder.Append("-- Written by the save editor from this game's own configs (mods included).\n-- Do not edit by hand.\nitems = {\n");
        foreach (var name in Order)
        {
            builder.Append('\t').Append(name).Append(" = { ")
                .AppendJoin(", ", groups[name].Select(key => "\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""))
                .Append(" },\n");
        }

        return builder.Append("}\n").ToString();
    }
}
