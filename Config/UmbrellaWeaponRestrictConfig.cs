using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using UmbrellaWeaponRestrict.Core;

namespace UmbrellaWeaponRestrict.Config;

public sealed class UmbrellaWeaponRestrictConfig : BasePluginConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("AllowRestrictedPickup")]
    public bool AllowRestrictedPickup { get; set; } = false;

    [JsonPropertyName("Notify")]
    public bool Notify { get; set; } = true;

    [JsonPropertyName("RestrictedSound")]
    public string RestrictedSound { get; set; } = "sounds/ui/weapon_cant_buy.vsnd_c";

    [JsonPropertyName("AnnounceChanges")]
    public bool AnnounceChanges { get; set; } = true;

    [JsonPropertyName("AdminPermission")]
    public string AdminPermission { get; set; } = "@css/cvar";

    [JsonPropertyName("AdminImmunity")]
    public bool AdminImmunity { get; set; } = false;

    [JsonPropertyName("ImmunityPermission")]
    public string ImmunityPermission { get; set; } = "@css/root";

    [JsonPropertyName("WeaponImmunity")]
    public Dictionary<string, string> WeaponImmunity { get; set; } = new();

    [JsonPropertyName("Restrictions")]
    public Dictionary<string, TeamLimitSettings> Restrictions { get; set; } =
        WeaponCatalog.All.ToDictionary(weapon => weapon.Key, _ => new TeamLimitSettings());

    [JsonPropertyName("Maps")]
    public Dictionary<string, Dictionary<string, TeamLimitSettings>> Maps { get; set; } = new();

    [JsonPropertyName("PerPlayer")]
    public PerPlayerSettings PerPlayer { get; set; } = new();

    [JsonPropertyName("Menu")]
    public MenuSettings Menu { get; set; } = new();

    [JsonPropertyName("Debug")]
    public bool Debug { get; set; } = false;

    /// <summary>A team left out of the JSON is null: unrestricted in Restrictions, unchanged in Maps.</summary>
    [JsonConverter(typeof(TeamLimitSettingsConverter))]
    public sealed class TeamLimitSettings
    {
        public int? T { get; set; } = RestrictionState.Unlimited;
        public int? CT { get; set; } = RestrictionState.Unlimited;
    }

    public sealed class PerPlayerSettings
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("CountBots")]
        public bool CountBots { get; set; } = true;

        [JsonPropertyName("CountSpectators")]
        public bool CountSpectators { get; set; } = true;

        [JsonPropertyName("Rules")]
        public Dictionary<string, Dictionary<string, int>> Rules { get; set; } = new();
    }

    public sealed class MenuSettings
    {
        [JsonPropertyName("FreezePlayer")]
        public bool FreezePlayer { get; set; } = true;

        [JsonPropertyName("VisibleItems")]
        public int VisibleItems { get; set; } = 5;

        [JsonPropertyName("InputDebounceMilliseconds")]
        public int InputDebounceMilliseconds { get; set; } = 140;

        [JsonPropertyName("InitialInputDelayMilliseconds")]
        public int InitialInputDelayMilliseconds { get; set; } = 500;

        [JsonPropertyName("ScrollUpKeys")]
        public List<string> ScrollUpKeys { get; set; } = ["W"];

        [JsonPropertyName("ScrollDownKeys")]
        public List<string> ScrollDownKeys { get; set; } = ["S"];

        [JsonPropertyName("SelectKeys")]
        public List<string> SelectKeys { get; set; } = ["E"];

        [JsonPropertyName("BackKeys")]
        public List<string> BackKeys { get; set; } = ["Shift", "A"];

        [JsonPropertyName("CancelKeys")]
        public List<string> CancelKeys { get; set; } = ["R"];
    }
}
