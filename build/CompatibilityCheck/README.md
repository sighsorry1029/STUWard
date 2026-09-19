# Compatibility verification

Build the mod in Debug first. This executable inspects the **final merged DLL**
and unmodified game references, then tests input-guard composition/transpilers
and required Harmony target resolution. It does not patch or copy game DLLs.

```powershell
dotnet build build/CompatibilityCheck/CompatibilityCheck.csproj -c Debug
& '<Unity Editor>/Editor/Data/MonoBleedingEdge/bin/mono.exe' `
  build/CompatibilityCheck/bin/Debug/net48/CompatibilityCheck.exe `
  bin/Debug/STUWard.dll '<Valheim>/valheim_Data/Managed' '<Valheim>/BepInEx/core'
```

The same check can use the dedicated server's original Managed directory.
Use the compatible Unity Mono runner (validated here with Unity 6000.0.56f1),
not Windows CLR: the 1.0 game assemblies contain default interface methods.
The installed Harmony 2.9.0 cannot run this check unchanged under .NET 9 either.
Neither limitation demonstrates a game-runtime failure of the mod.

Player's static initialization calls Unity's native Animator hash function.
Consequently full WardGameAccess initialization, rendering, prefab/scene lifetime,
controller navigation, live language switching, and multiplayer are **not**
executed by this managed harness. Do not substitute altered/publicized game
DLLs and report their success as a real game test.

In-game acceptance: run without Jotunn, then with Jotunn required by other mods;
check Hammer registration/search/recipe, place/reload ward, settings tabs/search,
Korean/English switching, UI scale/input/camera restoration, and session changes.
On host and dedicated+remote client verify initial config, admin denial, ward
counts in unloaded regions of chunked worlds, delayed/duplicate placement RPCs,
one-time refunds, ownership changes, and unchanged item totals. Guilds/Clan
missing, healthy, and failed startup are separate cases.

## Jotunn asset-path ordering regression

Pass a fourth argument containing an actual Jotunn DLL to run only the isolated
asset-path checks. Run the executable directly under Windows .NET Framework:

```powershell
& build/CompatibilityCheck/bin/Debug/net48/CompatibilityCheck.exe `
  bin/Debug/STUWard.dll '<original game Managed directory>' '<BepInEx>/core' `
  '<profile>/BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll'
```

This mode loads the final mod DLL and original game assemblies, then uses the
production STUWard patch class and the actual Jotunn transpiler. It does not
inject an ordering override: both registration orders must work using the
mod's own Harmony attributes. It checks STUWard alone, delayed Jotunn registration,
removal/re-registration of Jotunn, removal of STUWard, duplicate/null handling,
and restoration of the original method after cleanup. Patches exist only in the
test process; no assembly or installed mod is modified and the game target is
never invoked. This mode does not run the full compatibility checks above.

The regression is STUWard's AddPath replacement running before Jotunn's
AssetManager transpiler, leaving no Dictionary.Add for Jotunn to match.
Jotunn's plugin can load first while its AssetManager initializes later, for
example through ValheimRAFT's MapPinSync/MinimapManager. The production
HarmonyAfter constraint gives Jotunn priority in this specific transpiler chain
without adding a Jotunn dependency or forcing manager initialization.

The pre-fix 1.3.15 DLL fails this test with Jotunn 2.30.1 at AssetManager.cs:98.
The patch preserves null skipping and the first AssetID for a duplicate path;
it does not change asset availability, UI handle lifetimes, or ward permissions.
Other mods rewriting the same instruction remain outside this two-mod guarantee.

Use a full process restart for in-game acceptance: STUWard alone, then
STUWard + unmodified Jotunn + ValheimRAFT, including delayed AssetManager
initialization, ward UI reopening/world changes, and headless startup.
Verify RAFT map/prefab initialization as well as STUWard's UI. A prior failed
AssetManager static constructor is not repaired within the existing process.

Verification on 2026-09-20 (baseline `e3aac84`, STUWard 1.3.15): the regression
failed before the source change and passed afterward with the production patch
attributes. Each run passed 15 assertions using Jotunn 2.30.1 (SHA-256
`95E37F9E4FE4E5C34FD2C5F662E63E43B109885879BA40FD1DD70F58AE47FA7A`)
under Windows .NET Framework, against original 1.0.12 client and 1.0.15
client/dedicated-server DLLs. The full managed/IL mode separately passed under
Unity Editor Mono against 1.0.7 client/server, 1.0.12 client, and 1.0.15
client/server. These checks do not establish full game-version support.
An earlier prototype of the Harmony installation test reached its assertions
under Mono but exited with code 1; its exit cause remains unresolved, and it
is not counted as a successful runtime test. No actual game, RAFT UI, or
multiplayer session was executed for this patch.
