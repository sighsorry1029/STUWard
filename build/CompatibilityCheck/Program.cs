using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using System.Reflection.Emit;

// Managed compatibility checks against the final merged mod and unmodified game
// DLLs. This does not start Unity, instantiate prefabs, or prove multiplayer behavior.
if (args.Length is not (3 or 4 or 5)) throw new ArgumentException("Usage: mod.dll game-managed-directory BepInEx-core-directory [Jotunn.dll | --epicloot EpicLoot.dll]");
var modPath = Path.GetFullPath(args[0]);
var managed = Path.GetFullPath(args[1]);
var core = Path.GetFullPath(args[2]);
AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
{
    var name = new AssemblyName(eventArgs.Name);
    foreach (var dir in new[] { managed, core, Path.GetDirectoryName(modPath)!, args.Length >= 4 ? Path.GetDirectoryName(Path.GetFullPath(args[args.Length - 1]))! : core })
    {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    return null;
};
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
if (args.Length == 5)
{
    if (args[3] != "--epicloot") throw new ArgumentException("Unknown mode: " + args[3]);
    // Load the selected originals before LoadFrom can pick copied references
    // beside the mod (especially when checking a dedicated-server snapshot).
    foreach (var name in new[] { "UnityEngine.CoreModule.dll", "assembly_utils.dll", "assembly_guiutils.dll", "assembly_valheim.dll", "SoftReferenceableAssets.dll" })
    {
        var path = Path.Combine(managed, name);
        var selected = Assembly.LoadFrom(path);
        Check(string.Equals(selected.Location, path, StringComparison.OrdinalIgnoreCase), "Wrong original loaded: " + name);
    }
    var modAssembly = Assembly.LoadFrom(modPath);
    var epicAssembly = Assembly.LoadFrom(Path.GetFullPath(args[4]));
    Check(!modAssembly.GetReferencedAssemblies().Any(a => a.Name == "EpicLoot"), "EpicLoot must remain optional");
    var compat = modAssembly.GetType("STUWard.EpicLootCompat", true)!;
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var resolve = compat.GetMethod("ResolveHooks", flags)!;
    try
    {
        resolve.Invoke(null, new object[] { modAssembly });
        throw new InvalidOperationException("Accepted an incompatible optional assembly");
    }
    catch (TargetInvocationException) { checks++; }
    var hooks = (IEnumerable<(MethodInfo Target, MethodInfo Prefix)>)resolve.Invoke(null, new object[] { epicAssembly })!;
    Check(hooks.Count() == 9, "Expected Show, seven actions and the upgrade RPC");
    foreach (var hook in hooks)
    {
        Check(hook.Target.DeclaringType!.Assembly == epicAssembly, "Target is not from the supplied EpicLoot DLL");
        foreach (var parameter in hook.Prefix.GetParameters())
        {
            if (parameter.Name == "__runOriginal") Check(parameter.ParameterType == typeof(bool), "Wrong skip flag type");
            else if (parameter.Name == "__instance")
                Check(!hook.Target.IsStatic && parameter.ParameterType.IsAssignableFrom(hook.Target.DeclaringType), "Wrong instance binding");
            else
            {
                var index = int.Parse(parameter.Name!.Substring(2));
                Check(parameter.ParameterType.IsAssignableFrom(hook.Target.GetParameters()[index].ParameterType), "Wrong indexed argument binding");
            }
        }
    }
    Console.WriteLine($"PASS: {checks} EpicLoot target/prefix binding checks, using {epicAssembly.Location}. No detours, Unity, UI or RPC execution.");
    return;
}
if (args.Length == 4)
{
    // Separate mode: actual Harmony installation without the full game's type
    // initialization. Run in its own process; never invoke the game target.
    foreach (var name in new[] { "assembly_valheim.dll", "assembly_guiutils.dll", "SoftReferenceableAssets.dll" })
        Assembly.LoadFrom(Path.Combine(managed, name));
    var softAssembly = Assembly.LoadFrom(Path.Combine(managed, "SoftReferenceableAssets.dll"));
    Check(string.Equals(softAssembly.Location, Path.Combine(managed, "SoftReferenceableAssets.dll"), StringComparison.OrdinalIgnoreCase), "Wrong asset assembly loaded");
    var modAssembly = Assembly.LoadFrom(modPath);
    var jotunnAssembly = Assembly.LoadFrom(Path.GetFullPath(args[3]));
    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var patchType = modAssembly.GetType("STUWard.WardUiResources+AssetPathsPatch", true)!;
    var jotunnPatchType = jotunnAssembly.GetType("Jotunn.Managers.AssetManager+Patches", true)!;
    Check(jotunnPatchType.TypeInitializer == null, "Jotunn patch class now requires runtime initialization");
    var target = (MethodBase)patchType.GetMethod("TargetMethod", flags)!.Invoke(null, null)!;
    var stuAdd = patchType.GetMethod("AddPath", flags)!;
    var jotunnAdd = jotunnPatchType.GetMethod("AddSafe", flags)!;
    var jotunnTranspiler = jotunnPatchType.GetMethod("AssetBundleLoader_GetAllAssetPathsMappedToAssetID", flags)!;
    var assetId = softAssembly.GetType("SoftReferenceableAssets.AssetID", true)!;
    var dictionary = typeof(Dictionary<,>).MakeGenericType(typeof(string), assetId);
    var nativeAdd = dictionary.GetMethod("Add")!;
    Check(PatchProcessor.GetOriginalInstructions(target).Count(i => i.Calls(nativeAdd)) == 1, "Unexpected original asset lookup body");
    void CheckGuard(MethodInfo expected)
    {
        var code = PatchProcessor.GetCurrentInstructions(target);
        var other = expected == stuAdd ? jotunnAdd : stuAdd;
        Check(code.Count(i => i.Calls(expected)) == 1 && !code.Any(i => i.Calls(other) || i.Calls(nativeAdd)), "Wrong asset guard after Harmony reconstruction: " + expected.Name);
    }
    foreach (var stuFirst in new[] { true, false })
    {
        var stuHarmony = new Harmony("sighsorry.STUWard");
        var jotunnHarmony = new Harmony("com.jotunn.jotunn");
        try
        {
            if (stuFirst)
            {
                // Read production attributes through the same class processor
                // as WardPatchRegistry; do not supply test-only ordering.
                stuHarmony.CreateClassProcessor(patchType).Patch();
                CheckGuard(stuAdd);
                jotunnHarmony.Patch(target, transpiler: new HarmonyMethod(jotunnTranspiler));
            }
            else
            {
                jotunnHarmony.Patch(target, transpiler: new HarmonyMethod(jotunnTranspiler));
                stuHarmony.CreateClassProcessor(patchType).Patch();
            }
            CheckGuard(jotunnAdd);
            jotunnHarmony.Unpatch(target, jotunnTranspiler);
            CheckGuard(stuAdd);
            jotunnHarmony.Patch(target, transpiler: new HarmonyMethod(jotunnTranspiler));
            CheckGuard(jotunnAdd);
            stuHarmony.UnpatchSelf();
            CheckGuard(jotunnAdd);
            System.Console.WriteLine($"PASS: {(stuFirst ? "STUWard first" : "Jotunn first")}; late registration, removal and re-registration.");
        }
        finally
        {
            stuHarmony.UnpatchSelf();
            jotunnHarmony.Unpatch(target, jotunnTranspiler);
        }
    }
    var firstId = Activator.CreateInstance(assetId, new object[] { 1u, 2u, 3u, 4u });
    var secondId = Activator.CreateInstance(assetId, new object[] { 5u, 6u, 7u, 8u });
    foreach (var helper in new[] { stuAdd, jotunnAdd })
    {
        var entries = (System.Collections.IDictionary)Activator.CreateInstance(dictionary)!;
        helper.Invoke(null, new[] { entries, "same/path", firstId });
        helper.Invoke(null, new[] { entries, "same/path", secondId });
        helper.Invoke(null, new object?[] { entries, null, secondId });
        Check(entries.Count == 1 && entries["same/path"]!.Equals(firstId), helper.Name + " changed duplicate/null handling");
    }
    Check(PatchProcessor.GetCurrentInstructions(target).Count(i => i.Calls(nativeAdd)) == 1, "Asset target not restored after cleanup");
    System.Console.WriteLine($"PASS: {checks} asset-path checks against {jotunnAssembly.GetName()}. Unity initialization and the game target were NOT executed.");
    return;
}
using (var module = ModuleDefinition.ReadModule(modPath))
{
    Check(!module.AssemblyReferences.Any(r => r.Name == "Jotunn"), "Jotunn assembly reference remains");
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(managed);
    resolver.AddSearchDirectory(core);
    using var inspected = ModuleDefinition.ReadModule(modPath, new ReaderParameters { AssemblyResolver = resolver });
    foreach (var type in Flatten(inspected.Types))
    {
        foreach (var attr in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
            Check(!attr.ConstructorArguments.Any(a => a.Value?.ToString() == "com.jotunn.jotunn"), "Jotunn dependency remains");
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is FieldReference field && field.DeclaringType.FullName == "ZRoutedRpc" && field.Name == "Everybody")
                throw new InvalidOperationException("Old Everybody field access in " + method.FullName);
            if (instruction.Operand is not MemberReference member || member.DeclaringType?.Scope.Name is not ("assembly_valheim" or "assembly_guiutils" or "SoftReferenceableAssets")) continue;
            if (member is MethodReference call)
            {
                var resolved = call.Resolve();
                Check(resolved != null, "Unresolved game call " + call.FullName);
                Check(resolved!.IsPublic, "Direct non-public game call " + call.FullName);
            }
            if (member is FieldReference access)
            {
                var resolved = access.Resolve();
                Check(resolved != null, "Unresolved game field " + access.FullName);
                Check(resolved!.IsPublic, "Direct non-public game field " + access.FullName);
            }
        }
    }
}
// Preload the selected target before the mod: LoadFrom would otherwise probe
// the mod's output directory and could silently reuse client reference copies
// during a purported dedicated-server check.
var game = Assembly.LoadFrom(Path.Combine(managed, "assembly_valheim.dll"));
foreach (var assemblyName in new[] { "assembly_guiutils.dll", "SoftReferenceableAssets.dll" })
    Assembly.LoadFrom(Path.Combine(managed, assemblyName));
Check(string.Equals(Path.GetFullPath(game.Location), Path.Combine(managed, "assembly_valheim.dll"), StringComparison.OrdinalIgnoreCase), "Wrong game assembly was loaded");
var mod = Assembly.LoadFrom(modPath);
const BindingFlags hiddenStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
const BindingFlags hiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
// Exercise the actual merged YAML parser without initializing Unity/ServerSync.
// A failed parse returns diagnostics; only the file-reader logs and commits state.
var configService = mod.GetType("STUWard.ManagedWardConfigFileService", true)!;
var parseConfig = configService.GetMethod("TryParseYaml", hiddenStatic)!;
object ParseConfig(string yaml, bool expected = true)
{
    var arguments = new object?[] { yaml, null, null };
    var accepted = (bool)parseConfig.Invoke(null, arguments)!;
    Check(accepted == expected, "Unexpected YAML result: " + yaml + " / " + arguments[2]);
    Check(accepted == string.IsNullOrEmpty((string)arguments[2]!), "Missing YAML parse diagnostic");
    return arguments[1]!;
}
T SnapshotValue<T>(object snapshot, string name) =>
    (T)snapshot.GetType().GetProperty(name, hiddenInstance)!.GetValue(snapshot)!;
int SnapshotLimit(object snapshot, string accountId, bool outpost, int regularDefault = 5, int outpostDefault = 3) =>
    (int)snapshot.GetType().GetMethod("GetWardLimit", hiddenInstance)!
        .Invoke(snapshot, new object[] { accountId, outpost, regularDefault, outpostDefault })!;
const string account = "76561198000000000";
foreach (var yaml in new[] { "", "ward_limit_overrides: {}\noutpost_ward_limit_overrides: {}" })
{
    var defaults = ParseConfig(yaml);
    Check(SnapshotLimit(defaults, account, false) == 5, "Regular CFG fallback changed");
    Check(SnapshotLimit(defaults, account, true) == 3, "Outpost CFG fallback changed");
    Check(SnapshotLimit(defaults, account, true, outpostDefault: 7) == 7, "Outpost CFG fallback is hard-coded");
}
var configured = ParseConfig("ward_limit_overrides:\n  '76561198000000000': 9\noutpost_ward_limit_overrides:\n  '76561198000000000': 2\nitem_prefab_policy:\n  blocked_item_prefabs: [FLG_TamingOrb]\n  pickup_whitelist: [Wood]\n  pickup_blacklist: []\n");
Check(SnapshotLimit(configured, account, false) == 9, "Regular account override changed");
Check(SnapshotLimit(configured, account, true) == 2, "Outpost account override changed");
Check(SnapshotValue<string>(configured, "ItemPrefabPolicyYaml").Contains("FLG_TamingOrb"), "Shared item policy was lost");
foreach (var outpost in new[] { false, true })
{
    var section = outpost ? "outpost_ward_limit_overrides" : "ward_limit_overrides";
    foreach (var limit in new[] { -1, 0, 9 })
    {
        var snapshot = ParseConfig(section + ": {'Steam_" + account + "': " + limit + "}");
        Check(SnapshotLimit(snapshot, account, outpost) == limit, "Normalized account override was lost: " + section);
        Check(SnapshotLimit(snapshot, account, !outpost) == (outpost ? 5 : 3), "Quota leaked between ward types: " + section);
        Check(SnapshotLimit(snapshot, "76561198000000001", outpost) == (outpost ? 3 : 5), "Quota leaked between accounts: " + section);
    }
    foreach (var invalid in new[]
    {
        "{'invalid-id': 3}", "{'" + account + "': -2}",
        "{'" + account + "': 3, '" + account + "': 4}",
        "{'" + account + "': 3, 'Steam_" + account + "': 4}", "[]"
    }) ParseConfig(section + ": " + invalid, expected: false);
    ParseConfig(section + ":\n" + string.Join("\n", Enumerable.Range(0, 4097)
        .Select(i => "  '" + (76561198000000000L + i) + "': 3")), expected: false);
}
ParseConfig("outpost_ward: {max_radius: 16, max_per_steam_id: 3}", expected: false);
ParseConfig("unknown_section: {}", expected: false);
var generated = ParseConfig((string)configService.GetMethod("GetDefaultConfigFileContents", hiddenStatic)!.Invoke(null, null)!);
Check(SnapshotValue<IReadOnlyDictionary<string, int>>(generated, "WardLimitOverrides").Count == 0, "Generated regular overrides are not empty");
Check(SnapshotValue<IReadOnlyDictionary<string, int>>(generated, "OutpostWardLimitOverrides").Count == 0, "Generated Outpost overrides are not empty");
Check(SnapshotValue<string>(generated, "ItemPrefabPolicyYaml").Contains("kg_TameableCollector"), "Generated item policy was lost");
Check(SnapshotValue<string>(generated, "ItemPrefabPolicyYaml").Contains("FLG_TamingOrb"), "Generated item policy does not block the Taming Orb");
var explicitPolicy = ParseConfig("item_prefab_policy: {blocked_item_prefabs: [PalStone]}");
Check(!SnapshotValue<string>(explicitPolicy, "ItemPrefabPolicyYaml").Contains("FLG_TamingOrb"), "Explicit user policy was overwritten with new defaults");
var persistedSnapshot = configService.GetField("_currentSnapshot", hiddenStatic)!;
var previousSnapshot = persistedSnapshot.GetValue(null)!;
try
{
    persistedSnapshot.SetValue(null, configured);
    ParseConfig("outpost_ward_limit_overrides: {'" + account + "': -3}", expected: false);
    Check(persistedSnapshot.GetValue(null)!.Equals(configured), "Failed parse modified the active snapshot");
}
finally { persistedSnapshot.SetValue(null, previousSnapshot); }
var wardKind = mod.GetType("STUWard.StuWardArea", true)!;
var prefabHash = Assembly.LoadFrom(Path.Combine(managed, "assembly_utils.dll"))
    .GetType("StringExtensionMethods", true)!.GetMethod("GetStableHashCode", hiddenStatic, null, new[] { typeof(string) }, null)!;
var isManagedPrefab = wardKind.GetMethod("IsManagedPrefab", hiddenStatic)!;
foreach (var name in new[] { "piece_stuward", "piece_stuward_outpost", "guard_stone", "piece_chest_wood" })
    Check((bool)isManagedPrefab.Invoke(null, new[] { prefabHash.Invoke(null, new object[] { name }) })! == name.StartsWith("piece_stuward"),
        "Wrong managed prefab classification: " + name);
Check(wardKind.GetField("IsOutpostWard", hiddenInstance)!.GetCustomAttributes(false)
    .Any(a => a.GetType().FullName == "UnityEngine.SerializeField"), "Ghost kind is not Unity-serialized");
foreach (var language in new[] { "English", "Korean" })
{
    var resource = mod.GetManifestResourceNames().Single(n => n.EndsWith("." + language + ".yml"));
    using var reader = new StreamReader(mod.GetManifestResourceStream(resource)!);
    var text = reader.ReadToEnd();
    Check(text.Contains("stuw_outpost_name") && text.Contains("stuw_outpost_desc"), "Outpost localization missing: " + language);
}
var localizer = mod.GetType("LocalizationManager.Localizer", true)!;
var normalizeTokenKey = localizer.GetMethod("NormalizeTokenKey", hiddenStatic)!;
Check((string)normalizeTokenKey.Invoke(null, new object[] { "$stuw_piece_name" })! == "stuw_piece_name", "Localization token prefix was not removed");
Check((string)normalizeTokenKey.Invoke(null, new object[] { "stuw_piece_name" })! == "stuw_piece_name", "Normalized localization token changed");
var deserializeTranslations = localizer.GetMethod("DeserializeTranslations", hiddenStatic)!;
var parsedTranslations = (System.Collections.IDictionary)deserializeTranslations.Invoke(null, new object[] { "\"$stuw_piece_name\": \"Ward\"" })!;
Check(parsedTranslations.Contains("stuw_piece_name"), "YAML localization key was not normalized");
Check(!parsedTranslations.Contains("$stuw_piece_name"), "YAML localization retained the token prefix");
// Creating the static Player accessor triggers ZSyncAnimation's native Animator
// hash initialization. Do not substitute fake Unity calls to make this test pass.
// Accessor construction as a whole therefore remains an in-game check.
var resources = mod.GetType("STUWard.WardUiResources", true)!;
var guard = mod.GetType("STUWard.WardUiResources+MenuInputPatch", true)!;
var block = resources.GetMethod("BlockInput", hiddenStatic)!;
var include = guard.GetMethod("IncludeWard", hiddenStatic)!;
foreach (var visible in new[] { false, true })
{
    block.Invoke(null, new object[] { visible });
    foreach (var other in new[] { false, true })
        Check((bool)include.Invoke(null, new object[] { other })! == (visible || other), "Input guard released another UI's input block");
}
block.Invoke(null, new object[] { false });
var hoverService = mod.GetType("STUWard.ManagedWardHoverTextService", true)!;
var hoverLineType = mod.GetType("STUWard.ManagedWardHoverTextService+HoverTextLine", true)!;
var hoverLine = Activator.CreateInstance(hoverLineType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { 6, 14 }, null)!;
var matchesHoverLine = hoverService.GetMethod("IsAnyExactLine", hiddenStatic)!;
var hoverSource = "Owner\n[E] Deactivate\nPermitted:";
Check((bool)matchesHoverLine.Invoke(null, new object[] { hoverSource, hoverLine, new[] { "[E] Deactivate" } })!, "Exact vanilla ward action was not recognized");
Check(!(bool)matchesHoverLine.Invoke(null, new object[] { hoverSource, hoverLine, new[] { "[E] Ward settings" } })!, "Unrelated hover action was removed");
var transpiler = guard.GetMethod("Transpiler", hiddenStatic)!;
foreach (var (typeName, methodName) in new[] { ("InventoryGui", "Update"), ("GameCamera", "UpdateCamera") })
{
    var target = AccessTools.DeclaredMethod(game.GetType(typeName), methodName);
    var original = PatchProcessor.GetOriginalInstructions(target);
    var transformed = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { original })!).ToList();
    Check(transformed.Count > original.Count, "Input guard was not inserted into " + typeName);
    Check(transformed.Count(i => i.Calls(include)) == original.Count(i => i.Calls(AccessTools.DeclaredMethod(game.GetType("Menu"), "IsVisible"))), "Input guard coverage differs");
}
try
{
    _ = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Ret) } })!).ToList();
    throw new InvalidOperationException("Input transpiler accepted an unknown body");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("expected Menu.IsVisible")) { checks++; }
// Resolve all required mod patch targets, including private members and overloads.
var targetCount = 0;
foreach (var type in mod.GetTypes().Where(t => t.GetCustomAttributes<HarmonyPatch>().Any()))
{
    if (type.Namespace?.StartsWith("ServerSync") == true) continue;
    if (type.Name is "TameableCollectorCollectorItemPatch" or "AzuCraftyBoxesNearbyContainersPatch") continue;
    var targetsMethod = type.GetMethod("TargetMethods", hiddenStatic);
    if (targetsMethod != null)
    {
        foreach (var method in (IEnumerable<MethodBase>)targetsMethod.Invoke(null, null)!)
        {
            Check(method != null, "Missing dynamic target for " + type.FullName);
            targetCount++;
        }
        continue;
    }
    var singleTarget = type.GetMethod("TargetMethod", hiddenStatic);
    if (singleTarget != null)
    {
        Check(singleTarget.Invoke(null, null) is MethodBase, "Missing dynamic target for " + type.FullName);
        targetCount++;
        continue;
    }
    foreach (var patch in type.GetCustomAttributes<HarmonyPatch>())
    {
        if (patch.info.declaringType == null || patch.info.methodName == null) continue;
        var method = AccessTools.DeclaredMethod(patch.info.declaringType, patch.info.methodName, patch.info.argumentTypes);
        Check(method != null, "Missing target for " + type.FullName);
        targetCount++;
    }
}
var pathsPatch = mod.GetType("STUWard.WardUiResources+AssetPathsPatch", true)!;
var pathsTarget = (MethodBase)pathsPatch.GetMethod("TargetMethod", hiddenStatic)!.Invoke(null, null)!;
var originalPaths = PatchProcessor.GetOriginalInstructions(pathsTarget);
var transformedPaths = ((IEnumerable<CodeInstruction>)pathsPatch.GetMethod("Transpiler", hiddenStatic)!.Invoke(null, new object[] { originalPaths })!).ToList();
Check(transformedPaths.Any(i => i.Calls(pathsPatch.GetMethod("AddPath", hiddenStatic))), "Asset path guard was not inserted");
// Repeated paths retain the first identity; no global name overwrite policy.
var soft = Assembly.LoadFrom(Path.Combine(managed, "SoftReferenceableAssets.dll"));
var idType = soft.GetType("SoftReferenceableAssets.AssetID", true)!;
var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), idType);
var paths = (System.Collections.IDictionary)Activator.CreateInstance(dictionaryType)!;
var first = Activator.CreateInstance(idType, new object[] { 1u, 2u, 3u, 4u });
var second = Activator.CreateInstance(idType, new object[] { 5u, 6u, 7u, 8u });
var addPath = pathsPatch.GetMethod("AddPath", hiddenStatic)!;
addPath.Invoke(null, new[] { paths, "same/path", first });
addPath.Invoke(null, new[] { paths, "same/path", second });
addPath.Invoke(null, new object?[] { paths, null, second });
Check(paths.Count == 1 && paths["same/path"]!.Equals(first), "Asset lookup overwrote an earlier path");
// Execute the production packet parser using the original game's ZPackage.
// This exercises malformed network input without creating a Unity object.
var readGroups = mod.GetType("STUWard.WardRemoteGroupAccess", true)!.GetMethod("TryReadSnapshot", hiddenStatic)!;
var packageType = readGroups.GetParameters()[0].ParameterType;
// Both ward types may have the same configured radius. Their icon identity must
// survive the real ZPackage codec, including entries for distant/unloaded wards.
var mapManager = mod.GetType("STUWard.WardMinimapPinsManager", true)!;
var readMapEntries = mapManager.GetMethod("TryReadSnapshotEntries", hiddenStatic)!;
var writeMapEntries = mapManager.GetMethod("WriteSnapshotEntries", hiddenStatic)!;
byte[] MapEntriesPacket(float radius = 16f)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    foreach (var outpost in new[] { false, true })
    {
        writer.Write(42L); writer.Write(outpost ? 2u : 1u);
        writer.Write(2000f); writer.Write(50f); writer.Write(-3000f);
        writer.Write(radius); writer.Write(!outpost); writer.Write(outpost);
    }
    writer.Flush();
    return stream.ToArray();
}
bool ReadMapEntries(byte[] bytes, int count, out Array entries)
{
    var arguments = new object?[] { Activator.CreateInstance(packageType, new object[] { bytes }), count, null };
    var accepted = (bool)readMapEntries.Invoke(null, arguments)!;
    entries = (Array)arguments[2]!;
    return accepted;
}
var mapBytes = MapEntriesPacket();
Check(ReadMapEntries(mapBytes, 2, out var mapEntries) && mapEntries.Length == 2, "Mixed ward map payload rejected");
for (var index = 0; index < mapEntries.Length; index++)
{
    var entry = mapEntries.GetValue(index)!;
    Check(SnapshotValue<bool>(entry, "IsOutpost") == (index == 1), "Map ward identity lost");
    Check(SnapshotValue<bool>(entry, "IsEnabled") == (index == 0), "Map enabled state corrupted");
    Check(SnapshotValue<float>(entry, "Radius") == 16f, "Map radius corrupted");
}
var writtenMap = Activator.CreateInstance(packageType)!;
writeMapEntries.Invoke(null, new object[] { writtenMap, mapEntries });
Check(((byte[])packageType.GetMethod("GetArray", Type.EmptyTypes)!.Invoke(writtenMap, null)!).SequenceEqual(mapBytes),
    "Map snapshot codec did not round-trip both ward types");
Check(!ReadMapEntries(mapBytes.Take(mapBytes.Length - 1).ToArray(), 2, out _), "Truncated map kind accepted");
Check(!ReadMapEntries(MapEntriesPacket(float.NaN), 2, out _), "Non-finite map radius accepted");
Check(!ReadMapEntries(mapBytes, -1, out _), "Negative map count accepted");
Check(!ReadMapEntries(mapBytes, 16385, out _), "Oversized map count accepted");
var tintOutpost = mod.GetType("STUWard.StuWardPrefab", true)!.GetMethod("TintOutpostColor", hiddenStatic)!;
var colorType = tintOutpost.ReturnType;
foreach (var intensity in new[] { 0f, 0.5f, 1f })
{
    var originalColor = Activator.CreateInstance(colorType, new object[] { intensity, intensity, intensity, 0.4f })!;
    var tinted = tintOutpost.Invoke(null, new[] { originalColor })!;
    Check(Math.Abs((float)colorType.GetField("a")!.GetValue(tinted)! - 0.4f) < 0.0001f, "Icon tint changed alpha");
    Check(Math.Abs((float)colorType.GetField("r")!.GetValue(tinted)! - intensity * 85f / 255f) < 0.0001f &&
          Math.Abs((float)colorType.GetField("g")!.GetValue(tinted)! - intensity * 206f / 255f) < 0.0001f &&
          Math.Abs((float)colorType.GetField("b")!.GetValue(tinted)! - intensity * 214f / 255f) < 0.0001f,
        "Icon tint changed the teal ramp");
}
byte[] GroupPacket(int count, bool row = false, bool trailing = false)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(7L);
    writer.Write(count);
    if (row)
    {
        writer.Write(20L); writer.Write(30L); writer.Write(40L); writer.Write(50u);
        writer.Write("guilds"); writer.Write("123"); writer.Write("Guild");
    }
    if (trailing) writer.Write((byte)1);
    writer.Flush();
    return stream.ToArray();
}
bool ReadGroupPacket(byte[] bytes, out int count)
{
    var arguments = new object?[] { Activator.CreateInstance(packageType, new object[] { bytes }), 0L, null };
    var success = (bool)readGroups.Invoke(null, arguments)!;
    count = ((System.Collections.ICollection)arguments[2]!).Count;
    return success;
}
Check(ReadGroupPacket(GroupPacket(0), out var emptyCount) && emptyCount == 0, "No-group replacement packet rejected");
Check(ReadGroupPacket(GroupPacket(1, row: true), out var groupCount) && groupCount == 1, "Group packet rejected by original ZPackage");
Check(!ReadGroupPacket(GroupPacket(1), out _), "Truncated group packet accepted");
Check(!ReadGroupPacket(GroupPacket(-1), out _), "Negative group count accepted");
Check(!ReadGroupPacket(GroupPacket(1025), out _), "Oversized group count accepted");
Check(!ReadGroupPacket(GroupPacket(0, trailing: true), out _), "Trailing group payload accepted");
Check(!ReadGroupPacket(new byte[512 * 1024 + 1], out _), "Oversized group payload accepted");
var requesterGuard = mod.GetType("STUWard.ContainerManagedRequesterIdentityPatch", true)!.GetMethod("Prefix", hiddenStatic)!;
Check(requesterGuard.GetCustomAttribute<HarmonyPriority>()!.info.priority > Priority.First,
    "Requester identity must be checked before shared-container state-changing prefixes");
// Exercise the actual compiled skip/finalizer paths without creating Unity objects.
// Both grave entry points must preserve prior patch decisions and release only
// their own temporary permission scope, even on exceptions or repeated cleanup.
var accessType = mod.GetType("STUWard.WardAccess", true)!;
var scopeType = mod.GetType("STUWard.WardCheckScopeState", true)!;
var enterAllow = scopeType.GetMethod("EnterManagedWardAllow", BindingFlags.Instance | BindingFlags.NonPublic)!;
var isAllowActive = accessType.GetProperty("IsManagedWardAllowScopeActive", hiddenStatic)!;
bool AllowActive() => (bool)isAllowActive.GetValue(null)!;
foreach (var patchName in new[] { "DirectInteractionPatches", "ContainerTakeAllOwnTombstonePatch" })
{
    var patchType = mod.GetType("STUWard." + patchName, true)!;
    var prefix = patchType.GetMethod("Prefix", hiddenStatic)!;
    var finalizer = patchType.GetMethod("Finalizer", hiddenStatic)!;
    object?[] skippedArgs = patchName == "DirectInteractionPatches"
        ? new object?[] { null, null, false, null, false }
        : new object?[] { null, null, null, false };
    prefix.Invoke(null, skippedArgs);
    Check(!AllowActive(), patchName + " entered a scope after another patch denied the call");
    if (patchName == "DirectInteractionPatches")
        Check(!(bool)skippedArgs[2]!, "Skipped direct interaction replaced the previous result");
    var skippedState = skippedArgs[patchName == "DirectInteractionPatches" ? 3 : 2];
    var failure = new InvalidOperationException("grave scope cleanup fixture");
    Check(ReferenceEquals(finalizer.Invoke(null, new[] { skippedState, failure }), failure), "Skipped call swallowed an exception");
    Check(!AllowActive(), "Skipped call cleanup leaked permission");

    var outer = Activator.CreateInstance(scopeType)!;
    var inner = Activator.CreateInstance(scopeType)!;
    enterAllow.Invoke(outer, null);
    enterAllow.Invoke(inner, null);
    Check(AllowActive(), "Nested fixture did not enter permission scopes");
    object?[] cleanupArgs = { inner, failure };
    Check(ReferenceEquals(finalizer.Invoke(null, cleanupArgs), failure), patchName + " swallowed an exception");
    Check(AllowActive(), patchName + " cleared the enclosing permission scope");
    finalizer.Invoke(null, cleanupArgs);
    Check(AllowActive(), patchName + " double cleanup cleared the enclosing scope");
    Check(finalizer.Invoke(null, new object?[] { outer, null }) == null, "Successful cleanup produced an exception");
    Check(!AllowActive(), patchName + " leaked permission beyond the container call");
}
Console.WriteLine($"PASS: {checks} managed/IL checks; {targetCount} required target lookups. Unity native accessor initialization, rendering, prefab lifetime and networking were NOT executed.");
// Unity's managed assemblies can enter unavailable native teardown paths when
// this standalone verifier unloads under the Editor Mono runtime. All checks
// have completed, so bypass managed teardown before it obscures the result.
NativeProcess.ExitProcess(0);

internal static class NativeProcess
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern void ExitProcess(uint exitCode);
}
