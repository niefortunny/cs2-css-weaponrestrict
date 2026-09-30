using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace UmbrellaWeaponRestrict.Core;

/// <summary>
/// Reads and trims what a live player carries.
/// </summary>
public static class WeaponInventory
{
    public static CCSPlayerPawn? GetAlivePawn(CCSPlayerController player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        return pawn != null && pawn.IsValid && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE ? pawn : null;
    }

    public static int Count(CCSPlayerPawn pawn, WeaponDefinition weapon)
    {
        switch (weapon.Kind)
        {
            case WeaponKind.Kevlar:
                return pawn.ArmorValue > 0 && !HasHelmet(pawn) ? 1 : 0;
            case WeaponKind.AssaultSuit:
                return pawn.ArmorValue > 0 && HasHelmet(pawn) ? 1 : 0;
        }

        CPlayer_WeaponServices? services = pawn.WeaponServices;
        if (services == null)
        {
            return 0;
        }

        int count = 0;
        foreach (CHandle<CBasePlayerWeapon> handle in services.MyWeapons)
        {
            CBasePlayerWeapon? item = handle.Value;
            if (item != null && item.IsValid && item.AttributeManager.Item.ItemDefinitionIndex == weapon.DefinitionIndex)
            {
                count += weapon.IsStackable ? GetGrenadeCount(services, item) : 1;
            }
        }

        return count;
    }

    /// <summary>
    /// Walks the pawn's items once. Each managed item spends its team budget in
    /// <paramref name="remaining"/> (indexed by catalog index, negative = unrestricted); whatever does
    /// not fit is removed. Items the player is exempt from stay and spend nothing.
    /// </summary>
    public static void Enforce(CCSPlayerPawn pawn, int[] remaining, Func<WeaponDefinition, bool> isExempt)
    {
        if (pawn.ArmorValue > 0)
        {
            bool helmet = HasHelmet(pawn);
            WeaponDefinition armor = helmet ? WeaponCatalog.AssaultSuit : WeaponCatalog.Kevlar;
            ref int budget = ref remaining[armor.Index];
            if (budget >= 0 && !isExempt(armor))
            {
                if (budget > 0)
                {
                    budget--;
                }
                else
                {
                    RemoveArmor(pawn, helmet);
                }
            }
        }

        CPlayer_WeaponServices? services = pawn.WeaponServices;
        if (services == null)
        {
            return;
        }

        // Removing an item edits MyWeapons, so collect first.
        List<(CBasePlayerWeapon Item, WeaponDefinition Weapon)>? restricted = null;
        foreach (CHandle<CBasePlayerWeapon> handle in services.MyWeapons)
        {
            CBasePlayerWeapon? item = handle.Value;
            if (item == null || !item.IsValid)
            {
                continue;
            }

            WeaponDefinition? weapon = WeaponCatalog.FindByDefinitionIndex(item.AttributeManager.Item.ItemDefinitionIndex);
            if (weapon != null && remaining[weapon.Index] >= 0 && !isExempt(weapon))
            {
                (restricted ??= []).Add((item, weapon));
            }
        }

        if (restricted == null)
        {
            return;
        }

        foreach ((CBasePlayerWeapon item, WeaponDefinition weapon) in restricted)
        {
            ref int budget = ref remaining[weapon.Index];
            int units = weapon.IsStackable ? GetGrenadeCount(services, item) : 1;
            if (budget >= units)
            {
                budget -= units;
                continue;
            }

            if (budget > 0)
            {
                // Only grenades get here: keep what fits in the stack.
                SetGrenadeCount(pawn, services, item, budget);
                budget = 0;
                continue;
            }

            if (weapon.IsStackable)
            {
                SetGrenadeCount(pawn, services, item, 0);
            }

            // Destroys the weapon too; no separate Remove is needed.
            pawn.RemovePlayerItem(item);
        }
    }

    /// <summary>
    /// Several grenades of one type share a single entity; the count lives in the owner's ammo slot for
    /// that grenade.
    /// </summary>
    private static int GetGrenadeCount(CPlayer_WeaponServices services, CBasePlayerWeapon grenade)
    {
        int ammoType = GetAmmoType(grenade);
        return ammoType < 0 ? 1 : Math.Max(1, (int)services.Ammo[ammoType]);
    }

    private static void SetGrenadeCount(CCSPlayerPawn pawn, CPlayer_WeaponServices services, CBasePlayerWeapon grenade, int count)
    {
        int ammoType = GetAmmoType(grenade);
        if (ammoType < 0)
        {
            return;
        }

        services.Ammo[ammoType] = (ushort)count;
        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pWeaponServices");
    }

    private static int GetAmmoType(CBasePlayerWeapon weapon)
    {
        int ammoType = weapon.VData?.PrimaryAmmoType ?? -1;
        return ammoType is >= 0 and < 32 ? ammoType : -1;
    }

    private static bool HasHelmet(CCSPlayerPawn pawn)
    {
        CPlayer_ItemServices? services = pawn.ItemServices;
        return services != null && new CCSPlayer_ItemServices(services.Handle).HasHelmet;
    }

    private static void RemoveArmor(CCSPlayerPawn pawn, bool helmet)
    {
        pawn.ArmorValue = 0;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");

        if (helmet && pawn.ItemServices != null)
        {
            new CCSPlayer_ItemServices(pawn.ItemServices.Handle).HasHelmet = false;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
        }
    }
}
