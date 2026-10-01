using System.Globalization;
using System.Text;
using CoreSettings;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using MenuManager;
using Microsoft.Extensions.Logging;
using UmbrellaWeaponRestrict.Config;
using UmbrellaWeaponRestrict.Core;
using UmbrellaWeaponRestrict.Extensions;

namespace UmbrellaWeaponRestrict;

[MinimumApiVersion(376)]
public sealed partial class UmbrellaWeaponRestrictPlugin : BasePlugin, IPluginConfig<UmbrellaWeaponRestrictConfig>
{
    private const string ConsolePrefix = "[UWR]";
    private const float NotifyDelaySeconds = 1.5f;
    private const float PickupRecheckSeconds = 0.25f;
    private const float JustCreatedSeconds = 0.05f;
    private const double ChatLimitTimeoutSeconds = 30;
    private const int MaxSlots = 64;
    private const int MaxListLineLength = 90;

    public override string ModuleName => "Umbrella Weapon Restrict";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "Ayrton09";
    public override string ModuleDescription => "Weapon restrictions by team, weapon group and player count";

    public static string Prefix = string.Empty;
    public static string PrefixRed = string.Empty;

    private ICoreSettingsApi? _coreSettings;
    private IMenuApi? _menuApi;
    private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");

    public UmbrellaWeaponRestrictConfig Config { get; set; } = new();

    private readonly RestrictionState _limits = new();
    private readonly float[] _nextNotify = new float[MaxSlots];
    private readonly nint[] _lastPickupNotify = new nint[MaxSlots];
    private readonly Dictionary<nint, int> _slotByItemServices = new();
    private readonly Dictionary<nint, (nint Item, float Until)> _refusedPickups = new();
    private readonly Dictionary<int, PendingChatLimit> _pendingChatLimits = new();
    private readonly bool[] _immunityHeld = new bool[WeaponCatalog.All.Count];
    private TeamLimit[] _configLimits = CreateUnrestrictedLimits();
    private PerPlayerRules _perPlayerRules = PerPlayerRules.Empty;
    private WeaponImmunityRules _weaponImmunity = WeaponImmunityRules.Empty;
    private MapRules _mapRules = MapRules.Empty;
    private bool _canAcquireHooked;
    private bool _enforceQueued;
    private bool _mapStarted;
    private int _lastPerPlayerCount = -1;
    private DateTime _lastHookErrorUtc = DateTime.MinValue;
    private int _itemOffsetInWeapon = -1;

    public void OnConfigParsed(UmbrellaWeaponRestrictConfig config)
    {
        List<string> warnings = [];

        config.Restrictions ??= new Dictionary<string, UmbrellaWeaponRestrictConfig.TeamLimitSettings>();
        config.WeaponImmunity ??= new Dictionary<string, string>();
        config.Maps ??= new Dictionary<string, Dictionary<string, UmbrellaWeaponRestrictConfig.TeamLimitSettings>>();
        config.PerPlayer ??= new UmbrellaWeaponRestrictConfig.PerPlayerSettings();
        config.PerPlayer.Rules ??= new Dictionary<string, Dictionary<string, int>>();

        config.AdminPermission = NormalizePermission(config.AdminPermission, "@css/cvar", "AdminPermission", warnings);
        config.ImmunityPermission = NormalizePermission(config.ImmunityPermission, "@css/root", "ImmunityPermission", warnings);
        config.RestrictedSound = NormalizeSoundPath(config.RestrictedSound, warnings);

        TeamLimit[] limits = CreateUnrestrictedLimits();
        HashSet<int> seen = [];
        foreach ((string key, UmbrellaWeaponRestrictConfig.TeamLimitSettings? value) in config.Restrictions)
        {
            WeaponDefinition? weapon = WeaponCatalog.Find(key);
            if (weapon == null)
            {
                warnings.Add($"Restrictions: '{key}' is not a managed weapon, entry ignored.");
            }
            else if (value != null && !seen.Add(weapon.Index))
            {
                warnings.Add($"Restrictions: '{key}' repeats the entry for {weapon.Key}, keeping the first one.");
            }
            else if (value != null)
            {
                limits[weapon.Index] = new TeamLimit(
                    RestrictionState.Clamp(value.T ?? RestrictionState.Unlimited, $"Restrictions.{key}.T", warnings),
                    RestrictionState.Clamp(value.CT ?? RestrictionState.Unlimited, $"Restrictions.{key}.CT", warnings));
            }
        }

        MapRules mapRules = MapRules.Parse(ReadMapLimits(config.Maps), warnings);
        WeaponImmunityRules weaponImmunity = WeaponImmunityRules.Parse(config.WeaponImmunity, warnings);
        PerPlayerRules perPlayerRules = PerPlayerRules.Parse(config.PerPlayer.Rules, warnings);
        if (config.PerPlayer.Enabled && perPlayerRules.IsEmpty)
        {
            warnings.Add("PerPlayer.Enabled is true but PerPlayer.Rules has no valid rule.");
        }

        Config = config;
        _configLimits = limits;
        _mapRules = mapRules;
        _weaponImmunity = weaponImmunity;
        _perPlayerRules = perPlayerRules;

        foreach (string warning in warnings)
        {
            Logger.LogWarning("{Warning}", warning);
        }
    }

    public override void Load(bool hotReload)
    {
        _limits.ResetBase(_configLimits);

        AddCommand("css_restrict", "Restrict a weapon, weapon group or all weapons.", OnRestrictCommand);
        AddCommand("css_unrestrict", "Remove the restriction of a weapon, weapon group or all weapons.", OnUnrestrictCommand);
        AddCommand("css_restrictmenu", "Open the weapon restriction menu.", OnMenuCommand);
        AddCommand("css_wrmenu", "Open the weapon restriction menu.", OnMenuCommand);
        AddCommand("css_remove_restricted", "Remove weapons that exceed the current limits.", OnRemoveRestrictedCommand);
        AddCommand("css_wr_list", "List the active weapon restrictions.", OnListCommand);
        AddCommand("css_restrictions", "Show the weapon limits of your team.", OnRestrictionsCommand);
        AddCommand("css_wr_reload", "Reload the weapon restriction config.", OnReloadCommand);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnectPost);

        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

        // CounterStrikeSharp keys listeners by delegate: sharing one would leave "say" registered
        // after an unload.
        AddCommandListener("say", (player, info) => OnSay(player, info), HookMode.Pre);
        AddCommandListener("say_team", (player, info) => OnSay(player, info), HookMode.Pre);

        HookCanAcquire();

        // Loaded mid-map (css_plugins load or a hot reload): OnMapStart will not run for this map.
        Server.NextFrame(() =>
        {
            if (!_mapStarted && !string.IsNullOrEmpty(Server.MapName))
            {
                ResetToConfig(Server.MapName);
                RefreshPerPlayer(force: true);
                QueueEnforce();
            }
        });
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _coreSettings = CoreSettingsPlugin.ApiCapability.Get();
        Prefix = _coreSettings?.Get("prefix") ?? string.Empty;
        PrefixRed = _coreSettings?.Get("prefix_red") ?? string.Empty;

        _menuApi = _menuCapability.Get();
        if (_menuApi == null)
        {
            Logger.LogError("[UmbrellaWeaponRestrict] MenuManager Core not found...");
        }
    }

    public override void Unload(bool hotReload)
    {
        if (_canAcquireHooked)
        {
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnCanAcquire, HookMode.Pre);
            _canAcquireHooked = false;
        }
    }

    private void HookCanAcquire()
    {
        try
        {
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnCanAcquire, HookMode.Pre);
            _canAcquireHooked = true;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not hook CCSPlayer_ItemServices::CanAcquire. Buys and pickups will not be blocked; limits are still enforced when they change and at round start.");
        }
    }

    private void OnMapStart(string mapName)
    {
        // A queued enforcement may not survive the map change; it must not keep blocking new ones.
        _enforceQueued = false;
        _mapStarted = true;
        _slotByItemServices.Clear();
        _refusedPickups.Clear();
        _pendingChatLimits.Clear();
        Array.Clear(_nextNotify);
        Array.Clear(_lastPickupNotify);

        // Every map starts from the config file; admin changes only last until the map changes.
        ResetToConfig(mapName);
        RefreshPerPlayer(force: true);
    }

    /// <summary>Restrictions from the config file, with the Maps entries for this map on top.</summary>
    private void ResetToConfig(string mapName)
    {
        _limits.ResetBase(_mapRules.Apply(_configLimits, mapName));

        // Names the map as the server reports it, which is what Maps entries are matched against.
        if (!_mapRules.IsEmpty)
        {
            string[] entries = _mapRules.Matching(mapName).ToArray();
            Logger.LogInformation("Maps entries for {Map}: {Entries}.", mapName, entries.Length > 0 ? string.Join(", ", entries) : "none");
        }
    }

    private void OnClientPutInServer(int slot)
    {
        ResetSlot(slot);
        Server.NextFrame(() => RefreshPerPlayer());
    }

    private void OnClientDisconnect(int slot)
    {
        ResetSlot(slot);
    }

    private void OnClientDisconnectPost(int slot)
    {
        Server.NextFrame(() => RefreshPerPlayer());
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        Array.Clear(_lastPickupNotify);
        RefreshPerPlayer();
        QueueEnforce();
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        // The event fires before the new team is applied, so count on the next frame.
        Server.NextFrame(() => RefreshPerPlayer());
        return HookResult.Continue;
    }

    private void ResetSlot(int slot)
    {
        if (slot is < 0 or >= MaxSlots)
        {
            return;
        }

        _nextNotify[slot] = 0f;
        _lastPickupNotify[slot] = 0;
        _pendingChatLimits.Remove(slot);
        foreach (nint handle in _slotByItemServices.Where(entry => entry.Value == slot).Select(entry => entry.Key).ToList())
        {
            _slotByItemServices.Remove(handle);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Buy and pickup blocking
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CanAcquire runs before a buy charges any money and before a ground weapon is picked up, so
    /// refusing here blocks both without refunds or drops.
    /// </summary>
    private HookResult OnCanAcquire(DynamicHook hook)
    {
        if (!Config.Enabled)
        {
            return HookResult.Continue;
        }

        try
        {
            // Raw pointers: the typed GetParam overload builds its wrappers through reflection.
            nint itemPointer = hook.GetParam<nint>(1);
            if (itemPointer == 0)
            {
                return HookResult.Continue;
            }

            WeaponDefinition? weapon = WeaponCatalog.FindByDefinitionIndex(new CEconItemView(itemPointer).ItemDefinitionIndex);
            if (weapon == null || !_limits.IsRestrictedForAnyTeam(weapon))
            {
                return HookResult.Continue;
            }

            AcquireMethod method = (AcquireMethod)hook.GetParam<int>(2);
            bool pickup = method == AcquireMethod.PickUp;
            if (pickup && Config.AllowRestrictedPickup)
            {
                return HookResult.Continue;
            }

            // The game asks again every tick while a player stands on a ground weapon; reuse the
            // last refusal for a moment instead of recounting the whole team each time.
            nint services = hook.GetParam<nint>(0);
            if (pickup &&
                _refusedPickups.TryGetValue(services, out (nint Item, float Until) refused) &&
                refused.Item == itemPointer &&
                Server.CurrentTime < refused.Until)
            {
                hook.SetReturn(AcquireResult.NotAllowedByProhibition);
                return HookResult.Handled;
            }

            (CCSPlayerController? player, CCSPlayerPawn? pawn) = FindItemServicesOwner(services);
            if (player == null || pawn == null || !CanEvaluate(player) || IsImmune(player, weapon))
            {
                return HookResult.Continue;
            }

            CsTeam team = player.Team;
            if (IsAllowed(pawn, team, weapon, method))
            {
                return HookResult.Continue;
            }

            if (pickup)
            {
                _refusedPickups[services] = (itemPointer, Server.CurrentTime + PickupRecheckSeconds);
                RemoveIfJustCreated(itemPointer);
            }

            OnRefused(player, weapon, _limits.Get(weapon, team), method, pickup ? itemPointer : 0);
            hook.SetReturn(AcquireResult.NotAllowedByProhibition);
            return HookResult.Handled;
        }
        catch (Exception exception)
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastHookErrorUtc > TimeSpan.FromSeconds(30))
            {
                _lastHookErrorUtc = now;
                Logger.LogError(exception, "CanAcquire check failed; the item was allowed.");
            }

            return HookResult.Continue;
        }
    }

    /// <summary>
    /// Spawn loadouts and weapons handed out by other plugins are created right before this check, and
    /// the game drops a refused one at the player's feet. Those are deleted instead of piling up at the
    /// spawns; a weapon that was already on the ground stays where it is.
    /// </summary>
    private void RemoveIfJustCreated(nint itemPointer)
    {
        // A pickup's CEconItemView lives inside the weapon entity.
        if (_itemOffsetInWeapon < 0)
        {
            _itemOffsetInWeapon = Schema.GetSchemaOffset("CEconEntity", "m_AttributeManager") +
                                  Schema.GetSchemaOffset("CAttributeContainer", "m_Item");
        }

        CBasePlayerWeapon weapon = new(itemPointer - _itemOffsetInWeapon);
        if (Server.CurrentTime - weapon.CreateTime > JustCreatedSeconds)
        {
            return;
        }

        // The game is still inside the give; delete the weapon once it is done with it.
        CHandle<CBasePlayerWeapon> handle = new(weapon.EntityHandle.Raw);
        Server.NextFrame(() =>
        {
            CBasePlayerWeapon? dropped = handle.Value;
            if (dropped != null && dropped.IsValid && !dropped.OwnerEntity.IsValid)
            {
                dropped.Remove();
            }
        });
    }

    /// <summary>
    /// Finds the pawn that owns these item services and the player driving it. During a bot takeover
    /// the human drives the bot's pawn (m_hPawn) while the bot controller still owns it
    /// (m_hPlayerPawn); the driver is who gets the notice and whose immunity counts.
    /// </summary>
    private (CCSPlayerController? Player, CCSPlayerPawn? Pawn) FindItemServicesOwner(nint services)
    {
        if (services == 0)
        {
            return (null, null);
        }

        if (_slotByItemServices.TryGetValue(services, out int cachedSlot))
        {
            CCSPlayerController? cached = Utilities.GetPlayerFromSlot(cachedSlot);
            CCSPlayerPawn? cachedPawn = GetDrivenPawn(cached, services);
            if (cachedPawn != null)
            {
                return (cached, cachedPawn);
            }

            _slotByItemServices.Remove(services);
        }

        (CCSPlayerController? Player, CCSPlayerPawn? Pawn) owner = (null, null);
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            CCSPlayerPawn? driven = GetDrivenPawn(player, services);
            if (driven != null)
            {
                _slotByItemServices[services] = player.Slot;
                return (player, driven);
            }

            // Nobody drives a pawn that is still spawning, so fall back to the controller that owns it.
            CCSPlayerPawn? own = player.PlayerPawn.Value;
            if (owner.Player == null && own != null && own.IsValid && own.ItemServices?.Handle == services)
            {
                owner = (player, own);
            }
        }

        return owner;
    }

    private static CCSPlayerPawn? GetDrivenPawn(CCSPlayerController? player, nint services)
    {
        CBasePlayerPawn? driven = player is { IsValid: true } ? player.Pawn.Value : null;
        return driven != null && driven.IsValid && driven.ItemServices?.Handle == services
            ? new CCSPlayerPawn(driven.Handle)
            : null;
    }

    private static bool CanEvaluate(CCSPlayerController player)
    {
        return player.IsValid &&
               player.Connected == PlayerConnectedState.Connected &&
               player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    }

    /// <summary>AdminImmunity, which covers every weapon.</summary>
    private bool IsImmune(CCSPlayerController player)
    {
        return Config.AdminImmunity &&
               !player.IsBot &&
               HasPermission(player, Config.ImmunityPermission);
    }

    /// <summary>
    /// AdminImmunity, or a WeaponImmunity entry that covers this weapon, unless a per-player rule holds
    /// immunity back for it.
    /// </summary>
    private bool IsImmune(CCSPlayerController player, WeaponDefinition weapon)
    {
        return !_immunityHeld[weapon.Index] && (IsImmune(player) || HasWeaponImmunity(player, weapon));
    }

    private bool HasWeaponImmunity(CCSPlayerController player, WeaponDefinition weapon)
    {
        if (player.IsBot)
        {
            return false;
        }

        foreach (string permission in _weaponImmunity.Get(weapon))
        {
            if (HasPermission(player, permission))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Accepts a flag ("@css/cvar") or an admin group ("#css/admin"). CounterStrikeSharp checks each
    /// kind with its own call; the flag check alone would let any admin through for a group.
    /// </summary>
    private static bool HasPermission(CCSPlayerController? player, string permission)
    {
        return permission.StartsWith('#')
            ? AdminManager.PlayerInGroup(player, permission)
            : AdminManager.PlayerHasPermissions(player, permission);
    }

    private bool IsAllowed(CCSPlayerPawn pawn, CsTeam team, WeaponDefinition weapon, AcquireMethod method)
    {
        int limit = _limits.Get(weapon, team);
        if (limit < 0)
        {
            return true;
        }

        if (limit == 0)
        {
            return false;
        }

        // Swapping a weapon the player already owns does not change the team total.
        if (!weapon.IsStackable && method is AcquireMethod.Buy or AcquireMethod.PickUp && WeaponInventory.Count(pawn, weapon) > 0)
        {
            return true;
        }

        return CountTeamUnits(team, weapon) < limit;
    }

    private int CountTeamUnits(CsTeam team, WeaponDefinition weapon)
    {
        int count = 0;
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player.Team != team)
            {
                continue;
            }

            CCSPlayerPawn? pawn = WeaponInventory.GetAlivePawn(player);
            if (pawn != null && !IsImmune(GetDriver(player, pawn), weapon))
            {
                count += WeaponInventory.Count(pawn, weapon);
            }
        }

        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Enforcement
    // ---------------------------------------------------------------------------------------------

    private void QueueEnforce()
    {
        if (_enforceQueued)
        {
            return;
        }

        _enforceQueued = true;
        Server.NextFrame(() =>
        {
            _enforceQueued = false;
            EnforceRestrictions();
        });
    }

    /// <summary>
    /// Removes owned items over the current limits. Players are walked in slot order and the first
    /// ones keep their items, same as the SourceMod version.
    /// </summary>
    private void EnforceRestrictions()
    {
        if (!Config.Enabled)
        {
            return;
        }

        int[] tBudget = BuildBudget(CsTeam.Terrorist);
        int[] ctBudget = BuildBudget(CsTeam.CounterTerrorist);
        if (tBudget.All(limit => limit < 0) && ctBudget.All(limit => limit < 0))
        {
            return;
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!CanEvaluate(player))
            {
                continue;
            }

            CCSPlayerPawn? pawn = WeaponInventory.GetAlivePawn(player);
            if (pawn == null)
            {
                continue;
            }

            CCSPlayerController driver = GetDriver(player, pawn);
            try
            {
                WeaponInventory.Enforce(pawn, player.Team == CsTeam.Terrorist ? tBudget : ctBudget,
                    weapon => IsImmune(driver, weapon));
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Could not enforce the limits on {Player}.", player.PlayerName);
            }
        }
    }

    /// <summary>
    /// During a bot takeover the human drives the bot's pawn, so the human's immunity applies to what it
    /// carries, the same as for buys and pickups.
    /// </summary>
    private static CCSPlayerController GetDriver(CCSPlayerController owner, CCSPlayerPawn pawn)
    {
        // A bot's m_hPawn only leaves its own pawn while a human drives it.
        if (!owner.IsBot || owner.Pawn.Value?.Handle == pawn.Handle)
        {
            return owner;
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!player.IsBot && player.Pawn.Value?.Handle == pawn.Handle)
            {
                return player;
            }
        }

        return owner;
    }

    private int[] BuildBudget(CsTeam team)
    {
        int[] budget = new int[WeaponCatalog.All.Count];
        foreach (WeaponDefinition weapon in WeaponCatalog.All)
        {
            budget[weapon.Index] = _limits.Get(weapon, team);
        }

        return budget;
    }

    // ---------------------------------------------------------------------------------------------
    // Per-player limits
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A stricter per-player limit blocks new buys and pickups at once but only takes weapons away at
    /// the next round start, so nobody loses a weapon mid-round because someone left.
    /// </summary>
    private void RefreshPerPlayer(bool force = false)
    {
        if (!Config.PerPlayer.Enabled || _perPlayerRules.IsEmpty)
        {
            _limits.ClearPerPlayer();
            Array.Clear(_immunityHeld);
            _lastPerPlayerCount = -1;
            return;
        }

        int count = CountPlayersForRules();
        if (!force && count == _lastPerPlayerCount)
        {
            return;
        }

        _lastPerPlayerCount = count;
        foreach (WeaponDefinition weapon in WeaponCatalog.All)
        {
            _limits.SetPerPlayer(weapon, _perPlayerRules.GetLimit(weapon, count));
            _immunityHeld[weapon.Index] = _perPlayerRules.HoldsImmunity(weapon, count);
        }

        if (Config.Debug)
        {
            Logger.LogInformation("Per-player limits refreshed for {Count} player(s).", count);
        }
    }

    private int CountPlayersForRules()
    {
        int count = 0;
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player.IsHLTV ||
                (!Config.PerPlayer.CountBots && player.IsBot) ||
                (!Config.PerPlayer.CountSpectators && player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Notifications
    // ---------------------------------------------------------------------------------------------

    private void OnRefused(CCSPlayerController player, WeaponDefinition weapon, int limit, AcquireMethod method, nint pickupKey)
    {
        int slot = player.Slot;
        if (slot is < 0 or >= MaxSlots)
        {
            return;
        }

        if (pickupKey != 0)
        {
            // Standing on a weapon asks again every tick; report it once per weapon.
            if (_lastPickupNotify[slot] == pickupKey)
            {
                return;
            }

            _lastPickupNotify[slot] = pickupKey;
        }
        else
        {
            float now = Server.CurrentTime;
            if (now < _nextNotify[slot])
            {
                return;
            }

            _nextNotify[slot] = now + NotifyDelaySeconds;
        }

        if (Config.Debug)
        {
            Logger.LogInformation("Refused {Method} of {Weapon} for {Player} ({Team}, limit {Limit}).", method, weapon.Key, player.PlayerName, player.Team, limit);
        }

        if (player.IsBot)
        {
            return;
        }

        if (Config.RestrictedSound.Length > 0)
        {
            player.ExecuteClientCommand($"play {Config.RestrictedSound}");
        }

        if (Config.Notify)
        {
            PrintToChat(player, limit == 0
                ? Localizer.ForPlayer(player, "notify.blocked", weapon.DisplayName)
                : Localizer.ForPlayer(player, "notify.limit", weapon.DisplayName, limit),
                isWarning: true);
        }
    }

    private static void PrintToChat(CCSPlayerController player, string message, bool isWarning = false)
    {
        player.PrintToChatWithSound(message, isWarning: isWarning);
    }

    private void Reply(CCSPlayerController? player, CommandInfo info, string message)
    {
        if (player != null && info.CallingContext == CommandCallingContext.Chat)
        {
            PrintToChat(player, message);
            return;
        }

        info.ReplyToCommand($"{ConsolePrefix} {StripColors(message)}");
    }

    private void ReplyKey(CCSPlayerController? player, CommandInfo info, string key, params object[] args)
    {
        Reply(player, info, Localizer.ForPlayer(player, key, args));
    }

    private static string StripColors(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char character in text)
        {
            if (character >= ' ')
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }

    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    private bool HasAdminAccess(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || HasPermission(player, Config.AdminPermission))
        {
            return true;
        }

        ReplyKey(player, info, "command.no_permission");
        return false;
    }

    private void OnRestrictCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasAdminAccess(player, info))
        {
            return;
        }

        int args = info.ArgCount - 1;
        if (args is < 1 or > 3)
        {
            ReplyKey(player, info, "command.restrict_usage");
            return;
        }

        int amount = 0;
        if (args >= 2 && !TryParseLimit(info.GetArg(2), out amount))
        {
            ReplyKey(player, info, "command.invalid_amount");
            return;
        }

        RestrictTeam team = RestrictTeam.Both;
        if (args == 3 && !TryParseTeam(info.GetArg(3), out team))
        {
            ReplyKey(player, info, "command.invalid_team");
            return;
        }

        ApplyRestrictionCommand(player, info, info.GetArg(1), team, amount);
    }

    private void OnUnrestrictCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasAdminAccess(player, info))
        {
            return;
        }

        int args = info.ArgCount - 1;
        if (args is < 1 or > 2)
        {
            ReplyKey(player, info, "command.unrestrict_usage");
            return;
        }

        RestrictTeam team = RestrictTeam.Both;
        if (args == 2 && !TryParseTeam(info.GetArg(2), out team))
        {
            ReplyKey(player, info, "command.invalid_team");
            return;
        }

        ApplyRestrictionCommand(player, info, info.GetArg(1), team, RestrictionState.Unlimited);
    }

    private void ApplyRestrictionCommand(CCSPlayerController? player, CommandInfo info, string target, RestrictTeam team, int amount)
    {
        string teamName = GetTeamName(player, team);
        List<WeaponDefinition> weapons;
        string message;
        Func<CCSPlayerController, string> subjectName;

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase) || target.Equals("@all", StringComparison.OrdinalIgnoreCase))
        {
            weapons = WeaponCatalog.All.ToList();
            message = Localizer.ForPlayer(player, "restrict.all", teamName, amount);
            subjectName = reader => Localizer.ForPlayer(reader, "announce.all_weapons");
        }
        else if (WeaponCatalog.TryParseGroup(target, out WeaponGroup group))
        {
            weapons = WeaponCatalog.InGroup(group).ToList();
            message = Localizer.ForPlayer(player, "restrict.group", GetGroupName(player, group), teamName, amount);
            subjectName = reader => GetGroupName(reader, group);
        }
        else if (WeaponCatalog.Find(target) is { } weapon)
        {
            weapons = [weapon];
            message = Localizer.ForPlayer(player, "restrict.weapon", weapon.DisplayName, teamName, amount);
            subjectName = _ => weapon.DisplayName;
        }
        else
        {
            ReplyKey(player, info, "command.invalid_weapon", target);
            return;
        }

        SetBaseLimits(player, weapons, team, amount, target, subjectName);
        Reply(player, info, message);

        string? overridden = DescribePerPlayerOverride(player, weapons);
        if (overridden != null)
        {
            Reply(player, info, overridden);
        }
    }

    private void SetBaseLimits(CCSPlayerController? player, List<WeaponDefinition> weapons, RestrictTeam team, int amount, string target,
        Func<CCSPlayerController, string> subjectName)
    {
        foreach (WeaponDefinition weapon in weapons)
        {
            _limits.SetBase(weapon, team, amount);
        }

        string admin = player == null ? "Console" : $"{player.PlayerName} ({player.SteamID})";
        Logger.LogInformation("{Admin} set {Target} for {Team} to {Amount}.", admin, target, team, amount);
        AnnounceChange(player, weapons, subjectName, team, amount);
        QueueEnforce();
    }

    /// <summary>
    /// Tells the other players what an in-game admin changed, each in their own language. Changes from
    /// the server console, RCON or .cfg files stay quiet: they usually run on every map start. So does a
    /// change players would not notice: the plugin is off, or per-player rules decide every weapon.
    /// </summary>
    private void AnnounceChange(CCSPlayerController? admin, List<WeaponDefinition> weapons, Func<CCSPlayerController, string> subjectName,
        RestrictTeam team, int amount)
    {
        if (!Config.AnnounceChanges || !Config.Enabled || admin == null || weapons.All(_limits.HasPerPlayerLimit))
        {
            return;
        }

        string key = amount switch
        {
            RestrictionState.Unlimited => "announce.unrestricted",
            0 => "announce.blocked",
            _ => "announce.limited"
        };
        string teamKey = team switch
        {
            RestrictTeam.Terrorist => "team.t",
            RestrictTeam.CounterTerrorist => "team.ct",
            _ => "team.both"
        };

        foreach (CCSPlayerController reader in Utilities.GetPlayers())
        {
            if (!reader.IsBot && reader.Slot != admin.Slot)
            {
                PrintToChat(reader, Localizer.ForPlayer(reader, key, admin.PlayerName, subjectName(reader), Localizer.ForPlayer(reader, teamKey), amount));
            }
        }
    }

    /// <summary>
    /// Admin changes to a weapon with an active per-player rule are stored but the rule keeps deciding,
    /// so say which weapons are affected instead of failing silently.
    /// </summary>
    private string? DescribePerPlayerOverride(CCSPlayerController? player, IEnumerable<WeaponDefinition> weapons)
    {
        string[] names = weapons.Where(_limits.HasPerPlayerLimit).Select(weapon => weapon.DisplayName).ToArray();
        return names.Length == 0 ? null : Localizer.ForPlayer(player, "restrict.perplayer_active", string.Join(", ", names));
    }

    private void OnMenuCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            ReplyKey(player, info, "command.ingame_only");
            return;
        }

        if (HasAdminAccess(player, info))
        {
            _pendingChatLimits.Remove(player.Slot);
            OpenMainMenu(player);
        }
    }

    private void OnRemoveRestrictedCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasAdminAccess(player, info))
        {
            return;
        }

        if (!Config.Enabled)
        {
            ReplyKey(player, info, "command.disabled");
            return;
        }

        EnforceRestrictions();
        ReplyKey(player, info, "command.enforced");
    }

    private void OnListCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasAdminAccess(player, info))
        {
            return;
        }

        List<WeaponDefinition> restricted = WeaponCatalog.All.Where(_limits.IsRestrictedForAnyTeam).ToList();
        if (restricted.Count == 0)
        {
            ReplyKey(player, info, "list.none");
            return;
        }

        ReplyKey(player, info, "list.title");
        string tName = Localizer.ForPlayer(player, "team.t_short");
        string ctName = Localizer.ForPlayer(player, "team.ct_short");
        string perPlayer = Localizer.ForPlayer(player, "list.perplayer");
        foreach (WeaponDefinition weapon in restricted)
        {
            string line = FormatWeaponLine(weapon, tName, ctName);
            Reply(player, info, _limits.HasPerPlayerLimit(weapon) ? $"{line} ({perPlayer})" : line);
        }
    }

    /// <summary>
    /// Open to everyone: players see their own team's limits, spectators and the console both teams.
    /// </summary>
    private void OnRestrictionsCommand(CCSPlayerController? player, CommandInfo info)
    {
        CsTeam[] teams = player?.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist
            ? [player.Team]
            : [CsTeam.Terrorist, CsTeam.CounterTerrorist];

        bool any = false;
        foreach (CsTeam team in teams)
        {
            (List<string> blocked, List<string> limited) = DescribeTeamLimits(player, team);
            if (blocked.Count == 0 && limited.Count == 0)
            {
                continue;
            }

            any = true;
            Reply(player, info, teams.Length == 1
                ? Localizer.ForPlayer(player, "list.your_team")
                : Localizer.ForPlayer(player, "list.team_header", Localizer.ForPlayer(player, team == CsTeam.Terrorist ? "team.t" : "team.ct")));
            ReplyEntries(player, info, "list.blocked", blocked);
            ReplyEntries(player, info, "list.limited", limited);
        }

        if (!any)
        {
            ReplyKey(player, info, "list.none");
        }
    }

    /// <summary>A group whose weapons are all blocked is named once instead of weapon by weapon.</summary>
    private (List<string> Blocked, List<string> Limited) DescribeTeamLimits(CCSPlayerController? player, CsTeam team)
    {
        List<string> blocked = [];
        List<string> limited = [];
        if (!Config.Enabled)
        {
            return (blocked, limited);
        }

        foreach (WeaponGroup group in MenuCategories)
        {
            WeaponDefinition[] weapons = WeaponCatalog.InGroup(group).ToArray();
            if (group != WeaponGroup.None && weapons.All(weapon => _limits.Get(weapon, team) == 0))
            {
                blocked.Add(GetGroupName(player, group));
                continue;
            }

            foreach (WeaponDefinition weapon in weapons)
            {
                int limit = _limits.Get(weapon, team);
                if (limit == 0)
                {
                    blocked.Add(weapon.DisplayName);
                }
                else if (limit > 0)
                {
                    limited.Add(Localizer.ForPlayer(player, "list.entry_limit", weapon.DisplayName, limit));
                }
            }
        }

        return (blocked, limited);
    }

    private void ReplyEntries(CCSPlayerController? player, CommandInfo info, string key, List<string> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        List<string> lines = JoinInLines(entries, MaxListLineLength);
        Reply(player, info, Localizer.ForPlayer(player, key, lines[0]));
        foreach (string line in lines.Skip(1))
        {
            Reply(player, info, line);
        }
    }

    /// <summary>Chat cuts long messages, so the entries are spread over as many lines as needed.</summary>
    private static List<string> JoinInLines(IEnumerable<string> entries, int maxLength)
    {
        List<string> lines = [];
        StringBuilder line = new();
        foreach (string entry in entries)
        {
            if (line.Length > 0 && line.Length + 2 + entry.Length > maxLength)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            line.Append(line.Length > 0 ? ", " : string.Empty).Append(entry);
        }

        lines.Add(line.ToString());
        return lines;
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasAdminAccess(player, info))
        {
            return;
        }

        try
        {
            OnConfigParsed(ConfigManager.Load<UmbrellaWeaponRestrictConfig>(Path.GetFileName(ModuleDirectory)));
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not reload the config.");
            ReplyKey(player, info, "command.reload_failed", exception.Message);
            return;
        }

        ResetToConfig(Server.MapName);
        RefreshPerPlayer(force: true);
        QueueEnforce();
        ReplyKey(player, info, "command.reloaded");
    }

    private static bool TryParseLimit(string text, out int amount)
    {
        return int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount) &&
               amount is >= RestrictionState.Unlimited and <= RestrictionState.MaxLimit;
    }

    private static bool TryParseTeam(string text, out RestrictTeam team)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "t":
            case "terrorist":
            case "terrorists":
                team = RestrictTeam.Terrorist;
                return true;
            case "ct":
            case "counter":
            case "counter-terrorist":
            case "counter-terrorists":
                team = RestrictTeam.CounterTerrorist;
                return true;
            case "both":
            case "all":
            case "ambos":
                team = RestrictTeam.Both;
                return true;
            default:
                team = RestrictTeam.Both;
                return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Menu chat limit
    // ---------------------------------------------------------------------------------------------

    private HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !_pendingChatLimits.Remove(player.Slot, out PendingChatLimit? pending) || DateTime.UtcNow > pending.ExpiresUtc)
        {
            return HookResult.Continue;
        }

        // Anything that is not a number is ordinary chat and cancels the request.
        if (!int.TryParse(info.GetArg(1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int amount))
        {
            return HookResult.Continue;
        }

        if (amount is < RestrictionState.Unlimited or > RestrictionState.MaxLimit)
        {
            _pendingChatLimits[player.Slot] = pending;
            PrintToChat(player, Localizer.ForPlayer(player, "command.invalid_amount"));
        }
        else
        {
            ApplyMenuSelection(player, pending.Subject, pending.Team, amount);
        }

        return HookResult.Handled;
    }

    // ---------------------------------------------------------------------------------------------
    // Config helpers
    // ---------------------------------------------------------------------------------------------

    private static TeamLimit[] CreateUnrestrictedLimits()
    {
        return Enumerable.Repeat(TeamLimit.Unrestricted, WeaponCatalog.All.Count).ToArray();
    }

    private static Dictionary<string, Dictionary<string, MapRules.MapLimit>> ReadMapLimits(
        Dictionary<string, Dictionary<string, UmbrellaWeaponRestrictConfig.TeamLimitSettings>> maps)
    {
        return maps.ToDictionary(
            map => map.Key,
            map => (map.Value ?? []).Where(entry => entry.Value != null)
                .ToDictionary(entry => entry.Key, entry => new MapRules.MapLimit(entry.Value.T, entry.Value.CT)));
    }

    private static string NormalizePermission(string? value, string fallback, string name, ICollection<string> warnings)
    {
        string permission = value?.Trim() ?? string.Empty;
        if (permission.Length > 1 && permission[0] is '@' or '#')
        {
            return permission;
        }

        warnings.Add($"{name}: '{value}' is not a permission flag (@domain/flag) or group (#group), using '{fallback}'.");
        return fallback;
    }

    /// <summary>
    /// The sound is played with the client "play" command, so anything that could escape the argument
    /// or point outside the game content is refused.
    /// </summary>
    private static string NormalizeSoundPath(string? value, ICollection<string> warnings)
    {
        string path = value?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            return string.Empty;
        }

        bool safe = path.Length <= 160 &&
                    !Path.IsPathRooted(path) &&
                    path[0] != '/' &&
                    !path.Contains("..", StringComparison.Ordinal) &&
                    !path.Contains("//", StringComparison.Ordinal) &&
                    path.IndexOfAny(['\\', ':', ';', '"', '\'', '\r', '\n', '\t', ' ']) < 0;

        if (safe)
        {
            return path;
        }

        warnings.Add($"RestrictedSound: '{value}' is not a safe sound path, the sound is disabled.");
        return string.Empty;
    }
}
