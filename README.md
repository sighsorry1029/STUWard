# STU Ward

Protect your base, share access, and keep doors from staying open. STU Ward adds configurable wards with player registration, map pins, and optional Guilds or Clan access.

[User guide](https://github.com/sighsorry1029/STUWard/blob/main/docs/user-guide.md) · [Permissions](https://github.com/sighsorry1029/STUWard/blob/main/docs/user-guide.md#permissions)

Click any image or animation to view the original.

## See it in action

| Manage players | Set ward rules |
| --- | --- |
| <a href="https://i.ibb.co/Q7GkMgvB/Screenshot-2026-03-31-031730.png"><img src="https://i.ibb.co/Q7GkMgvB/Screenshot-2026-03-31-031730.png" alt="Registered and recent players in Ward Settings" width="320"></a> | <a href="https://i.ibb.co/HLH084PV/Screenshot-2026-03-31-024031.png"><img src="https://i.ibb.co/HLH084PV/Screenshot-2026-03-31-024031.png" alt="Ward behavior and protection settings" width="320"></a> |

Register players on the first settings page. Adjust alerts, doors, and available protection options on the second.

| Ward boundaries | Doors that close for you |
| --- | --- |
| <a href="https://i.ibb.co/gZRSPGrr/Screenshot-2026-03-30-234415.png"><img src="https://i.ibb.co/gZRSPGrr/Screenshot-2026-03-30-234415.png" alt="Ward ranges and overlap boundaries" width="320"></a> | <a href="https://i.ibb.co/v42DSpST/Video-Project-8.gif"><img src="https://i.ibb.co/v42DSpST/Video-Project-8.gif" alt="A door closing automatically inside a ward" width="320"></a> |

Wards can overlap when they share an owner or an authorized group. Auto-close shuts doors after a short delay.

## What you get

| Feature | What it does |
| --- | --- |
| Base protection | Restricts access to chests, doors, crafting stations, and other protected objects. |
| Building protection | Blocks unauthorized building, repairs, removal, and terrain changes. Protects building pieces from damage. |
| Shared access | Register individual characters or use an optional Guilds or Clan group. |
| Ward settings | Control alerts, door auto-close, range rotation, and settings allowed by the server. |
| Outpost Ward | A teal ward with the same protection, a default 16 m range cap, and a separate limit of 3 per account. |
| Item rules | Set blocked item prefabs and separate pickup lists. |
| Map and server tools | Show ward pins and ranges, set account ward limits, and request ward reports. |

## Start here

These are the default keyboard controls.

| To… | Do this |
| --- | --- |
| Place a ward | Select **Ward** or **Outpost Ward** with the Hammer and build it. |
| Activate or deactivate it | Look at the ward and press **E**. |
| Open Ward Settings | Look at the ward and press **Left Alt + E**. |
| Register a player | Open the first settings page and add a character from the recent-player list. |
| Change ward behavior | Open the second settings page. The server decides which options you may change. |

New wards fit their range around existing foreign wards. Manual range adjustment must be enabled by the server. [Range and overlap rules](https://github.com/sighsorry1029/STUWard/blob/main/docs/user-guide.md#ward-overlap).

## Who has access?

- The ward owner and individually registered characters.
- Members of the ward's authorized Guild or primary Clan, while that integration is enabled.
- Server admins with active admin-debug access.

**Every trusted player can change settings, manage registrations, toggle the ward, and dismantle it.** Registering someone grants management access as well as use of the protected area.

Use either **Guilds or Clan**, with only one installed. Clan Leader, Officer, and Member roles receive automatic access; **Guests do not**. Where wards overlap, every covering ward must allow the action.

Enabled wards also block player and tamed-creature damage to protected building pieces, including attacks by trusted players. Trust does not disable this damage protection.

STUWard lets you recover your own tombstone without ward access. Ordinary chests and other players' graves keep their normal restrictions.

## Installation

Install with a Valheim mod manager, or place `STUWard.dll` in `BepInEx/plugins`. Requires **BepInExPack Valheim 5.4.2351**. Jotunn is not required.

Install the **same STUWard version on the server and every client**. Launch once to create the config; enter a world to initialize server data.

## Configuration

Paths are relative to `BepInEx/config/`:

| File | Use it for |
| --- | --- |
| `sighsorry.STUWard.cfg` | Server rules, ward limits, maximum range, integrations, and local display controls. |
| `STUWard/STUWard.yml` | Server item lists and per-account ward limit overrides. |
| `STUWard/STUWard.RecentPlayers.yml` | Automatically maintained recent-player history. |

The server controls protection settings. Restrictions default to **ForcedOn**; use **NotForced** to let trusted players change that restriction per ward. **Ward Range Configuration** is off by default.

## Integrations and support

- **Guilds or Clan:** automatic group access, shared overlap, and group labels.
- **EpicLoot:** enchanting-table access follows the ward's Crafting Stations restriction.
- **FeedLikeGrandma:** configured Taming Orb restrictions check both the player and the capture or release location. Update both mods on the server and clients.
- **PortalRules:** supported builds defer portal damage and dismantling protection to active STU Wards.

See the [user guide](https://github.com/sighsorry1029/STUWard/blob/main/docs/user-guide.md) for item-policy examples, integration requirements, and detailed permission rules.

[GitHub](https://github.com/sighsorry1029/STUWard) · [Report an issue](https://github.com/sighsorry1029/STUWard/issues) · [Discord](https://discord.gg/jkcJCq2sK5) · [Changelog](https://github.com/sighsorry1029/STUWard/blob/main/Thunderstore/CHANGELOG.md)
