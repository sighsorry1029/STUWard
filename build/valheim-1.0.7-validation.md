# STUWard 1.0.7 / Jotunn removal — 2026-09-10

Based on repository HEAD `17993cd`, with the current uncommitted compatibility
changes. The Release version is **1.3.11**. Release ZIPs were built; no commit,
push, uploader inspection or manual publication was performed.

## Change boundaries

- Removed the Jotunn assembly reference, build requirement, hard plugin dependency
  and manifest entry. BepInExPack dependency corrected to **5.4.2350**.
- Native `ZNetScene.Awake` registration of the same `piece_stuward` clone, created
  under an inactive root so its network/ward Awake callbacks do not run on the
  template. Hammer registration, recipe overrides, default enabled/dungeon policy,
  vanilla ward recipe toggle and placement notification stay in StuWardPrefab.
- Localization keeps the existing files, merging, English fallback and public
  Localizer surface; it now applies words after native SetupLanguage. The
  SetupLanguage argument is used because startup language selection may not yet
  be reflected in the global selected-language state.
- WardGuiController keeps the configuration drafts, requests, pages, search and
  row state. WardUiResources owns only widget/style resources, native GUI canvas,
  asset references and input guards. Extended-manifest availability and duplicate
  asset-path handling formerly supplied by Jotunn remain available. Only requested
  UI assets are loaded, with explicit reference release on scene teardown.
- Input guards OR STUWard's visible state with existing menu/text-input decisions;
  they do not decrement another mod's input counter or force its cursor state.
- Original 1.0.7 game references replace stale publicized inputs. Public methods
  are used for inventory contents, player lists and hovering. Required private
  fields/methods use cached, typed Harmony accessors in WardGameAccess; private
  patch targets are named explicitly and checked against original metadata.
- Fixed ServerSync `valheim-1.0.7-r1` is vendored through local package **1.0.1**,
  keeping the existing internal merge. See Libs/README.md and manifest for hashes.
  Recompilation also selects new Message/EffectList/ConsoleCommand signatures and
  the constant Everybody value in the mod's own broadcast path.
- Both native ZDO Load and LoadChunks completion invoke the existing authoritative
  initial observation. No mod migration or old game runtime branch was added.
- Cleanup attempts every independent service even after a previous cleanup fails.
  The plugin does not run its Update services unless Awake completed successfully.

Protection/overlap/damage policies, configuration keys, ZDO keys, prefab name/hash,
RPC names/payload order, authoritative checks, ownership, deduplication and refund
logic were not intentionally changed. Guilds/Clan/TargetPortal and other optional
integrations were not removed. Other installed mods can still require Jotunn.

## Performed

| Verification | Result |
| --- | --- |
| `dotnet build StuWard.csproj -c Debug -p:DeployToGame=true` | Succeeded, 0 warnings / 0 errors, including ILRepack and final plugin copy |
| `dotnet build StuWard.csproj -c Release` | Succeeded, 0 warnings / 0 errors, including ILRepack and Thunderstore/Nexus ZIP creation |
| Domain xUnit project | 49 passed, 0 failed |
| Final merged DLL + original client references | 3,057 managed/IL assertions, 118 required Harmony target lookups passed |
| Same final DLL + original dedicated references | 3,057 managed/IL assertions, 118 required Harmony target lookups passed |
| Final assembly dependencies | No Jotunn reference/hard dependency; no old ZRoutedRpc.Everybody field operand |
| Direct game calls/fields | Resolved and public against original game/GUI/SoftReference metadata |
| Input transpilers | Applied to actual original InventoryGui.Update and GameCamera.UpdateCamera IL; unknown body rejected; other input blocks retained |
| Asset lookup | Original path-list IL transformed; duplicate path keeps first identity and null paths are ignored |
| Deployment | Build and installed final STUWard.dll SHA-256 match |
| Git whitespace check | Passed; only local Git line-ending conversion notices |

Output: `C:/Users/blizz/RiderProjects/STUWard/bin/Debug/STUWard.dll`

Installed: `C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/plugins/STUWard.dll`

SHA-256 for both:
`0d9431609be04fd859eb82cf33c0bb0cba0d16720d3821fbf16198f71b79479b`

Release output: `C:/Users/blizz/RiderProjects/STUWard/bin/Release/STUWard.dll`

Release DLL SHA-256:
`30a2d47f7b6740a4c6d4086f333239d8e1f6aecf1edce989664f569e14942939`

Thunderstore ZIP: `C:/Users/blizz/RiderProjects/STUWard/Thunderstore/STUWard_v1.3.11.zip`
(`9cb677b563eb87873fd86ca4fb409b72804ca98e181681525efd89fedc61505e`)

Nexus ZIP: `C:/Users/blizz/RiderProjects/STUWard/Nexus/STUWard_v1.3.11.zip`
(`224d3866945deb914a8d415e4de6d7ff9800a77a08c779e8f8268b524f406a0f`)

Both ZIPs contain the same Release DLL. The staged Thunderstore manifest reports
version 1.3.11 and only `denikson-BepInExPack_Valheim-5.4.2350` as a dependency.

Client original: 1.0.7, Windows x64 / Steam b25185596.
Dedicated original: 1.0.7, Windows x64 / Steam b25185644.
The harness explicitly preloads and checks the selected game assembly path to
avoid accidentally testing copied client DLLs in the dedicated pass.

## Not performed / limits

The compatibility harness uses Unity Editor 6000.0.56f1's Mono with the unmodified
target game DLLs; it is **not a game launch or Unity engine simulation**. Windows
CLR cannot load the game's default interface methods, and .NET 9 cannot initialize
the installed Harmony version unchanged. Attempts on those runtimes were not
counted as passing checks. Full WardGameAccess static initialization reaches
Player/ZSyncAnimation native Animator calls, so it cannot be validated without
the Unity engine; no fake or publicized game assembly was substituted.

No new game, host or dedicated session was launched. Existing LogOutput.log was
still the pre-patch log, not evidence that this DLL had loaded. UI resource/display
readiness, fonts/materials, scale, controller navigation, actual Harmony installation
and patch combinations, prefab spawning/destruction, reloads, external group APIs,
Steam/PlayFab joins, authorization and item loss/duplication require in-game checks.
No frame/allocation profiler measurements were made. Resource scans occur on UI
readiness/explicit creation, not as a new per-frame polling loop.

Use build/CompatibilityCheck/README.md for reproduction and the focused runtime
acceptance scenarios. In particular: test with Jotunn absent and present, chunked
worlds with remote/unloaded wards, duplicate/delayed placements, one-time refunds,
scene transitions, UI close during pending requests, and failed optional providers.
