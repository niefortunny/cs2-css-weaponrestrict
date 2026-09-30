using System.Globalization;

namespace UmbrellaWeaponRestrict.Core;

/// <summary>
/// Limits that depend on how many players are on the server. A rule switches to a new limit once the
/// player count reaches each of its thresholds:
/// <code>"awp": { "default": 0, "10": 1, "20": 2, "immunity": 10 }</code>
/// blocks the AWP below 10 players, allows one from 10 and two from 20. Without "default", the weapon's
/// Restrictions entry applies below the first threshold. "immunity" holds back AdminImmunity and
/// WeaponImmunity for the weapon until that many players are counted.
/// </summary>
public sealed class PerPlayerRules
{
    public const int MaxPlayerCount = 64;

    private readonly Rule?[] _rules = new Rule?[WeaponCatalog.All.Count];

    public static PerPlayerRules Empty { get; } = new();

    public bool IsEmpty { get; private set; } = true;

    public static PerPlayerRules Parse(Dictionary<string, Dictionary<string, int>> rules, ICollection<string> warnings)
    {
        PerPlayerRules result = new();
        foreach ((string weaponText, Dictionary<string, int>? values) in rules)
        {
            WeaponDefinition? weapon = WeaponCatalog.Find(weaponText);
            if (weapon == null)
            {
                warnings.Add($"PerPlayer.Rules: '{weaponText}' is not a managed weapon, rule ignored.");
                continue;
            }

            if (result._rules[weapon.Index] != null)
            {
                warnings.Add($"PerPlayer.Rules: '{weaponText}' repeats the rule for {weapon.Key}, keeping the first one.");
                continue;
            }

            int? defaultLimit = null;
            int? immunityFrom = null;
            SortedDictionary<int, int> steps = new();
            foreach ((string key, int value) in values ?? [])
            {
                string name = $"PerPlayer.Rules.{weaponText}.{key}";
                if (key.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    defaultLimit = RestrictionState.Clamp(value, name, warnings);
                }
                else if (key.Trim().Equals("immunity", StringComparison.OrdinalIgnoreCase))
                {
                    if (value is >= 0 and <= MaxPlayerCount)
                    {
                        immunityFrom = value;
                    }
                    else
                    {
                        warnings.Add($"{name}: {value} is not a player count from 0 to {MaxPlayerCount}, ignored.");
                    }
                }
                else if (int.TryParse(key.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int threshold) && threshold <= MaxPlayerCount)
                {
                    steps[threshold] = RestrictionState.Clamp(value, name, warnings);
                }
                else
                {
                    warnings.Add($"{name}: the key must be \"default\", \"immunity\" or a player count from 0 to {MaxPlayerCount}, ignored.");
                }
            }

            if (defaultLimit == null && steps.Count == 0 && immunityFrom == null)
            {
                warnings.Add($"PerPlayer.Rules: '{weaponText}' has no limits or immunity, rule ignored.");
                continue;
            }

            result._rules[weapon.Index] = new Rule(defaultLimit, steps.Select(step => (step.Key, step.Value)).ToArray(), immunityFrom);
            result.IsEmpty = false;
        }

        return result;
    }

    /// <summary>
    /// The rule's limit for this player count, or null when the weapon has no rule or the count is below
    /// the first threshold of a rule without "default".
    /// </summary>
    public int? GetLimit(WeaponDefinition weapon, int playerCount)
    {
        Rule? rule = _rules[weapon.Index];
        if (rule == null)
        {
            return null;
        }

        int? limit = rule.Default;
        foreach ((int threshold, int value) in rule.Steps)
        {
            if (playerCount < threshold)
            {
                break;
            }

            limit = value;
        }

        return limit;
    }

    /// <summary>True while the player count is below the rule's "immunity" threshold.</summary>
    public bool HoldsImmunity(WeaponDefinition weapon, int playerCount)
    {
        return _rules[weapon.Index]?.ImmunityFrom is int from && playerCount < from;
    }

    private sealed record Rule(int? Default, (int Threshold, int Limit)[] Steps, int? ImmunityFrom);
}
