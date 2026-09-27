namespace UmbrellaWeaponRestrict.Core;

public enum WeaponGroup
{
    None,
    Pistols,
    Smgs,
    Shotguns,
    Rifles,
    Snipers,
    Grenades,
    Armor
}

public enum WeaponKind
{
    Weapon,
    Grenade,
    Kevlar,
    AssaultSuit
}

public sealed class WeaponDefinition
{
    public required int Index { get; init; }
    public required string Key { get; init; }
    public required ushort DefinitionIndex { get; init; }
    public required string DisplayName { get; init; }
    public required WeaponGroup Group { get; init; }
    public required WeaponKind Kind { get; init; }

    /// <summary>
    /// Grenades can be carried several at a time, so owning one never lets a player skip the team count.
    /// </summary>
    public bool IsStackable => Kind == WeaponKind.Grenade;
}

/// <summary>
/// Every item the plugin manages, keyed by its item definition index. CS2 reuses one entity class for
/// several weapons (the USP-S is a weapon_hkp2000, the M4A1-S a weapon_m4a1), so the definition index
/// is the only reliable way to tell them apart. Knives, the C4, the defuser and other utility items are
/// left out on purpose: restricting them breaks objectives or the round flow.
/// </summary>
public static class WeaponCatalog
{
    private static readonly List<WeaponDefinition> Definitions = [];
    private static readonly Dictionary<ushort, WeaponDefinition> ByDefinitionIndex = new();
    private static readonly Dictionary<string, WeaponDefinition> ByName = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<WeaponDefinition> All => Definitions;

    public static IReadOnlyList<WeaponGroup> Groups { get; } =
    [
        WeaponGroup.Pistols,
        WeaponGroup.Smgs,
        WeaponGroup.Shotguns,
        WeaponGroup.Rifles,
        WeaponGroup.Snipers,
        WeaponGroup.Grenades,
        WeaponGroup.Armor
    ];

    public static WeaponDefinition Kevlar { get; }
    public static WeaponDefinition AssaultSuit { get; }

    static WeaponCatalog()
    {
        Add("glock", 4, "Glock-18", WeaponGroup.Pistols, WeaponKind.Weapon, "glock18");
        Add("hkp2000", 32, "P2000", WeaponGroup.Pistols, WeaponKind.Weapon, "p2000");
        Add("usp_silencer", 61, "USP-S", WeaponGroup.Pistols, WeaponKind.Weapon, "usp", "usps", "usp_s");
        Add("elite", 2, "Dual Berettas", WeaponGroup.Pistols, WeaponKind.Weapon, "dualies", "berettas");
        Add("p250", 36, "P250", WeaponGroup.Pistols, WeaponKind.Weapon);
        Add("tec9", 30, "Tec-9", WeaponGroup.Pistols, WeaponKind.Weapon);
        Add("fiveseven", 3, "Five-SeveN", WeaponGroup.Pistols, WeaponKind.Weapon, "five_seven");
        Add("cz75a", 63, "CZ75-Auto", WeaponGroup.Pistols, WeaponKind.Weapon, "cz75", "cz");
        Add("deagle", 1, "Desert Eagle", WeaponGroup.Pistols, WeaponKind.Weapon, "desert_eagle");
        Add("revolver", 64, "R8 Revolver", WeaponGroup.Pistols, WeaponKind.Weapon, "r8");

        Add("mac10", 17, "MAC-10", WeaponGroup.Smgs, WeaponKind.Weapon);
        Add("mp9", 34, "MP9", WeaponGroup.Smgs, WeaponKind.Weapon);
        Add("mp7", 33, "MP7", WeaponGroup.Smgs, WeaponKind.Weapon);
        Add("mp5sd", 23, "MP5-SD", WeaponGroup.Smgs, WeaponKind.Weapon, "mp5");
        Add("ump45", 24, "UMP-45", WeaponGroup.Smgs, WeaponKind.Weapon, "ump");
        Add("p90", 19, "P90", WeaponGroup.Smgs, WeaponKind.Weapon);
        Add("bizon", 26, "PP-Bizon", WeaponGroup.Smgs, WeaponKind.Weapon, "ppbizon");

        Add("nova", 35, "Nova", WeaponGroup.Shotguns, WeaponKind.Weapon);
        Add("xm1014", 25, "XM1014", WeaponGroup.Shotguns, WeaponKind.Weapon, "xm");
        Add("sawedoff", 29, "Sawed-Off", WeaponGroup.Shotguns, WeaponKind.Weapon, "sawed_off");
        Add("mag7", 27, "MAG-7", WeaponGroup.Shotguns, WeaponKind.Weapon);

        Add("galilar", 13, "Galil AR", WeaponGroup.Rifles, WeaponKind.Weapon, "galil");
        Add("famas", 10, "FAMAS", WeaponGroup.Rifles, WeaponKind.Weapon);
        Add("ak47", 7, "AK-47", WeaponGroup.Rifles, WeaponKind.Weapon, "ak");
        Add("m4a1", 16, "M4A4", WeaponGroup.Rifles, WeaponKind.Weapon, "m4a4");
        Add("m4a1_silencer", 60, "M4A1-S", WeaponGroup.Rifles, WeaponKind.Weapon, "m4a1s", "m4a1_s");
        Add("sg556", 39, "SG 553", WeaponGroup.Rifles, WeaponKind.Weapon, "sg553");
        Add("aug", 8, "AUG", WeaponGroup.Rifles, WeaponKind.Weapon);
        Add("m249", 14, "M249", WeaponGroup.Rifles, WeaponKind.Weapon);
        Add("negev", 28, "Negev", WeaponGroup.Rifles, WeaponKind.Weapon);

        Add("ssg08", 40, "SSG 08", WeaponGroup.Snipers, WeaponKind.Weapon, "scout");
        Add("awp", 9, "AWP", WeaponGroup.Snipers, WeaponKind.Weapon);
        Add("g3sg1", 11, "G3SG1", WeaponGroup.Snipers, WeaponKind.Weapon);
        Add("scar20", 38, "SCAR-20", WeaponGroup.Snipers, WeaponKind.Weapon);

        Add("flashbang", 43, "Flashbang", WeaponGroup.Grenades, WeaponKind.Grenade, "flash");
        Add("hegrenade", 44, "HE Grenade", WeaponGroup.Grenades, WeaponKind.Grenade, "he");
        Add("smokegrenade", 45, "Smoke Grenade", WeaponGroup.Grenades, WeaponKind.Grenade, "smoke");
        Add("molotov", 46, "Molotov", WeaponGroup.Grenades, WeaponKind.Grenade, "molly");
        Add("incgrenade", 48, "Incendiary Grenade", WeaponGroup.Grenades, WeaponKind.Grenade, "incendiary", "inc");
        Add("decoy", 47, "Decoy Grenade", WeaponGroup.Grenades, WeaponKind.Grenade);

        Kevlar = Add("kevlar", 50, "Kevlar Vest", WeaponGroup.Armor, WeaponKind.Kevlar, "vest");
        AssaultSuit = Add("assaultsuit", 51, "Kevlar + Helmet", WeaponGroup.Armor, WeaponKind.AssaultSuit, "vesthelm");

        Add("taser", 31, "Zeus x27", WeaponGroup.None, WeaponKind.Weapon, "zeus");
    }

    public static WeaponDefinition? FindByDefinitionIndex(ushort definitionIndex)
    {
        return ByDefinitionIndex.GetValueOrDefault(definitionIndex);
    }

    /// <summary>
    /// Resolves a key, alias or entity class name ("awp", "m4a1s", "weapon_ak47", "item_kevlar").
    /// </summary>
    public static WeaponDefinition? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string name = text.Trim();
        if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["weapon_".Length..];
        }
        else if (name.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["item_".Length..];
        }

        return ByName.GetValueOrDefault(name);
    }

    public static bool TryParseGroup(string text, out WeaponGroup group)
    {
        group = text.Trim().ToLowerInvariant() switch
        {
            "pistol" or "pistols" => WeaponGroup.Pistols,
            "smg" or "smgs" => WeaponGroup.Smgs,
            "shotgun" or "shotguns" => WeaponGroup.Shotguns,
            "rifle" or "rifles" => WeaponGroup.Rifles,
            "sniper" or "snipers" => WeaponGroup.Snipers,
            "grenade" or "grenades" => WeaponGroup.Grenades,
            "armor" or "armour" => WeaponGroup.Armor,
            _ => WeaponGroup.None
        };

        return group != WeaponGroup.None;
    }

    public static IEnumerable<WeaponDefinition> InGroup(WeaponGroup group)
    {
        return Definitions.Where(weapon => weapon.Group == group);
    }

    private static WeaponDefinition Add(string key, ushort definitionIndex, string displayName, WeaponGroup group, WeaponKind kind, params string[] aliases)
    {
        WeaponDefinition definition = new()
        {
            Index = Definitions.Count,
            Key = key,
            DefinitionIndex = definitionIndex,
            DisplayName = displayName,
            Group = group,
            Kind = kind
        };

        Definitions.Add(definition);
        ByDefinitionIndex.Add(definitionIndex, definition);
        ByName.Add(key, definition);
        foreach (string alias in aliases)
        {
            ByName.Add(alias, definition);
        }

        return definition;
    }
}
