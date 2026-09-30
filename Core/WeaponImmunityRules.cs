namespace UmbrellaWeaponRestrict.Core;

/// <summary>
/// Permissions that exempt their holders from the limits of single weapons or whole groups:
/// <code>"awp": "@css/vip"</code>
/// lets VIPs buy and pick up the AWP whatever its limit, without taking the team's AWP slots. Every
/// other limit still applies to them.
/// </summary>
public sealed class WeaponImmunityRules
{
    private readonly string[][] _permissions =
        Enumerable.Repeat(Array.Empty<string>(), WeaponCatalog.All.Count).ToArray();

    public static WeaponImmunityRules Empty { get; } = new();

    public bool IsEmpty { get; private set; } = true;

    public static WeaponImmunityRules Parse(Dictionary<string, string> entries, ICollection<string> warnings)
    {
        WeaponImmunityRules result = new();
        foreach ((string key, string? value) in entries)
        {
            IEnumerable<WeaponDefinition>? weapons = WeaponCatalog.Find(key) is { } weapon
                ? [weapon]
                : WeaponCatalog.TryParseGroup(key, out WeaponGroup group) ? WeaponCatalog.InGroup(group) : null;
            if (weapons == null)
            {
                warnings.Add($"WeaponImmunity: '{key}' is not a managed weapon or group, entry ignored.");
                continue;
            }

            string permission = value?.Trim() ?? string.Empty;
            if (permission.Length < 2 || permission[0] is not ('@' or '#'))
            {
                warnings.Add($"WeaponImmunity.{key}: '{value}' is not a permission flag (@domain/flag) or group (#group), entry ignored.");
                continue;
            }

            foreach (WeaponDefinition covered in weapons)
            {
                string[] current = result._permissions[covered.Index];
                if (!current.Contains(permission, StringComparer.Ordinal))
                {
                    result._permissions[covered.Index] = [.. current, permission];
                }
            }

            result.IsEmpty = false;
        }

        return result;
    }

    /// <summary>Permissions exempt from this weapon's limits; empty when none.</summary>
    public ReadOnlySpan<string> Get(WeaponDefinition weapon)
    {
        return _permissions[weapon.Index];
    }
}
