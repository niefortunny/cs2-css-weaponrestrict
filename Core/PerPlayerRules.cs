using System.Globalization;

namespace UmbrellaWeaponRestrict.Core;

/// <summary>
/// Limits that depend on how many players are on the server. A rule switches to a new limit once the
/// player count reaches each of its thresholds:
/// <code>"awp": { "default": 0, "10": 1, "20": 2 }</code>
/// blocks the AWP below 10 players, allows one from 10 and two from 20. Without "default", the weapon's
/// Restrictions entry applies below the first threshold.
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
            SortedDictionary<int, int> steps = new();
            foreach ((string key, int value) in values ?? [])
            {
                string name = $"PerPlayer.Rules.{weaponText}.{key}";
                if (key.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    defaultLimit = RestrictionState.Clamp(value, name, warnings);
                }
                else if (int.TryParse(key.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int threshold) && threshold <= MaxPlayerCount)
                {
                    steps[threshold] = RestrictionState.Clamp(value, name, warnings);
                }
                else
                {
                    warnings.Add($"{name}: the key must be \"default\" or a player count from 0 to {MaxPlayerCount}, ignored.");
                }
            }

            if (defaultLimit == null && steps.Count == 0)
            {
                warnings.Add($"PerPlayer.Rules: '{weaponText}' has no limits, rule ignored.");
                continue;
            }

            result._rules[weapon.Index] = new Rule(defaultLimit, steps.Select(step => (step.Key, step.Value)).ToArray());
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

    private sealed record Rule(int? Default, (int Threshold, int Limit)[] Steps);
}
