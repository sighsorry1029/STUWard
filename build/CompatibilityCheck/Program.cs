using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using System.Reflection.Emit;

// Managed compatibility checks against the final merged mod and unmodified game
// DLLs. This does not start Unity, instantiate prefabs, or prove multiplayer behavior.
if (args.Length != 3) throw new ArgumentException("Usage: mod.dll game-managed-directory BepInEx-core-directory");
var modPath = Path.GetFullPath(args[0]);
var managed = Path.GetFullPath(args[1]);
var core = Path.GetFullPath(args[2]);
AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
{
    var name = new AssemblyName(eventArgs.Name);
    foreach (var dir in new[] { managed, core, Path.GetDirectoryName(modPath)! })
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
