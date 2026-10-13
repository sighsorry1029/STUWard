# STU Ward user guide

[Back to overview](../README.md)

Detailed permissions, server settings, and integration requirements.

[Permissions](#permissions) · [Registration](#registration) · [Overlap](#ward-overlap) · [Ward settings](#ward-settings) · [Item policy](#item-policy) · [Integrations](#integrations)

## Protection

Inside an enabled ward, the configured restrictions can block unauthorized players from:

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
- Players matching the ward's authorized Guild or primary Clan identity while that integration is enabled
- Server admins using effective debug control

All trusted players can use the protected area, open and change ward settings, toggle the ward, add or remove players from its individual registration list, and dismantle the ward. Authorized changes apply immediately without confirmation popups.

The owner identity is still retained for ward limits, reporting, and group metadata; it does not grant a higher permission tier on an existing ward.

You can recover your own tombstone inside an STU Ward without being trusted by that ward. This applies to opening the grave and taking all its contents. Ownership uses the deceased character's stored ID, so another character on the same account does not receive the exception. Ordinary chests and other players' graves keep their existing access restrictions.

STUWard supports either `Guilds` or `Clan 1.0.0+` (`sighsorry.Clan`, public API v4) as its group provider. Clan `Leader`, `Officer`, and `Member` roles are authorized through the primary Clan; `Guest` connections never grant automatic ward access. Individually registered, owner, and admin-debug trust continue to work independently of group membership. An older or incompatible Clan API fails closed for automatic group authorization.

The server-synchronized `Guilds Integration` and `Clan Integration` settings in `1 - General` independently control automatic group trust. Both default to `On` and use the existing configuration lock. Turning one `Off` disables that provider's automatic access, ward management, map visibility, and same-group overlap allowance in STUWard; the Guilds or Clan mod itself continues working normally. Players who are individually registered, own the ward, or have effective admin-debug control keep their access. Group labels show `-` while integration is disabled. Stored ownership, registrations, and group metadata are retained; enabling integration again rechecks membership.

Before disabling integration in a shared base, register the intended users on every ward covering that base. Already overlapping wards keep their radius, but access to the shared area requires permission from every covering ward. Disabling integration does not revoke an existing individual registration. No automatic registration copying or data migration is performed.

## Registration

Individual registration is managed by trusted players in Ward Settings:

- The server stores structured policy in `BepInEx/config/STUWard/STUWard.yml` and keeps one recent-player history in `BepInEx/config/STUWard/STUWard.RecentPlayers.yml`.
- The list includes authenticated characters currently online or seen within the last twenty-eight days, with online characters first and older activity lower in the list.
- Registered and recent-player rows show the character name, resolved Guild/Clan group, account identity, and online/last-seen status. SteamID64 values are displayed as their last ten digits to save space; other platform IDs remain unchanged, and the full account ID remains searchable. Registered characters without retained activity show that their last-seen time is unavailable.
- Trusted players can add a recent character to the ward or remove an individually registered character.
- Registration is character-specific because ward permissions use Valheim player IDs.
- Disabled wards do not allow outsiders to register themselves.

Recent-player history begins when STUWard 1.3.0 is installed. Earlier visits are not imported, and extending retention from fourteen to twenty-eight days does not restore records that were already pruned. Those characters must reconnect before appearing again.

These auxiliary YAML files are grouped under the active BepInEx installation or mod-manager profile's `config/STUWard` directory. Remote clients do not create the server policy or recent-player files; a remote admin creates only a local `STUWard.WardCountReport.yml` after requesting a ward report. Deleting a mod-manager profile also deletes its STUWard YAML files, while existing ward ownership and registered-player permissions remain stored in the world.

## Ward Overlap

Ward overlap is strict.

- Foreign wards cannot overlap.
- Same-owner wards can overlap.
- Wards with the same exact provider-qualified Guild/Clan identity can overlap while that integration is enabled.
- Registered-player access does not bypass overlap rules.

When placing a new ward, older foreign wards keep their radius and the new ward automatically yields to the largest non-overlapping radius. `Ward Range Configuration` is a synchronized server setting that defaults to `Off`. When enabled, trusted players can explicitly shrink or re-expand a ward from Ward Settings, but the server clamps expansion to the currently available non-overlapping radius without changing neighboring wards. Removing a neighboring ward or increasing the server maximum does not silently expand it. Lowering the server maximum clamps existing wards.

Losing group access does not automatically shrink existing wards. Saving unrelated settings preserves their radius, explicit reductions remain allowed, and an expansion without enough free space leaves the current radius unchanged. The server maximum still applies.

In overlapping coverage, access is additive: if any enabled foreign ward denies the player, the action is denied.

## Ward Settings

The server's `2 - Ward Restrictions` settings default to `ForcedOn`. Set a restriction to `NotForced` to let trusted players toggle it for individual wards. Range adjustment is controlled separately by `Ward Range Configuration`, which defaults to `Off`.

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

Edit the `item_prefab_policy` section in the server's `BepInEx/config/STUWard/STUWard.yml`. Use prefab names, not translated item names. For example:

```yaml
item_prefab_policy:
  blocked_item_prefabs:
    - FLG_TamingOrb
  pickup_whitelist:
    - Wood
  pickup_blacklist: []
```

The pickup mode in the main config selects which pickup list applies. With STUWard 1.4.0+ and FeedLikeGrandma 1.1.7+, the Orb check also covers the capture target or release position, including use from outside a ward. Trusted access still applies; adding a prefab does not ban it everywhere.

## Map Pins

Ward pins can show ward locations and active ranges on the map.

Players normally see wards they are allowed to see. Admin debug control can show all managed wards.

## Integrations

- **EpicLoot:** enchanting-table opening, its seven main actions, and upgrade requests follow the existing Crafting Stations restriction. A denied action cancels before material use. Already paid Augment choices can still finish. An unsupported EpicLoot API logs a warning and leaves this integration inactive.
- **FeedLikeGrandma:** update both mods on the server and all clients. The Orb checks player and target positions against the server's blocked-item list. If STUWard is installed but its required API is missing, new Orb actions are blocked. Completed actions can still resolve through their existing recovery path.

The recent EpicLoot and Orb changes have build and automated-check coverage; actual game and multiplayer acceptance is still pending. See the [compatibility verification notes](../build/WardCompatibility.md) for the exact scope. This is separate from a claim of support for every future version of those mods.

## Technical details

- Compatible PortalRules builds defer portal damage and dismantling protection to STUWard inside enabled wards. Portals can still collapse if they lose structural support. Outside active coverage, PortalRules applies its own portal protection setting. Both mods must be updated on the server and clients; portal travel permissions are unchanged.
- Optional integrations can query `WardAccessApi.IsInsideActiveWard(Vector3)` for current, locally loaded active coverage. This is a coverage query, not a player permission grant; callers must preserve STUWard's action and RPC checks.

- Ownership metadata is based on the ward creator player id.
- Account identity is used for limits and reporting, not as a separate permission tier.
- A ward's provider-qualified group identity is projected from its owner's authoritative Guilds or Clan membership. Clan Guest membership is excluded.
- Clan authorization is revalidated by the server for access, overlap, placement, minimap visibility, and presence checks. Stored Clan display metadata is not trusted as server authorization.
- Existing Guilds ward metadata remains compatible and is upgraded to the provider-qualified representation when observed.
- If both Guilds and Clan are installed unexpectedly, automatic group authorization fails closed until only one provider remains.
- Servers and clients must run the same STUWard version.
