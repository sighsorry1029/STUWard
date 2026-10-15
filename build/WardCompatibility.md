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

## Outpost Ward implementation (1.4.2, 2026-10-15)

Baseline: `7e0e01b` (1.4.1), clean `main`. Added `piece_stuward_outpost`, with
fixed teal model emission/active effects and an independently cloned boundary
segment template. Original ward materials and segment assets remain unchanged.
MaterialMan can restore the teal defaults after placement/repair highlighting;
the headless server registers the same identity without building visual materials.
Both templates use the existing inactive-root initialization and shutdown cleanup.

Both ward types use CFG for scalar defaults and YAML for account overrides.
`Max Outpost Ward Radius` (16) and `Max Outpost Wards Per Steam ID` (3) use the
same General section, ServerSync bindings, configuration lock and radius-change
handler as the regular settings. `outpost_ward_limit_overrides` uses the same
validation and server authority as `ward_limit_overrides`; each selects only its
own type's quota. No Outpost-specific synchronization service or legacy
`outpost_ward` YAML parser/migration remains. Failed live YAML edits keep the last
valid snapshot. Existing regular overrides and CFG keys retain their scope.
Outpost radius reads, placement preview, server mutation, saved-radius shrinking,
spatial queries and GUI limits now use its own cap. The open GUI also refreshes
its maximum when the saved radius itself has not changed.

Two-prefab discovery uses the existing cached `GetWorldObjects()` accessor.
Original 1.0.20 C# and IL show that `GetAllZDOsWithPrefabIterative` can return
sector 0 twice and repeat a batch-boundary sector (break before index increment).
The ID dictionary avoids counting one ward twice. This corrects an existing
collection defect as part of implementing reliable separate quotas; it does not
change account authentication, access policy or the one-time refund marker.
Persisted/offline wards of both kinds enter the initial accepted set. Reports
keep the regular fields and add separate Outpost count/limit/override fields.
Public access APIs and saved setting keys are unchanged; protection/integration
paths are shared through managed identity.
Both kinds reach large-map and minimap location/range rendering. Snapshot entries
now carry an Outpost boolean through full/delta RPC payloads; all peers must use
the same updated build. Outpost pins select a cached teal copy of the ward icon.
The original 1.0.20 `Piece.m_icon` references guardstone (64x64, unrotated atlas
rectangle, non-readable BC7). GPU readback copies only that rectangle, preserving
alpha and intensity while tinting RGB. Temporary render target and sRGB-write
state are restored; owned texture/sprite are destroyed on source replacement,
world teardown and plugin shutdown. Failed creation is cached for that source.
Ordinary icons and white dashed range rings retain their styling; pin names stay
empty. Remote server updates and opening the large map refresh
the snapshot. Some host/local mutations while the large map is closed retain
the existing delayed display refresh behavior. This work does not change it.

`Outpost Ward Recipe` is a separate synchronized CFG setting with default
`Ectoplasm:3,Iron:5,Chitin:7,ElderBark:9`. Each item was verified in original
1.0.20 bundle `c4210710` as a GameObject with ItemDrop and the matching item token.
`ElderBark` has display name Ancient Bark. Recipe parse failures preserve the
affected type's previous recipe; empty overrides retain the vanilla fallback
policy. Placement rejection reads that type's recipe and keeps the refund-once
guard. New default YAML includes `FLG_TamingOrb`, verified from FeedLikeGrandma's
`TamingOrbSystem.PrefabName`. Explicit existing YAML is never merged or rewritten.

Verification performed:

- Baseline and final Debug build with `DeployToGame=true`: zero warnings/errors.
  Final merged DLL deployed to the normal Valheim `BepInEx/plugins` directory;
  source and destination SHA-256 both
  `E6ABFDDF2B2480445B210C7AEB0664ADC972F76FDD78EA381444ED6F2F04F9DB`.
- Release 1.4.2 build: zero warnings/errors. Thunderstore and Nexus ZIPs have
  exactly the expected seven/two entries, all matching their source files. Both
  contain the final merged Release DLL with SHA-256
  `AA3C0FBE1818B359D1285FC5D15C4312EEA1B8CD3B9E05137149C8F9FF77F816`.
  Assembly version is 1.4.2.0; plugin, file and manifest versions are 1.4.2.
  ServerSync/YamlDotNet are merged, and no game/reference DLL is packaged.
- xUnit: 110 baseline, 115 final, including mixed 16/32 m overlap, minimum-space
  rejection and radius shrink/re-expansion policy cases.
- Existing source-linked access checks: 13 item-use and 20 grave/container cases.
- Final merged Release DLL: 3,483 managed/IL checks and 122 Harmony target lookups for
  **each** original 1.0.20 role: client build 25909357 and server build 25909431.
  These include actual merged YAML parsing, omitted/empty maps, independent CFG
  fallback selection, per-type overrides including 0/unlimited, invalid account
  IDs/counts, duplicate/normalized-duplicate/unknown keys, oversized maps and
  unchanged active snapshot on parse failure, prefab-hash classification,
  serialized ghost-kind metadata and embedded English/Korean token presence.
  The original game's ZPackage also round-trips mixed regular/Outpost entries
  at equal radii; truncated kind, invalid radius and invalid counts are rejected.
  Managed tint checks cover alpha/intensity preservation. GPU readback itself
  remains an in-game check. The preview uses the original guardstone sprite from
  bundle `6a33a62` and the same RGB tint, without changing any game asset.
- Installed client `assembly_valheim.dll` SHA-256 matches the preserved original
  `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.
  Server original SHA-256:
  `EF8C8D3A1F45694EF7A701E8E2A25BF8A3874C951A2639C81DFE614CE3AA5FA3`.
  Existing global snapshots/extractions were reused. Relevant original prefab
  resources and PrivateArea/MaterialMan/CircleProjector/ZDOMan code were inspected;
  no game DLL was publicized or replaced.

An attempted standalone call through the full ownership/config entry point
reached ServerSync's BepInEx/Unity initialization and could not run outside the
game. The executable checks the snapshot's actual policy selection instead;
it does not simulate a running server or claim end-to-end quota verification.

Actual game/host/dedicated multiplayer acceptance is still required:

1. Install the same updated DLL on all peers. Place three regular plus three
   Outpost wards; reject only the fourth of each kind and refund materials once.
   Repeat on another character of the same account, with disabled/unloaded wards,
   reconnects, world restart and delayed/duplicate placement notifications.
2. Check English/Korean Hammer entries, independent recipe changes, fixed teal runes,
   effects and rings, invalid-to-valid placement highlighting, repair/dismantle
   highlights, enable/disable, distance/LOD and leaving/re-entering worlds.
   Verify Outpost's 3/5/7/9 costs, its rejection refund and both recipe live edits.
3. Use regular cap 8 / Outpost cap 16, then the reverse. Change the Outpost cap
   while its settings window is open; verify no unsolicited configuration RPC.
   Lower 16 to 12, save/reload, then restore 16: stored wards remain at 12 until
   explicitly expanded. Verify both CFG values arrive on remote clients and
   respect the configuration lock. Test independent YAML overrides, live reload
   and malformed YAML after a previous local-server session.
4. Verify mixed-type overlap and normal owner/registration/Guilds/Clan/admin
   access, remote map pins and own-tombstone recovery. Confirm no resource loss
   or duplication on placement rejection and that ordinary chests remain guarded.
   Check teal icon alpha/color at both map sizes, at distant/unloaded wards,
   after map switching and reconnecting, and regular icons remaining unchanged.

Actual game sessions and performance measurements have not been performed.
