# Changelog

## 1.3.17

- Re-release of 1.3.16 with no functional changes, retaining the Jotunn AssetManager load-order compatibility fix.
- Servers and clients must update STUWard together to satisfy the synchronized version requirement.

## 1.3.16

- Fixed a load-order conflict with Jotunn's AssetManager that could break initialization of ValheimRAFT and other Jotunn-based mods. STUWard's asset-path transpiler now runs after Jotunn's, including when AssetManager initializes later in startup.
- Preserved duplicate-path protection when Jotunn is absent and added regression checks for both patch registration orders and patch removal/re-registration. Jotunn remains optional; ward permissions, settings, and stored data are unchanged.
- Servers and clients must update STUWard together. Fully restart the game or server after updating.

## 1.3.15

- Fixed administrator debug access on dedicated servers when Server Devcommands or another server mod supplies the effective administrator decision. STUWard now validates the authenticated peer through Valheim's native administrator policy instead of comparing the raw administrator list itself.
- Bound administrator debug approval to the current connection and character, with revalidation after permission changes, reconnects, character changes, and disconnects so stale approval cannot carry into another session.
- Added reliable retry and focused `[admin-debug]` diagnostics for missing approval responses while preserving the existing RPC payloads, configuration, ward data, and requirement that actual debug mode and server approval are both active. Servers and clients must update together.

## 1.3.14

- Fixed Clan and Guild members being denied access to managed containers owned by another client on dedicated servers. STUWard now distributes short-lived, server-authoritative group snapshots and validates the live player session and character before applying remote group access.
- Added an optional container-access API for multi-user container mods and protected managed container RPCs against claimed-player identity spoofing. Existing ward restrictions, role policies, direct permitted-player access, and item ownership safeguards remain in effect.
- Fixed administrator debug map visibility not refreshing when approval changes, and rejected delayed map responses or pushes created for an outdated player or administrator state.
- InventorySlots 1.4.15 or later is required for Clan, Guild, and administrator grants to pass through its built-in multi-user chest requester checks. Servers and clients must update STUWard together.

## 1.3.13

- Simplified managed-ward initialization and placement validation while preserving Harmony targets, placement results, marker setup, overlap rules, and player feedback.
- Consolidated authoritative server request validation for ward toggles, settings, permitted-player removal, and recent-player access. Sender identity, ward validity, trust, and mutation ownership rules are unchanged.
- Reduced repeated recipe setup work by reusing the resolved Hammer table and refreshing the available-piece list once after both vanilla and STUWard recipe policies are applied.
- Removed unused compile references while retaining the same final runtime assembly dependencies and the existing ServerSync and YamlDotNet merge path.
- Configuration keys and defaults, ward ZDO data, YAML formats, RPC payloads, prefab names, and optional Guilds, Clan, TargetPortal, TameableCollector, AzuCraftyBoxes, and PortablePals integrations are unchanged. Servers and clients must update together.

## 1.3.12
- Fixed some UI problems

## 1.3.11

- Updated STUWard for Valheim 1.0.7 using the original client and dedicated-server assemblies. Updated game API calls, Harmony targets, routed-message and effect handling, and authoritative ward discovery after both regular and chunked world loads.
- Removed STUWard's Jotunn runtime dependency. Prefab registration, localization, UI resources, native asset lookup, and input blocking now use a focused implementation inside STUWard while preserving the existing ward prefab name, recipe policy, localization files, and public localization surface.
- Updated the embedded ServerSync build to the reviewed `valheim-1.0.7-r1` revision while preserving configuration keys, locks, version checks, RPC identifiers, and payload formats.
- Replaced publicized compile references with original game assemblies and cached the required private-member access through Harmony. Cleanup now continues across independent services, and frame updates remain disabled if plugin initialization does not complete.
- Existing ward ZDO data, configuration and YAML formats, permission and ownership rules, optional Guilds/Clan/TargetPortal integrations, duplicate-processing guards, and refund behavior are unchanged. Servers and clients must update together.

## 1.3.10

- Reduced redundant minimap work by reusing snapshot keys during pin cleanup and skipping server snapshot construction when the viewer revision has not changed.
- Simplified repeated Guilds cache and blocked-item checks while preserving authoritative guild results, short retries for clients still synchronizing, and item-use and attack behavior.
- Ward Settings now stops its callbacks and releases its separate UI root when the plugin shuts down. Cleanup of an old or duplicate controller no longer releases the active controller's input block, and service shutdown failures no longer skip Harmony cleanup.
- Consolidated minimap snapshot state handling and Clan map refresh calls, and removed an unnecessary ownership identity state wrapper.
- Added `DeployToGame=true` support for Debug builds, copying only the final merged mod DLL to the local game's plugin folder. `DebugQuickBuild.ps1` now uses this build path.
- Existing ward data, configuration keys, and RPC payload formats are unchanged; no migration from 1.3.9 is required. Servers and clients must update together.

## 1.3.9

- Grouped `STUWard.yml`, `STUWard.RecentPlayers.yml`, and the generated `STUWard.WardCountReport.yml` under `BepInEx/config/STUWard`, and removed the Ward Settings folder button now that the files use a conventional config path.
- There is no automatic migration or legacy-path fallback. Before first startup with this update, stop the game or server, create `BepInEx/config/STUWard`, and move the existing `BepInEx/config/STUWard.yml` and `BepInEx/config/STUWard.RecentPlayers.yml` files into it. Files left in the config root are ignored; without a manual move, STUWard creates the default policy and starts with an empty recent-player history. The generated ward-count report does not need to be moved. Existing ward ownership and registered-player permissions remain stored in ward ZDOs and are unaffected.

## 1.3.8

- Moved `STUWard.yml`, `STUWard.RecentPlayers.yml`, and the generated `STUWard.WardCountReport.yml` to the shared `<Valheim save data>/STUWard` directory so local-host data survives mod-manager profile removal and related YAML files stay together. Worlds and mod-manager profiles using the same Valheim save-data root share the policy and recent-player history.
- Added a Ward Settings header button that opens the data directory for a local host (single-player or listen server). It is hidden from remote clients and headless dedicated servers.
- There is no automatic migration or legacy-path fallback. Before first startup with this update, stop the game or server, create `<Valheim save data>/STUWard`, and move the existing `STUWard.yml` and `STUWard.RecentPlayers.yml` files into it. Files left in `BepInEx/config` are ignored; without a manual move, STUWard creates the default policy and starts with an empty recent-player history. The generated ward-count report does not need to be moved. Existing ward ownership and registered-player permissions remain stored in ward ZDOs and are unaffected.
- Deferred managed YAML loading until the authoritative server session is ready, preventing early platform-storage initialization failures while preserving Valheim `-savedir` overrides.

## 1.3.7

- Updated optional Clan integration to the current `sighsorry.Clan` plugin GUID while retaining the public API v4 ward contract. The former `sighsorry.valheim.Clan` plugin GUID is no longer recognized.
- Existing wards and individual registrations require no migration or replacement because their stored owner, registered-player, and immutable Clan identity data is unchanged. Servers and clients using Clan must install its current build together with this STUWard version.

## 1.3.6

- Added optional Clan 1.0.0 public API v4 integration. Primary Clan Leaders, Officers, and Members receive group-based ward access, overlap, placement, minimap visibility, and player-list metadata; Guests never grant automatic ward access.
- Generalized group identities so Guilds and Clan remain distinct while preserving existing Guilds ward metadata. If both providers are installed, automatic group authorization fails closed until only one remains.
- Added the synchronized `Ward Range Configuration` server setting, defaulting to `Off`. When disabled, the per-ward radius slider is hidden and the second settings page reclaims the space while preserving stored radii; enabling it restores trusted-player radius adjustment.
- Shortened valid SteamID64 values in registered and recent-player rows to their final ten digits. Full IDs remain unchanged internally and searchable, while non-Steam account IDs remain fully displayed.

## 1.3.5

- Restored a server-authoritative per-ward range slider on the second settings page. New wards still start at the largest legal radius; later expansion is clamped against the server maximum and current foreign wards without resizing neighboring wards.
- Consolidated recent-player history into one server-profile-wide `BepInEx/config/STUWard.RecentPlayers.yml` file. Existing ward registrations remain stored on their wards and are unchanged.
- Former per-world recent-player files are not migrated automatically or modified. To retain one history, stop the server before its first startup with STUWard 1.3.5 and copy the complete contents of the desired `STUWard.RecentPlayers.<worldUID>.yml` into `STUWard.RecentPlayers.yml`. Do not combine multiple files; entries older than twenty-eight days are pruned normally.

## 1.3.4

- Fixed door auto-close for slow-opening modded doors such as OdinsKingdom's `GB_Large_Portcullis`. After the shared five-second delay, STUWard now waits up to sixty seconds for the door to become interactable before issuing one close request.

## 1.3.3

- Fixed dedicated-server admin+debug ward access when another client owns the ward or container ZDO. Server-validated admin+debug state is now projected to every peer, including late-join snapshots and revocation when debug mode, admin access, or the session ends.
- Made same-guild ward access recover quickly when Guilds or remote-player identity arrives after a client enters an already loaded zone, without turning a transient no-guild result into a thirty-second denial.
- Removed legacy ward and Guilds recovery paths. STUWard now recognizes only the current `piece_stuward` prefab and current Guilds `Steam_<id>`/`name` schema, and no longer backfills missing permitted snapshots or repairs malformed stored radii.
- Removed obsolete ServerSync template, old BepInEx binding redirect, and unstripped-corelib build fallbacks. Servers and all clients must update to STUWard 1.3.3 together.

## 1.3.2

- Extended the per-world unregistered-player history from fourteen to twenty-eight days. The change is not retroactive: records already pruned under the fourteen-day policy return only after those characters reconnect.
- Fixed dedicated-server player tracking by reconciling delayed character identities periodically and immediately before building the unregistered-player list.
- Renamed the individual-registration list heading to `Registered players`.

## 1.3.1

- Simplified ward-ring visibility: placement previews and enabled wards show their ring, while disabled wards remain hidden unless highlighted as the closest placement blocker.
- Removed the unused per-ward marker-visibility state and the settings-response overlap-feedback flag from the exact-version settings protocol while retaining authoritative radius clamping and the normal placement-conflict warning/highlight path.
- Preserved registered-player identity snapshots when offline lookups are unavailable and removed quadratic recent-player filtering.
- Rejected oversized or invalid server YAML safely, bounded report and minimap payloads, and preserved the last valid client minimap snapshot when the server exceeds the supported ward count.
- Simplified runtime RPC ownership, player-list UI construction, and local deployment while making release packaging stage the manifest without modifying the tracked source file.
- This wire format is not backward compatible; servers and all clients must update to STUWard 1.3.1 together.

## 1.3.0

- Expanded the second-page behavior labels and arranged ward alert sound, ward alert visual effect, ward-range rotation, and door auto-close in a two-column grid. Range rotation defaults to on at 50% of the native marker speed.
- Added a server-authoritative, per-world list of characters seen within the last fourteen days so trusted players can add them directly to a ward. Records previously pruned by the shorter retention policy cannot be recovered retroactively.
- Restored the two-page Ward Settings layout: player management on the first page, behavior toggles and restrictions on the second, with independent scrolling lists.
- Removed confirmation popups from ward dismantling, trusted-player removal, and ward activation changes; authorized actions now apply immediately.
- Removed disabled-ward outsider self-registration and its stale vanilla opt-in/opt-out hover prompts. Trusted players now manage individual ward registration from Ward Settings.
- Recent-player history starts with this release and is not imported from logs or older STUWard data.
- Added a local full-brightness ward-ring cue that pulses for 0.5 seconds when crossing and remains active while standing within 0.75m of the boundary.
- Added a client-only boundary brightness scope setting for trusted wards, untrusted wards, all wards, or off; placement conflicts independently highlight the closest blocking ward ring for 1.5 seconds.
- Added guild, public platform account IDs, and online/last-seen status to player rows, plus independent local search fields for the registered and unregistered lists.
- Made generated `STUWard.yml` ward-limit examples directly uncommentable under the active `ward_limit_overrides` mapping.

## 1.2.9

- Unified existing-ward permissions for owners, individually trusted players, same-guild members, and admin+debug users.
- Added confirmation prompts before removing an individually trusted player, dismantling a ward, or deactivating a ward.
- Made new wards automatically use the largest legal radius up to the server maximum, yielding to older foreign wards.
- Fixed ward marker speed and brightness at their minimum settings and removed all three marker sliders.
- Simplified door auto-close to a per-ward on/off switch with one shared fixed delay of 5 seconds.
- Fixed the one-page restrictions grid failing to build because its ScrollView still had Jotunn's default vertical layout component.
- Removed the legacy ward-settings payload and door-delay migration path; servers and clients must update together.
- Combined warning toggles, individually trusted players, and the two-column restrictions grid on one page with independent scrolling blocks.

## 1.2.8

- Fixed revoked admin+debug access remaining cached and stopped treating cross-platform accounts with the same numeric suffix as the same administrator.
- Fixed Guilds membership and ward projection refreshes, including stale join/leave data and prefixed or bare Steam account IDs, while keeping other platform identities distinct.
- Fixed host-only and temporarily deferred ward minimap refreshes so local pins update without remote peers and queued server refreshes retry after index preparation.
- Reworked automatic pickup protection to run the vanilla AutoPickup method while temporarily excluding denied drops, with exception-safe item-state restoration.
- Made temporary interaction, portal, and pickup restriction scopes exception-safe so failed calls cannot affect later ward access checks.
- Fixed valid empty permitted-player snapshots being treated as invalid and bounded snapshot backfill work for stale or malformed entries.
- Hardened managed-ward placement and minimap snapshot RPCs with malformed-packet checks, request throttling, and bounded pending queues.
- Changed newly generated `STUWard.yml` files to use empty `ward_limit_overrides`; sample Steam account entries are now comments instead of active mappings.

## 1.2.7

- Fixed ward activation and deactivation VFX/SFX on dedicated servers by assigning the single networked effect spawn to the requesting client after server authorization.

## 1.2.6

- Added a server-side prefab fallback for ward toggle effects when no live ward instance was loaded.

## 1.2.5

- Fixed networked ward activation and deactivation effects multiplying by the number of nearby players.
- Simplified blocked-action warnings to flash only the nearest denying ward and coalesced each ward's warning effects into a 0.5-second client cooldown.

## 1.2.4

- Simplified authoritative ward-limit counting to scan only STUWard prefabs.
- Removed redundant global ZDO placement tracking and diagnostic logging configuration.
- Reduced diagnostic-only state and synchronization plumbing while preserving ward access behavior.

## 1.2.3

- Fixed ward owner and admin+debug controls on dedicated servers when another peer owns the ward ZDO.
- Restored authoritative per-account ward limit enforcement across world and zone loading.
- Routed managed ward settings, enabled state, and permitted-player changes through the server.

## 1.2.2

- Fixed automatic pickup and ward removal checks to follow each ward's effective restriction and owner-control rules.
- Hardened server-authoritative guild, RPC, map-state, and permitted-player synchronization.
- Made core Harmony patch startup transactional and simplified duplicated config, map-state, registry, and snapshot code.

## 1.2.1

- Added per-ward restriction toggles with server-side forced/not-forced controls.
- Separated hammer-placed consumable and feast consumption from normal item pickup.
- Simplified hostile creature structure protection config surface.
- Cleaned up ConfigManager ordering, patch safety, UI layout, and diagnostic logging.

## 1.2.0

- Refactoring and optimizations.

## 1.1.9

- Fixed errors that could happen with floating itemdrops within the ward area.

## 1.1.8

- Refactored some codes.

## 1.1.7

- Added config option for hostile creature to not damage warded pieces always.

## 1.1.6

- Fixed ward icons sometimes not showing on map on dedi.

## 1.1.5

- TSV file is no longer needed, and YAML files are integrated into one `STUWard.yml`.
- Organized configs.
- Refactored some codes for better performance.

## 1.1.4

- Fixed wards not being able to place close enough to each other.
- Fixed ward range not showing on map.
- Fixed some guild related bugs.

## 1.1.3

- Fixed guild members not having authorities on guild wards on dedi.
- Updated README with more specific info.

## 1.1.2

- Fixed some errors that would happen when placing ward.
- Resolved framedrop while holding preview of the ward.
- Players can view ward icons even though they didn't load the zone after server reboot.
- Ward circle projection segment count was reduced to 36 from 80.
- Refactored some per-check codes.

## 1.1.1

- Admin+debug mode has the same authority as the ward owner.
- Added ward icon and range on map and minimap for guild members, registered players, and owners.

## 1.1.0

- Added server-synced `BepInEx/config/STUWard.ItemPrefabs.yml` for item prefab policy.
- Added `Pickup Block Mode` config: `BlockAllExceptWhitelist` and `AllowAllExceptBlacklist`.

## 1.0.9

- Fixed some guild related behaviors.

## 1.0.8

- Reduced some excessive logs that could happen on dedi.

## 1.0.7

- Fixed the E button not working on dedi.
- Fixed guild related behaviors on dedi.

## 1.0.6

- Improved overall performance.

## 1.0.5

- Changed the method of E button appearing on players.

## 1.0.4

- Fixed the E button not working on server.

## 1.0.3

- Pieces within active area don't get damage from mobs when no trusted player is near ward area.

## 1.0.2

- Fixed guild players not having authorities on guild wards on dedi.
- Fixed debug mode not working for the ward on dedi.
- Made it so that players and tamed animals won't damage pieces within the protected area.
- Feasts are no longer consumable within the ward area.

## 1.0.1

- Only the owner and debug admin can remove the ward.
- The ward itself is invulnerable to damage.

## 1.0.0

- Initial release.
