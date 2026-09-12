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
