using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using UmbrellaWeaponRestrict.Core;

namespace UmbrellaWeaponRestrict;

public sealed partial class UmbrellaWeaponRestrictPlugin
{
    /// <summary>
    /// Menu categories: the weapon groups plus "Other" for the Zeus, which belongs to no group.
    /// </summary>
    private static readonly WeaponGroup[] MenuCategories = [.. WeaponCatalog.Groups, WeaponGroup.None];

    /// <summary>What a team/limit screen edits: one weapon, or every weapon of a group.</summary>
    private sealed record MenuSubject(WeaponDefinition? Weapon, WeaponGroup Group);

    private sealed record PendingChatLimit(MenuSubject Subject, RestrictTeam Team, DateTime ExpiresUtc);

    private void OpenMainMenu(CCSPlayerController player)
    {
        if (_menuApi == null)
        {
            Logger.LogError("[UmbrellaWeaponRestrict] MenuManager Core not found...");
            return;
        }

        var menu = _menuApi.GetMenu(Localizer.ForPlayer(player, "menu.title"));
        menu.AddMenuOption(Localizer.ForPlayer(player, "menu.groups_option"), (p, _) => OpenGroupMenu(p));

        foreach (WeaponGroup category in MenuCategories)
        {
            menu.AddMenuOption(FormatCategory(player, category), (p, _) => OpenCategoryMenu(p, category));
        }

        menu.PostSelectAction = PostSelectAction.Close;
        menu.ExitButton = true;
        menu.Open(player);
    }

    private void OpenGroupMenu(CCSPlayerController player)
    {
        if (_menuApi == null)
        {
            return;
        }

        var menu = _menuApi.GetMenu(
            Localizer.ForPlayer(player, "menu.groups_title"),
            backAction: p => OpenMainMenu(p));

        foreach (WeaponGroup group in WeaponCatalog.Groups)
        {
            menu.AddMenuOption(FormatCategory(player, group), (p, _) =>
                OpenTeamMenu(p, new MenuSubject(null, group)));
        }

        menu.PostSelectAction = PostSelectAction.Close;
        menu.ExitButton = true;
        menu.Open(player);
    }

    private void OpenCategoryMenu(CCSPlayerController player, WeaponGroup category)
    {
        if (_menuApi == null)
        {
            return;
        }

        string tName = Localizer.ForPlayer(player, "team.t_short");
        string ctName = Localizer.ForPlayer(player, "team.ct_short");

        var menu = _menuApi.GetMenu(
            GetGroupName(player, category),
            backAction: p => OpenMainMenu(p));

        foreach (WeaponDefinition weapon in WeaponCatalog.InGroup(category))
        {
            menu.AddMenuOption(FormatWeaponLine(weapon, tName, ctName), (p, _) =>
                OpenTeamMenu(p, new MenuSubject(weapon, category)));
        }

        menu.PostSelectAction = PostSelectAction.Close;
        menu.ExitButton = true;
        menu.Open(player);
    }

    private void OpenTeamMenu(CCSPlayerController player, MenuSubject subject)
    {
        if (_menuApi == null)
        {
            return;
        }

        Action<CCSPlayerController> back = subject.Weapon != null
            ? p => OpenCategoryMenu(p, subject.Group)
            : p => OpenGroupMenu(p);

        var menu = _menuApi.GetMenu(
            Localizer.ForPlayer(player, "menu.team_title", GetSubjectName(player, subject)),
            backAction: back);

        menu.AddMenuOption(Localizer.ForPlayer(player, "team.t"), (p, _) =>
            OpenLimitMenu(p, subject, RestrictTeam.Terrorist));
        menu.AddMenuOption(Localizer.ForPlayer(player, "team.ct"), (p, _) =>
            OpenLimitMenu(p, subject, RestrictTeam.CounterTerrorist));
        menu.AddMenuOption(Localizer.ForPlayer(player, "team.both"), (p, _) =>
            OpenLimitMenu(p, subject, RestrictTeam.Both));

        menu.PostSelectAction = PostSelectAction.Close;
        menu.ExitButton = true;
        menu.Open(player);
    }

    private void OpenLimitMenu(CCSPlayerController player, MenuSubject subject, RestrictTeam team)
    {
        if (_menuApi == null)
        {
            return;
        }

        var menu = _menuApi.GetMenu(
            Localizer.ForPlayer(player, "menu.limit_title", GetSubjectName(player, subject), GetTeamName(player, team)),
            backAction: p => OpenTeamMenu(p, subject));

        menu.AddMenuOption(Localizer.ForPlayer(player, "menu.limit_unlimited"), (p, _) =>
            ApplyMenuSelection(p, subject, team, RestrictionState.Unlimited));
        menu.AddMenuOption(Localizer.ForPlayer(player, "menu.limit_blocked"), (p, _) =>
            ApplyMenuSelection(p, subject, team, 0));
        menu.AddMenuOption(Localizer.ForPlayer(player, "menu.limit_custom"), (p, _) =>
            RequestChatLimit(p, subject, team));

        menu.PostSelectAction = PostSelectAction.Close;
        menu.ExitButton = true;
        menu.Open(player);
    }

    /// <summary>
    /// Closes the menu and takes the limit from the admin's next chat message instead of listing every
    /// number from 1 to 64.
    /// </summary>
    private void RequestChatLimit(CCSPlayerController player, MenuSubject subject, RestrictTeam team)
    {
        _menuApi?.CloseMenu(player);
        _pendingChatLimits[player.Slot] = new PendingChatLimit(subject, team, DateTime.UtcNow.AddSeconds(ChatLimitTimeoutSeconds));
        PrintToChat(player, Localizer.ForPlayer(player, "menu.limit_prompt", GetSubjectName(player, subject), GetTeamName(player, team)));
    }

    private void ApplyMenuSelection(CCSPlayerController player, MenuSubject subject, RestrictTeam team, int amount)
    {
        if (!HasPermission(player, Config.AdminPermission))
        {
            _menuApi?.CloseMenu(player);
            PrintToChat(player, Localizer.ForPlayer(player, "command.no_permission"));
            return;
        }

        List<WeaponDefinition> weapons = subject.Weapon != null
            ? [subject.Weapon]
            : WeaponCatalog.InGroup(subject.Group).ToList();

        SetBaseLimits(player, weapons, team, amount, subject.Weapon?.Key ?? subject.Group.ToString().ToLowerInvariant(),
            reader => GetSubjectName(reader, subject));

        string teamName = GetTeamName(player, team);
        PrintToChat(player, subject.Weapon != null
            ? Localizer.ForPlayer(player, "restrict.weapon", subject.Weapon.DisplayName, teamName, amount)
            : Localizer.ForPlayer(player, "restrict.group", GetGroupName(player, subject.Group), teamName, amount));

        string? overridden = DescribePerPlayerOverride(player, weapons);
        if (overridden != null)
        {
            PrintToChat(player, overridden);
        }

        // Back to the list the admin came from, rebuilt so it shows the new limits.
        if (subject.Weapon != null)
        {
            OpenCategoryMenu(player, subject.Group);
        }
        else
        {
            OpenGroupMenu(player);
        }
    }

    private string FormatCategory(CCSPlayerController player, WeaponGroup group)
    {
        int restricted = WeaponCatalog.InGroup(group).Count(_limits.IsRestrictedForAnyTeam);
        string name = GetGroupName(player, group);
        return restricted == 0 ? name : $"{name} ({restricted})";
    }

    private string FormatWeaponLine(WeaponDefinition weapon, string tName, string ctName)
    {
        int tLimit = _limits.Get(weapon, CsTeam.Terrorist);
        int ctLimit = _limits.Get(weapon, CsTeam.CounterTerrorist);

        if (tLimit < 0 && ctLimit < 0)
        {
            return weapon.DisplayName;
        }

        if (tLimit >= 0 && ctLimit >= 0)
        {
            return $"{weapon.DisplayName}  {tName}:{tLimit} {ctName}:{ctLimit}";
        }

        return tLimit >= 0
            ? $"{weapon.DisplayName}  {tName}:{tLimit}"
            : $"{weapon.DisplayName}  {ctName}:{ctLimit}";
    }

    private string GetSubjectName(CCSPlayerController? player, MenuSubject subject)
    {
        return subject.Weapon?.DisplayName ?? GetGroupName(player, subject.Group);
    }

    private string GetGroupName(CCSPlayerController? player, WeaponGroup group)
    {
        return Localizer.ForPlayer(player, group switch
        {
            WeaponGroup.Pistols => "group.pistols",
            WeaponGroup.Smgs => "group.smgs",
            WeaponGroup.Shotguns => "group.shotguns",
            WeaponGroup.Rifles => "group.rifles",
            WeaponGroup.Snipers => "group.snipers",
            WeaponGroup.Grenades => "group.grenades",
            WeaponGroup.Armor => "group.armor",
            _ => "group.other"
        });
    }

    private string GetTeamName(CCSPlayerController? player, RestrictTeam team)
    {
        return Localizer.ForPlayer(player, team switch
        {
            RestrictTeam.Terrorist => "team.t_short",
            RestrictTeam.CounterTerrorist => "team.ct_short",
            _ => "team.both"
        });
    }
}
