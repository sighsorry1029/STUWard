# Changelog

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
