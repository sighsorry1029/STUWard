# Dedicated-server admin + debug authorization

## Scope and evidence

Patch baseline: STUWard 1.3.14, commit `40f60b4`. This is a Debug-only follow-up; the already published 1.3.14 Release ZIP does not contain it. InventorySlots and Server Devcommands are unchanged.

The supplied Server Devcommands package is 1.113.0 (BepInPlugin version `1.113`). Its `ServerDevcommands.dll` SHA-256 is `72E6F0804B7BF9D508C4FAA0E56C9262B9D808F60D898A7857417EC274F4CFD3`. Original DLL inspection shows a Harmony prefix on `ZNet.ListContainsId`: for the server administrator list, `PermissionLoader.Data.ResolveAdminOverride(hostId)` may explicitly allow or deny the authenticated account. STUWard's former direct `GetAdminList()` string comparison bypassed this policy. Its generic account cache could also lose non-Steam platform prefixes in the socket fallback.

Original Valheim sources and metadata were reused from `C:/Users/blizz/.codex/references/valheim/snapshots/`, under each snapshot's `derived/ilspy-9.1.0.7988-r1/assemblies`:

- 1.0.12 client `client-b25253764-windows-x64-20260911T154617Z`; server `dedicated-server-b25253791-windows-x64-20260912T115223Z`.
- 1.0.7 client `client-b25185596-windows-x64-20260909T131109Z`; server `dedicated-server-b25185644-windows-x64-20260909T131109Z`.
- `assembly_valheim`: `ZNet.IsAdmin`, `ListContainsId`, `GetAdminList`, `ZNetPeer`, Steam/PlayFab socket host identities and `Terminal` debugmode.
- `assembly_utils`: `SyncedList.Contains/CheckLoad`; `Splatform`: `PlatformUserID` identity filtering.

`ZNet.IsAdmin(string)` is public in all four originals and uses the game's administrator policy, including live list reload checks and platform/display-ID matching. The 1.0.7 native `ListContainsId` overwrites a prior match when checking a filtered ID; 1.0.12 preserves it with OR. The patch intentionally respects the active game's and server mods' final decision. There is no second raw-ID fallback after denial: that would override Server Devcommands' explicit `admin=no`. Original API/IL checks passing on 1.0.7 do not prove identical permission results across game versions.

## Implementation and preserved boundaries

- A ready, current server peer and authoritative sender-to-character mapping are required. Initial approval calls `ZNet.IsAdmin` with that peer's unmodified `m_socket.GetHostName()`. No character/group account cache, client admin claim, or platform-stripped ID is used.
- Approved players are bound to the live server peer. Access revalidation uses the same native decision and current player mapping. Policy revocation, changed characters, replaced connections and disconnects cannot carry that binding forward. Session reset releases all bindings and diagnostics.
- Existing request/projection/snapshot RPC names, signatures and payloads are unchanged. Runtime registration follows the actual routed-RPC instance. Requests wait for a server peer and retry missing replies, including debug-off requests. A late enabled snapshot after debug-off keeps the disable request eligible for retry.
- Local `Player.m_debugMode` and server approval are both required. Server Devcommands' `devcommands`, fly, no-cost, `Access warded areas` or character-specific command permissions do not by themselves become full STUWard authority. Account-level `admin=yes/no` recognized by the native server check is respected. No new optional dependency or configuration key is introduced.
- The remaining `IsAdminAccountId` caller for ward reports retains its existing policy; this patch is scoped to admin + debug access. Ward data, role rules, mutation ownership, multi-user chest leases and item transfer code are unchanged.

## Diagnostics and reproduction

Search client and server `LogOutput.log` for `[admin-debug]`.

- Client: `Local debug requested=True/False` records the actual game flag; `Server approval=True/False` records the authoritative result. `No server approval response` starts after roughly 10 seconds without a reply and repeats at most every 30 seconds while retrying.
- Server: one message per changed decision includes sender, player ID, bounded socket account ID, request state and `approved`, `not-server-admin`, `identity-not-ready`, `peer-not-ready`, `host-id-unavailable`, `admin-check-error:<type>` or `debug-off`. Identical heartbeats do not repeat it. No full administrator list or exception payload is logged.
- The supplied Gale profile has `Automatic devcommands=true`, `Automatic debug mode=false` and `Access warded areas=true`. These are observations of that local profile, not the unknown dedicated server configuration. Use the explicit debugmode command and confirm `Debugmode True` when testing the existing admin + debug contract.
- Install this patched STUWard DLL on the dedicated server and clients, restart, and test an unrelated player's managed ward: map visibility, enable/settings, door, building/removal and container access. Built-in InventorySlots shared chests still require InventorySlots 1.4.15 or later. Test native/server-mod allow and explicit deny, debug-off, admin revocation, reconnect and a different character; verify no non-admin or debug-off privilege remains.

## Verification limits

Baseline Debug build and 55 domain tests passed. The final test suite passes 74 cases, including 19 additional cases linking the production handshake source with engine/transport stand-ins. They cover external native allow/deny/error, exact socket identity, identity delay, unknown sender, revalidation, disconnect/reset, missing server/reply, disabled-state retries, character replacement and diagnostics. These tests do not reimplement or execute the game's native administrator policy.

Debug builds use `DeployToGame=true`, followed by final DLL SHA-256 comparison with the local plugins copy. Original client/server 1.0.7 and 1.0.12 metadata/IL checks verify public access and Harmony targets against the merged DLL. No original game DLL is publicized or modified. No game, dedicated-server handshake, crossplay login, Server Devcommands permission file, native `SyncedList` file reload or item transfer was executed in this verification. Those remain runtime checks; the supplied DLL proves the policy mismatch but does not identify which state failed on the user's server.
