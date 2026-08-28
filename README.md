# STU Ward

Simple, Tidy, and Unique Ward for Valheim servers. <br>
It adds a clone of vanilla ward but with more server-side features such as diverse protections, permission control, Guilds/Clan integration, ward count limits, and compatibility handling for common utility mods.
<br>
![](https://i.ibb.co/Q7GkMgvB/Screenshot-2026-03-31-031730.png)<br>
Trusted players manage individual ward registration from Ward Settings. <br>
There is blacklist config to block certain items inside ward area.

![](https://i.ibb.co/HLH084PV/Screenshot-2026-03-31-024031.png)<br>
Ward Settings UI <br>

![](https://i.ibb.co/gZRSPGrr/Screenshot-2026-03-30-234415.png)
Ward area cannot be overlapped unless the wards share an owner or an authorized Guild/Clan group.<br>

![](https://i.ibb.co/v42DSpST/Video-Project-8.gif) <br>
Good old auto closing door inside ward area. <br>

## What It Does

- Adds a placeable `Ward` with server-controlled protection rules
- Lets trusted players configure alerts, range rotation, door auto-close, protected-action restrictions, and ward range when the server enables it
- Blocks unauthorized interaction, building, terrain edits, pickup, item use, and damage inside enabled foreign wards
- Prevents foreign ward overlap while allowing same-owner and same-Guild/Clan ward groups
- Tracks per-account ward limits
- Shows ward pins and active ranges on the map when allowed

## How To Use

1. Select `Ward` with the hammer and place it.
2. The server assigns the largest legal radius up to its configured maximum.
3. Look at your ward and press `Alt+E` to open `Ward Settings`.
4. On the first page, manage registered and recent unregistered players.
5. Add a character from the server's recent unregistered-player list.
6. Open the second page to configure ward alerts, range rotation, door auto-close, protected actions, and ward range when enabled by the server.

## Protection

Inside an enabled ward, unauthorized players are blocked from:

- Opening or using containers, doors, carts, ships, signs, item stands, beehives, crafting stations, fermenters, sap collectors, traps, portals, and tamed creatures
- Building, repairing, removing pieces, or modifying terrain
- Damaging protected structures and objects
- Picking up items, including auto-pickup when the item policy blocks it
- Using or equipping blocked item prefabs
- Using creature-catching items on protected tamed animals

Building pieces inside an enabled STU Ward receive extra damage protection. Player and tamed-creature damage to protected building pieces is blocked, and hostile creature damage can also be blocked depending on ward attendance settings.

## Permissions

STU Ward uses one trusted-player permission level for existing wards. Trust is granted to:

- The ward owner
- Individually registered players
- Players matching the ward's authorized Guild or primary Clan identity
- Server admins using effective debug control

All trusted players can use the protected area, open and change ward settings, toggle the ward, add or remove players from its individual registration list, and dismantle the ward. Authorized changes apply immediately without confirmation popups.

The owner identity is still retained for ward limits, reporting, and group metadata; it does not grant a higher permission tier on an existing ward.

STUWard supports either `Guilds` or `Clan 1.0.0+` (`sighsorry.Clan`, public API v4) as its group provider. Clan `Leader`, `Officer`, and `Member` roles are authorized through the primary Clan; `Guest` connections never grant automatic ward access. Individually registered, owner, and admin-debug trust continue to work independently of group membership. An older or incompatible Clan API fails closed for automatic group authorization.

## Registration

Individual registration is managed by trusted players in Ward Settings:

- The server stores structured policy in `<Valheim save data>/STUWard/STUWard.yml` and keeps one recent-player history in `<Valheim save data>/STUWard/STUWard.RecentPlayers.yml`.
- The list includes authenticated characters currently online or seen within the last twenty-eight days, with online characters first and older activity lower in the list.
- Registered and recent-player rows show the character name, resolved Guild/Clan group, account identity, and online/last-seen status. SteamID64 values are displayed as their last ten digits to save space; other platform IDs remain unchanged, and the full account ID remains searchable. Registered characters without retained activity show that their last-seen time is unavailable.
- Trusted players can add a recent character to the ward or remove an individually registered character.
- Registration is character-specific because ward permissions use Valheim player IDs.
- Disabled wards do not allow outsiders to register themselves.

Recent-player history begins when STUWard 1.3.0 is installed. Earlier visits are not imported, and extending retention from fourteen to twenty-eight days does not restore records that were already pruned. Those characters must reconnect before appearing again.

On Windows without a custom `-savedir`, the data directory is `AppData/LocalLow/IronGate/Valheim/STUWard`. A local host (single-player or listen server) can open it with the folder button in the Ward Settings header; the button is hidden from remote clients and headless dedicated servers. The directory is shared by every local world and mod-manager profile that uses the same Valheim save-data root. Dedicated server instances should use separate `-savedir` values when they require independent STUWard policy and recent-player history.

## Ward Overlap

Ward overlap is strict.

- Foreign wards cannot overlap.
- Same-owner wards can overlap.
- Wards with the same exact provider-qualified Guild/Clan identity can overlap.
- Registered-player access does not bypass overlap rules.

When placing a new ward, older foreign wards keep their radius and the new ward automatically yields to the largest non-overlapping radius. `Ward Range Configuration` is a synchronized server setting that defaults to `Off`. When enabled, trusted players can explicitly shrink or re-expand a ward from Ward Settings, but the server clamps expansion to the currently available non-overlapping radius without changing neighboring wards. Removing a neighboring ward or increasing the server maximum does not silently expand it. Lowering the server maximum clamps existing wards.

In overlapping coverage, access is additive: if any enabled foreign ward denies the player, the action is denied.

## Ward Settings

Each ward can store its own behavior:

- Ward range when enabled by the synchronized server setting (from 8 m up to the server maximum and the currently available non-overlapping radius)
- Ward alert sound
- Ward alert visual effect
- Ward range rotation (enabled by default at 50% of the native rotation speed; stationary when disabled)
- Door auto-close
- Protected-action restrictions

When auto-close is enabled, doors opened inside the active ward area close after a shared minimum delay of 5 seconds. If a slow-opening door is still animating at that point, STUWard waits up to 60 seconds and closes it as soon as the door becomes interactable.

Ward rings are visible for placement previews and enabled wards. Disabled wards remain hidden unless a placement conflict highlights the closest blocking ward ring for 1.5 seconds on the client that attempted placement. Crossing an enabled ward boundary locally raises that ward ring from its minimum brightness to full brightness for 0.5 seconds, and it remains at full brightness while the player stays within 0.75m of the boundary. The client-only `Ward Boundary Brighten Mode` selects trusted wards, untrusted wards, all wards (the default), or disables this boundary cue.

The first settings page is dedicated to player management, with separate scrolling lists for registered and recent unregistered players. Each list has its own local search field for character name, Guild/Clan group, account ID, or character player ID. The second page contains a two-column behavior grid and an independently scrolling restrictions grid.

## Item Policy

Servers can define blocked item prefabs and pickup rules.

Blocked item prefabs cannot be used, equipped, or used to attack while the player is inside a foreign enabled ward. Pickup rules can either block everything except a whitelist or allow everything except a blacklist.

## Map Pins

Ward pins can show ward locations and active ranges on the map.

Players normally see wards they are allowed to see. Admin debug control can show all managed wards.

## Important Details

- Ownership metadata is based on the ward creator player id.
- Account identity is used for limits and reporting, not as a separate permission tier.
- A ward's provider-qualified group identity is projected from its owner's authoritative Guilds or Clan membership. Clan Guest membership is excluded.
- Clan authorization is revalidated by the server for access, overlap, placement, minimap visibility, and presence checks. Stored Clan display metadata is not trusted as server authorization.
- Existing Guilds ward metadata remains compatible and is upgraded to the provider-qualified representation when observed.
- If both Guilds and Clan are installed unexpectedly, automatic group authorization fails closed until only one provider remains.
- Servers and clients must run the same STUWard version.

## Github
https://github.com/sighsorry1029/STUWard
