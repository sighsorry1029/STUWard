# Ward compatibility checks (2026-10-13)

## Changed behavior

- `WardAccessApi.IsItemUseAllowed` applies the existing blocked-item list at both
  the authenticated player's position and the action's target position. It uses
  the current loaded-ward evaluator, including ordinary trusted access. It is
  not a query for unloaded wards or a replacement for caller authentication.
- FeedLikeGrandma consumes this API through an optional cached delegate. Both
  patched DLLs are needed on the server and clients. No hard mod dependency,
  config key, item save format, or Orb RPC format was added.
- EpicLoot's table UI, seven actions and owner-side upgrade request now follow
  the existing **Crafting Stations** restriction. The table's position is used.
  Paid augment choices and completed upgrade responses remain unchanged.
- EpicLoot integration is optional and resolves all targets before patching.
  Incompatible bindings warn and skip this integration; they do not provide
  protection. Look for `EpicLoot enchanting table ward protection enabled.`

## Automated verification

The build references the installed original Valheim **1.0.17** client (Steam
25730771); this was confirmed from `Version` metadata, not the older README.
Checks used that client and the preserved **1.0.16** dedicated-server snapshot
(25527701), without publicizing or changing the project's supported-version policy.
The supplied EpicLoot plugin identifies itself as 0.14.13; its
assembly version is still 0.13.0. DLL SHA-256:
`802790E0CBD8A6F951627619A488B84E6A406E8770E2687B2FFF8B63D2ADF9EA`.

- Debug build/deploy and Release 1.4.0 build: zero warnings/errors. Deployed Debug
  DLL hashes matched; Release package DLLs and staged documents matched their sources.
- Existing xUnit suite: 110 passed.
- Managed/IL checker: 3,305 checks and 121 required target lookups, for each game role.
- EpicLoot: 39 target/prefix binding checks per role, covering all nine hooks.
- Source-linked item-access API: 13 checks for actor/target boundaries, trusted
  decisions, unlisted items, inactive wards, invalid IDs/coordinates and readiness.
  Its ward evaluator is a fixture; it does not test live discovery or group trust.

```powershell
dotnet build StuWard.sln -c Debug -p:DeployToGame=true
dotnet test STUWard.Tests/STUWard.Tests.csproj -c Debug --no-build
dotnet run --project build/WardItemAccessChecks/WardItemAccessChecks.csproj -c Debug
dotnet build build/CompatibilityCheck/CompatibilityCheck.csproj -c Debug
& '<Unity Mono>/mono.exe' build/CompatibilityCheck/bin/Debug/net48/CompatibilityCheck.exe `
  bin/Debug/STUWard.dll '<original Managed directory>' '<BepInEx core directory>' `
  --epicloot '<original EpicLoot.dll>'
```

The optional checker preloads selected original game assemblies and verifies
their paths. It checks bindings, not detour installation. An attempted standalone
EpicLoot detour test reached `Player`/`ZSyncAnimation` native Animator initialization
and failed because Unity was not running. Actual Harmony installation remains
an in-game acceptance item; no altered game DLL was used to bypass that limit.

## In-game acceptance still required

Use matching patched STUWard/FeedLikeGrandma on server and clients; restart them.

1. Add `FLG_TamingOrb` to `item_prefab_policy.blocked_item_prefabs`. Check capture
   and release for outside/outside, outside/inside, inside/outside, inside/inside.
   Repeat as an outsider, owner, registered player, group member and admin-debug
   user, with overlapping and disabled wards. Unlisted items keep their policy.
2. Change access or move the capture target during the 4.5-second effect. Repeat
   with delayed results, slot moves, reconnects and network-owner migration.
   Verify the combined live-creature plus filled-Orb total remains one.
   Release is checked on initial server approval and again locally before spawn;
   this patch does not add a final server-authoritative spawn handshake.
3. With Crafting Stations restricted, outsiders must not open EpicLoot's table.
   Open while trusted, revoke access, then try each action. No materials or items
   should change on denial; the progress controls must unlock. Repeat with the
   restriction disabled and with a remote client owning the table's ZDO.
4. Check upgrade denial/success, pending callback cleanup, and an already paid
   Augment choice completing normally. EpicLoot's existing failed-upgrade response
   can display a success-like message: verify actual level and material counts.
5. Start with each optional mod absent. Confirm no required dependency was added.

The managed/IL and EpicLoot binding checks also passed against the final merged
Release 1.4.0 DLL. Manifest, assembly, plugin and file versions were verified;
both Release ZIPs contain the matching DLL and expected package files.
No game, host, dedicated-server session or multiplayer transport was executed.

## Own-tombstone recovery follow-up (1.4.1, 2026-10-13)

The tombstone's stored `ZDOVars.s_owner` character ID now grants recovery through
STUWard restrictions. The check requires the exact `Container` used by the
same-object `TombStone` and a valid network view; a network owner, matching chest
owner field, parent/child container, or second container does not qualify.
Outside managed coverage, the public container API still defers to its caller.

Both `Container.Interact` and the automatic `Container.TakeAll` path enter a
temporary managed-ward allowance for the matching grave owner. Their finalizers
preserve exceptions and clear their own scope. `Container.CheckAccess` uses the
same ownership rule, including on the RPC receiver. Existing RPC requester
authentication, busy checks, TakeAll throttling, ownership transfer, item movement
and tombstone cleanup are unchanged. No new setting or saved field was added.

- Baseline and patched Debug builds/deploys, and Release 1.4.1: zero warnings/errors;
  deployed Debug DLL SHA-256 matches the build output. Both Release ZIPs contain
  the matching final DLL and expected package files; manifest, assembly, file and
  plugin versions agree on 1.4.1. Embedded library and optional dependency checks passed.
- Existing xUnit suite: 110 passed. Source-linked API checks: 13 existing item-use
  cases and 20 grave/container cases passed, using component/ZDO/ward fixtures.
- Original client 1.0.17 and dedicated-server 1.0.16 API/IL checks: 3,337 checks
  and 122 target lookups per role, including the final merged Release DLL. The compiled skip/finalizer paths also passed
  prior-denial, exception, nested-scope and repeated-cleanup checks without Unity.
- Original metadata confirms `TombStone.GetOwner` is private; the patch uses
  public ZNetView/ZDO access instead. The native interaction, owner ID and
  container retrieval methods inspected in both original DLLs have identical IL.

Actual game acceptance remains required with the patched DLL on server and all
clients: recover an own grave with space for TakeAll and with a full/overweight
inventory requiring the grave UI; verify ordinary chests and other graves stay
blocked. Repeat with overlapping wards, another peer owning the grave ZDO,
disconnect/reconnect and duplicate requests. Confirm no item loss/duplication and
normal empty-grave cleanup. InventorySlots excludes graves from multiuserchest;
this follow-up does not change that policy or add concurrent grave access.
