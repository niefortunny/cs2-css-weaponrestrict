<div align="center">

# Umbrella Weapon Restrict

**Weapon restrictions for Counter-Strike 2, built natively on CounterStrikeSharp.**

[![Release](https://img.shields.io/github/v/release/Ayrton09/umbrella-weapon-restrict-cs2?style=flat-square&color=success)](https://github.com/Ayrton09/umbrella-weapon-restrict-cs2/releases)
[![License](https://img.shields.io/github/license/Ayrton09/umbrella-weapon-restrict-cs2?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![CounterStrikeSharp](https://img.shields.io/badge/CounterStrikeSharp-1.0.375-F5834A?style=flat-square)](https://docs.cssharp.dev/)

</div>

---

Limit weapons per team, per weapon group or by the number of players on the server. Restricted buys are refused before any money is taken, restricted weapons cannot be picked up, and weapons over a lowered limit are removed from the players who carry them.

> CS2 port based on my SourceMod plugin [Umbrella Weapon Restrict](https://github.com/Ayrton09/umbrella-weapon-restrict) for CS:S and CS:GO, rebuilt natively on CounterStrikeSharp.

## Contents

- [Features](#features)
- [Installation](#installation)
- [Commands](#commands)
- [Menu](#menu)
- [Configuration](#configuration)
- [Per-Player Rules](#per-player-rules)
- [Weapons](#weapons)
- [How It Works](#how-it-works)
- [Languages](#languages)
- [Coming from SourceMod](#coming-from-sourcemod)

## Features

**Restrictions**
- Per-team limits for every buyable weapon, grenade and armor
- Weapon groups: pistols, SMGs, shotguns, rifles, snipers, grenades, armor
- Optional limits that follow the number of players on the server
- Admin immunity through a CounterStrikeSharp flag or group

**Enforcement**
- Buys are refused before the money is taken
- Restricted weapons cannot be picked up or handed out, spawn loadouts included (optional)
- Weapons over a lowered limit are removed at once and at every round start

**Admins**
- WASD center menu organized by weapon type, color coded per group
- Console, RCON and chat commands, also usable from `.cfg` files
- Config reload without restarting the server

**Players**
- Chat notice plus the game's "can't buy" sound, with spam protection
- Nine languages

## Installation

**1.** Download the latest ZIP from [Releases](https://github.com/Ayrton09/umbrella-weapon-restrict-cs2/releases).

**2.** Extract it into `game/csgo/`. The resulting layout:

```text
addons/counterstrikesharp/plugins/UmbrellaWeaponRestrict/
|-- UmbrellaWeaponRestrict.dll
|-- UmbrellaWeaponRestrict.deps.json
`-- lang/
    `-- en.json, es.json, pt.json, ru.json, zh.json, de.json, fr.json, pl.json, tr.json
```

**3.** Start the server. The config is created on first start at:

```text
addons/counterstrikesharp/configs/plugins/UmbrellaWeaponRestrict/UmbrellaWeaponRestrict.json
```

Requires CounterStrikeSharp 1.0.375 or newer.

## Commands

| Command | Description |
| --- | --- |
| `css_restrict <weapon\|group\|all> [amount] [t\|ct\|both]` | Set a limit. `amount` defaults to `0` (blocked), the team to `both` |
| `css_unrestrict <weapon\|group\|all> [t\|ct\|both]` | Remove a limit |
| `css_restrictmenu` / `css_wrmenu` | Open the restriction menu (in-game only) |
| `css_remove_restricted` | Remove the weapons over the current limits right now |
| `css_wr_list` | List the active restrictions |
| `css_wr_reload` | Reload the config file and reset every limit to it |

Every command needs `AdminPermission` (`@css/cvar` by default). In chat they work as `!restrict`, `/restrict` and so on; from the server console, RCON or a `.cfg` file no permission is checked.

```text
css_restrict awp 0 both
css_restrict ak47 2 t
css_restrict snipers 1
css_unrestrict grenades t
css_unrestrict all
```

Limit values:

```text
-1 = unrestricted
 0 = blocked
>0 = maximum amount for that team (up to 64)
```

`all` also accepts `@all`, and the team also accepts `terrorist` and `counter-terrorist`.

Changes made with commands or the menu last until the map changes or `css_wr_reload` runs: both start again from the config file. To use different limits on some maps or modes, put the commands in a `.cfg` that runs after the map loads, such as an [Umbrella Multimod Interface](https://github.com/Ayrton09/umbrella-multimod-interface-cs2) category config.

## Menu

`!wrmenu` (or `!restrictmenu`) opens a center menu driven with the movement keys:

- **Restrict a whole group** applies one limit to every weapon of a group.
- **Pistols, SMGs, Shotguns, Rifles, Snipers, Grenades, Armor, Other** list their weapons with the current limits. Each group has its own color and shows how many of its weapons are restricted.
- After a weapon or group, pick the team and the limit: unlimited, blocked, or **type a number in chat** (1 to 64). The number is not shown in chat, and a message that is not a number cancels.

While the menu is open you stay in place, so the movement keys only drive the menu. After a change it goes back to the list you came from. It works while dead or spectating, and closes when you take over a bot (open it again on the bot).

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. The commands and the menu keep working while it is off |
| `AllowRestrictedPickup` | `false` | Let players get restricted weapons from anything but a buy: the ground, spawn loadouts and other plugins |
| `Notify` | `true` | Tell players in chat why a buy or pickup was refused |
| `RestrictedSound` | `sounds/ui/weapon_cant_buy.vsnd_c` | Sound played on a refused buy or pickup. `""` disables it |
| `AdminPermission` | `@css/cvar` | Flag (`@css/...`) or group (`#...`) needed for the commands and the menu |
| `AdminImmunity` | `false` | Players with `ImmunityPermission` ignore every limit and do not count toward it |
| `ImmunityPermission` | `@css/root` | Flag or group checked for immunity |
| `Restrictions` | all `-1` | Limit per weapon and team, see below |
| `PerPlayer` | disabled | Limits that follow the player count, see [Per-Player Rules](#per-player-rules) |
| `Menu` | | Menu keys and behavior, see below |
| `Debug` | `false` | Log refused buys and pickups (once per notice, like the chat message) and per-player refreshes |

`Restrictions` lists every managed weapon with a limit for each team:

```json
"Restrictions": {
  "awp": { "T": 1, "CT": 1 },
  "negev": { "T": 0, "CT": 0 },
  "flashbang": { "T": -1, "CT": 2 }
}
```

Weapons missing from the list are unrestricted. Keys accept the aliases from the [weapon table](#weapons).

`Menu` controls the WASD center menu:

| Option | Default | Description |
| --- | --- | --- |
| `FreezePlayer` | `true` | Hold the player in place while the menu is open |
| `VisibleItems` | `5` | Rows shown at once (3 to 9) |
| `InputDebounceMilliseconds` | `140` | Minimum time between two key presses |
| `InitialInputDelayMilliseconds` | `500` | Keys ignored right after opening the menu |
| `ScrollUpKeys` / `ScrollDownKeys` | `["W"]` / `["S"]` | Move the selection |
| `SelectKeys` | `["E"]` | Choose the selected row |
| `BackKeys` | `["Shift", "A"]` | Go back one screen |
| `CancelKeys` | `["R"]` | Close the menu |

Each action takes one or more of `W`, `A`, `S`, `D`, `E`, `R` and `Shift`; a key can belong to only one action. For example, `"BackKeys": ["Shift"]` leaves only Shift for going back.

The config is read on start and with `css_wr_reload`. Invalid values are clamped or ignored and reported in the server log.

## Per-Player Rules

A rule sets the limit of a weapon from the number of players on the server. It uses `default` below the first threshold and changes once the player count reaches each threshold:

```json
"PerPlayer": {
  "Enabled": true,
  "CountBots": true,
  "CountSpectators": true,
  "Rules": {
    "awp": { "default": 0, "10": 1, "20": 2 }
  }
}
```

Here the AWP is blocked below 10 players, limited to 1 per team from 10 players and to 2 from 20. Without `default`, the weapon's `Restrictions` entry applies below the first threshold.

- A rule applies to both teams. While it gives a limit, it replaces the weapon's `Restrictions` entry, and admin commands for that weapon are ignored (the reply says so).
- When the player count makes a limit stricter, new buys and pickups are refused at once, but weapons already owned stay until the next round start (or the next admin change), so nobody loses a weapon mid-round because someone left.
- `CountBots` and `CountSpectators` decide who is counted.

## Weapons

Commands and config keys accept the key, any alias, or the entity name (`weapon_awp`, `item_kevlar`).

| Group | Key (aliases) |
| --- | --- |
| `pistols` | `glock` (glock18), `hkp2000` (p2000), `usp_silencer` (usp, usps, usp_s), `elite` (dualies, berettas), `p250`, `tec9`, `fiveseven` (five_seven), `cz75a` (cz75, cz), `deagle` (desert_eagle), `revolver` (r8) |
| `smgs` | `mac10`, `mp9`, `mp7`, `mp5sd` (mp5), `ump45` (ump), `p90`, `bizon` (ppbizon) |
| `shotguns` | `nova`, `xm1014` (xm), `sawedoff` (sawed_off), `mag7` |
| `rifles` | `galilar` (galil), `famas`, `ak47` (ak), `m4a1` (m4a4), `m4a1_silencer` (m4a1s, m4a1_s), `sg556` (sg553), `aug`, `m249`, `negev` |
| `snipers` | `ssg08` (scout), `awp`, `g3sg1`, `scar20` |
| `grenades` | `flashbang` (flash), `hegrenade` (he), `smokegrenade` (smoke), `molotov` (molly), `incgrenade` (incendiary, inc), `decoy` |
| `armor` | `kevlar` (vest), `assaultsuit` (vesthelm) |
| no group | `taser` (zeus) |

`m4a1` is the M4A4, as in the game files. Group names also work in singular form (`rifle`, `sniper`...), plus `armour`.

Knives, the C4, the defuser and the other utility items are not managed: restricting the C4 would break bomb maps.

## How It Works

- **Buy block:** the plugin answers CS2's own check for whether a player may acquire an item. A refused buy never charges money and never hands out the item, and it covers the buy menu, `buy` commands, autobuy and rebuy.
- **Pickup block:** the same check runs when a player touches or uses a weapon on the ground, and when the game or another plugin hands a player an item (spawn loadouts, gun menus, retakes). So restricting `glock` or `pistols` also means spawning without that pistol. `AllowRestrictedPickup` turns all of this off.
- **Enforcement:** when an admin lowers a limit and at every round start, weapons over the limit are removed. Players are checked in slot order, so the first ones keep theirs.
- **Counting:** only living players count and weapons on the ground do not. While a weapon is allowed at all, a player who already owns one can swap it for another of the same kind. Each grenade counts on its own, so two flashbangs on one player use two of the team's flashbangs.
- **Armor:** `kevlar` is armor without a helmet and `assaultsuit` armor with a helmet.

## Languages

`en` `es` `pt` `ru` `zh` `de` `fr` `pl` `tr`

Players see the language they chose with CounterStrikeSharp's `!lang` command, falling back to the server language set in `core.json`.

## Coming from SourceMod

| SourceMod | CS2 |
| --- | --- |
| `sm_uwr_*` cvars | `UmbrellaWeaponRestrict.json` |
| `sm_restrict_<weapon>_t/ct` cvars | `Restrictions` section |
| `perplayerrestrict.txt` | `PerPlayer.Rules` |
| `sm_restrict`, `sm_unrestrict`, `sm_restrictmenu`, `sm_wrmenu`, `sm_remove_restricted` | same commands with the `css_` prefix |
| Admin menu category | WASD center menu |
| `ADMFLAG_CONVARS` / `umbrella_weapon_restrict_immunity` override | `AdminPermission` / `ImmunityPermission` |

Behavior that changed on the way:

- The weapon list follows CS2: the heavy assault suit and the CS:S-only weapons are gone, and the USP-S, M4A1-S, CZ75-Auto, R8 and MP5-SD have their own keys.
- Each grenade counts on its own. SourceMod counted a player with two flashbangs once, so a migrated `flashbang` limit of 2 is now stricter.
- A per-player rule without `default` used to mean unrestricted below its first threshold; now the weapon's `Restrictions` entry applies there.
- The `sm_uwr_*` switches could be changed per map from a `.cfg`. The JSON options cannot; per-map changes go through the commands, which cover every limit.

## License

Released under the [MIT License](LICENSE).
