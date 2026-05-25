using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace STUWard;

internal static class WardPatchRegistry
{
    private static readonly HashSet<Type> RequiredPatchTypes = new()
    {
        typeof(PrivateAreaAwakePatch),
        typeof(PrivateAreaOnDestroyPatch),
        typeof(PrivateAreaUpdateStatusPatch),
        typeof(PrivateAreaSetupPatch),
        typeof(ZNetSceneCreateObjectManagedWardPatch),
        typeof(PrivateAreaRpcTogglePermittedManagedPatch),
        typeof(PrivateAreaRpcToggleEnabledAdminDebugPatch),
        typeof(DirectInteractionPatches),
        typeof(UseItemInteractionPatches),
        typeof(ItemDropPickupPatch),
        typeof(HumanoidPickupPatch),
        typeof(PlayerTryPlacePiecePatch),
        typeof(PlayerPlacePiecePatch),
        typeof(PlayerCheckCanRemovePiecePatch),
        typeof(PlayerRemovePiecePatch),
        typeof(ZNetSceneDestroyPatch),
        typeof(WearNTearDamagePatch),
        typeof(WearNTearRpcDamagePatch),
        typeof(WearNTearRemovePatch),
        typeof(WearNTearRpcRemovePatch),
        typeof(DestructibleDamagePatch),
        typeof(DestructibleRpcDamagePatch),
        typeof(TreeBaseDamagePatch),
        typeof(TreeBaseRpcDamagePatch)
    };

    internal static void ApplyAll(Harmony harmony)
    {
        var failedRequiredPatches = new List<string>();
        var patchTypes = GetHarmonyPatchTypes(Assembly.GetExecutingAssembly());
        for (var index = 0; index < patchTypes.Count; index++)
        {
            ApplyPatch(harmony, patchTypes[index], failedRequiredPatches);
        }

        PatchOptionalCompat("GuildsCompat", () => GuildsCompat.TryPatch(harmony));
        PatchOptionalCompat("TargetPortalCompat", () => TargetPortalCompat.TryPatch(harmony));

        if (failedRequiredPatches.Count == 0)
        {
            return;
        }

        var message = $"Failed to apply required patches: {string.Join(", ", failedRequiredPatches)}";
        Plugin.Log.LogError(message);
        throw new InvalidOperationException(message);
    }

    private static void ApplyPatch(Harmony harmony, Type patchType, ICollection<string> failedRequiredPatches)
    {
        var required = RequiredPatchTypes.Contains(patchType);
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
        catch (Exception exception)
        {
            if (required)
            {
                failedRequiredPatches.Add(patchType.Name);
                Plugin.Log.LogError($"Failed to patch required {patchType.Name}: {exception.GetType().Name}: {exception.Message}");
                return;
            }

            Plugin.Log.LogWarning($"Failed to patch optional {patchType.Name}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static IReadOnlyList<Type> GetHarmonyPatchTypes(Assembly assembly)
    {
        var patchTypes = new List<Type>();
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsClass && HasHarmonyPatchAttribute(type))
            {
                patchTypes.Add(type);
            }
        }

        patchTypes.Sort(static (left, right) => string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
        return patchTypes;
    }

    private static void PatchOptionalCompat(string name, Action patchAction)
    {
        try
        {
            patchAction();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to patch {name}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool HasHarmonyPatchAttribute(Type type)
    {
        return type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loadableTypes = new List<Type>();
            if (exception.Types == null)
            {
                return loadableTypes;
            }

            for (var index = 0; index < exception.Types.Length; index++)
            {
                var type = exception.Types[index];
                if (type != null)
                {
                    loadableTypes.Add(type);
                }
            }

            return loadableTypes;
        }
    }
}
