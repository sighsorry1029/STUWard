# Shared chest authorization patch

Baselines: STUWard `471952a` (1.3.13), InventorySlots `a235330` (1.4.14). Use matching patched DLLs on server and clients and restart. Versions are not bumped and no Release packages are produced by this patch.

InventorySlots' shared-open and write-ownership handoff used an extra ward check accepting only creator/explicit registration. STUWard also could not query remote Clan members on owner clients because Clan.ResolveWardAuthorization is server-only. The fixes preserve shared viewing, write leases, distance, ownership revisions, tokens, duplicates, item operations, and the separate external MultiUserChest boundary.

Guilds source: [API.cs at d9133c40ecbf842dcfc1ac617eeb476eb96771a9](https://github.com/blaxxun-boop/Guilds/blob/d9133c40ecbf842dcfc1ac617eeb476eb96771a9/Guilds/API.cs), version 1.1.14. GetPlayerGuild(PlayerReference) works on the server and matches platform ID plus character name. IsLoaded is not sync readiness. Authenticated reference lookup now precedes Player fallback; periodic resolution repairs initial sync gaps. Guilds itself is unchanged. Clan primary Leader/Officer/Member authorization, Guest exclusion and conflicting-provider denial remain.

## Runtime contract

- `STUWard_RequestGroupAccessV1(long requestId)` accepts authenticated connected peers, never membership claims.
- `STUWard_GroupAccessV1(ZPackage)` carries requestId, count, then playerId, connection ID, character ZDOID, provider, group ID and name. Only the current server may respond.
- Latest-request matching, atomic full replacement, duplicate rejection and size limits prevent partial/stale updates. Missing rows remove membership. Lookup also matches the live character's network owner and provider.
- Clients poll every second with a two-second pending timeout. Server data refreshes each second and is invalidated by group/session events. Trust expires five seconds from request time; delayed responses cannot renew it. Revocation is not instantaneous: server cache age plus client lifetime can retain membership for roughly six seconds plus scheduling delay. Missing/expired group data fails closed; owner, explicit permit and admin remain independent.
- World reset/shutdown clear caches. The request sequence survives world changes. Disconnect removes its rate-limit entry and invalidates server data. No player ZDO/config/save format changes.
- Managed Container RequestOpen/Stack/TakeAll validates sender against playerID before InventorySlots. Unprotected locations and existing response/ownership/item operations retain their behavior.
- Public `STUWard.WardAccessApi.TryCheckContainerAccess(Container, long, out bool)` returns whether managed protection handles the location and its decision. `IsManagedWard(PrivateArea)` identifies managed areas. Callers must authenticate requesters and preserve distance, vanilla protection and mutation checks. InventorySlots binds once through a soft dependency, without a direct STUWard DLL reference.

## Verification and remaining execution

Both Debug builds use DeployToGame=true and final merged DLL hashes match local plugins. STUWard passes 55 domain cases, including replacement/revocation, session/character/provider mismatch, expiry, stale/duplicate responses and malformed rows. Its original 1.0.7/1.0.12 client/server verifier also executes the production packet parser with original ZPackage, without Unity initialization.

InventorySlots passes 167 checks. The production adapter/requester gate is linked into tests with engine/plugin stand-ins covering missing dependency, delegated trust, privacy denial, managed denial precedence, vanilla overlap and API exceptions. Original 1.0.12 client/server metadata checks cover 1,014 references, 134 Harmony targets and 40 reflection contracts without failures; eight pre-existing manual entries remain.

Actual game/host/dedicated multiplayer execution has not been performed. Test two clients with either owning the chest: open, drag/split, quick stack/restock, take-all, sorting and exact saved item totals after reconnect. Repeat Clan/Guilds separately, Guests/nonmembers/direct permits, leave/kick/role changes, delayed/duplicate replies, owner changes, mixed vanilla/managed wards, Containers protection off, sharing off and admin debug. Test map approval/revocation while open or closed, including delayed ordinary/admin snapshots. Static/isolated checks do not prove these runtime cases.
