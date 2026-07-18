using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace STUWard;

internal static class WardInteractionPatchTargets
{
    private static readonly Type[] CommonTargetTypes =
    {
        typeof(Container),
        typeof(Door),
        typeof(ShipControlls),
        typeof(Vagon),
        typeof(Sign),
        typeof(ItemStand),
        typeof(Beehive),
        typeof(CraftingStation),
        typeof(Fermenter),
        typeof(SapCollector),
        typeof(Trap),
        typeof(Tameable),
        typeof(Sadle),
        typeof(TeleportWorld),
        typeof(Feast),
        typeof(Pickable)
    };

    internal static IEnumerable<MethodBase> GetCommonTargets(string methodName)
    {
        for (var index = 0; index < CommonTargetTypes.Length; index++)
        {
            yield return RequireDeclaredMethod(CommonTargetTypes[index], methodName);
        }
    }

    internal static IEnumerable<MethodBase> GetDirectInteractTargets()
    {
        foreach (var method in GetCommonTargets(nameof(Container.Interact)))
        {
            yield return method;
        }

        yield return RequireDeclaredMethod(typeof(ItemDrop), nameof(ItemDrop.Interact));
    }

    internal static bool TryGetRestriction(Component? target, out WardRestrictionOptions restriction)
    {
        restriction = target switch
        {
            Door => WardRestrictionOptions.Doors,
            TeleportWorld => WardRestrictionOptions.Portals,
            Feast => WardRestrictionOptions.PlacedConsumables,
            ItemStand => WardRestrictionOptions.ItemStands,
            ArmorStand => WardRestrictionOptions.ArmorStands,
            Container => WardRestrictionOptions.Containers,
            CraftingStation => WardRestrictionOptions.CraftingStations,
            Tameable => WardRestrictionOptions.TameablesAndSaddles,
            Sadle => WardRestrictionOptions.TameablesAndSaddles,
            _ => WardRestrictionOptions.None
        };
        return restriction != WardRestrictionOptions.None;
    }

    private static MethodBase RequireDeclaredMethod(Type type, string methodName)
    {
        var method = AccessTools.DeclaredMethod(type, methodName);
        if (method == null)
        {
            throw new MissingMethodException(type.FullName, methodName);
        }

        return method;
    }
}

internal struct WardCheckScopeState
{
    private WardAccess.RestrictionScope _restrictionScope;
    private WardAccess.ManagedWardAllowScope _allowScope;

    internal void EnterRestriction(WardRestrictionOptions restriction)
    {
        _restrictionScope = WardAccess.EnterRestrictionScope(restriction);
    }

    internal void EnterManagedWardAllow()
    {
        _allowScope = WardAccess.EnterManagedWardAllowScope();
    }

    internal void Dispose()
    {
        _restrictionScope.Dispose();
        _allowScope.Dispose();
    }
}

[HarmonyPatch]
internal static class DirectInteractionPatches
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return WardInteractionPatchTargets.GetDirectInteractTargets();
    }

    private static bool Prefix(Component __instance, Humanoid __0, ref bool __result, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(__0);
        return TryHandleDirectInteraction(__instance, player, ref __result, ref __state);
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }

    private static bool TryHandleDirectInteraction(Component target, Player? player, ref bool result, ref WardCheckScopeState scopeState)
    {
        if (target is ItemDrop itemDrop)
        {
            if (WardAccess.IsPlacedConsumable(itemDrop))
            {
                return TryBlockWithRestriction(WardRestrictionOptions.PlacedConsumables, itemDrop, player, ref result, ref scopeState);
            }

            if (!WardItemPrefabPolicy.CanAnyPickupBeBlocked() || !WardItemPrefabPolicy.ShouldBlockPickup(itemDrop))
            {
                scopeState.EnterManagedWardAllow();
                return true;
            }

            if (!WardAccess.ShouldBlockPickup(itemDrop, player))
            {
                scopeState.EnterRestriction(WardRestrictionOptions.Pickup);
                return true;
            }

            WardAccess.ShowNoAccessMessage(player);
            result = true;
            return false;
        }

        if (WardInteractionPatchTargets.TryGetRestriction(target, out var restriction))
        {
            return TryBlockWithRestriction(restriction, target, player, ref result, ref scopeState);
        }

        return WardAccess.TryBlockInteraction(target, player, ref result);
    }

    private static bool TryBlockWithRestriction(WardRestrictionOptions restriction, Component target, Player? player, ref bool result, ref WardCheckScopeState scopeState)
    {
        var continueOriginal = WardAccess.TryBlockInteraction(restriction, target, player, ref result);
        if (continueOriginal)
        {
            scopeState.EnterRestriction(restriction);
        }

        return continueOriginal;
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.HaveLocalAccess))]
internal static class PrivateAreaHaveLocalAccessManagedPatch
{
    private static void Postfix(PrivateArea __instance, ref bool __result)
    {
        if (__result || !WardAccess.IsManagedWard(__instance, false))
        {
            return;
        }

        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }

        if (WardAdminDebugAccess.CanLocallyControlAnyWard(__instance, player))
        {
            __result = true;
            return;
        }

        if (WardAccess.IsPlayerInWardGuild(player, __instance))
        {
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.CheckAccess))]
internal static class PrivateAreaCheckAccessManagedPatch
{
    private static bool Prefix(Vector3 point, float radius, bool flash, bool wardCheck, ref bool __result)
    {
        var player = Player.m_localPlayer;
        if (player == null || !WardAccess.HasEnabledManagedWards())
        {
            return true;
        }

        var effectiveRadius = radius;
        var placementGhost = player.m_placementGhost;
        var placementGhostArea = placementGhost != null ? placementGhost.GetComponent<PrivateArea>() : null;
        if (placementGhost != null &&
            StuWardArea.IsManaged(placementGhostArea) &&
            Vector3.Distance(placementGhost.transform.position, point) <= 0.1f)
        {
            // Managed ward placement should use the minimum legal radius for foreign ward access checks.
            // The actual configured radius is still constrained separately by overlap logic and settings clamp.
            effectiveRadius = WardSettings.MinRadius;
        }

        var candidates = WardAccess.GetCandidateManagedWards(point, effectiveRadius, requireEnabled: true);
        if (WardAccess.IsManagedWardAllowScopeActive && WardAccess.IsInsideAnyManagedWard(point, effectiveRadius, candidates))
        {
            __result = true;
            return false;
        }

        var hasRestrictionScope = WardAccess.TryGetRestrictionScope(out var scopedRestriction);
        var access = hasRestrictionScope
            ? WardAccess.EvaluateRestrictionAccessAgainstCandidates(
                scopedRestriction,
                point,
                effectiveRadius,
                player.GetPlayerID(),
                candidates,
                flash,
                wardCheck)
            : WardAccess.EvaluateAccessAgainstCandidates(
                point,
                effectiveRadius,
                player.GetPlayerID(),
                candidates,
                flash,
                wardCheck);
        if (access.Decision == WardAccess.AccessDecision.NoWard)
        {
            if (hasRestrictionScope && WardAccess.IsInsideAnyManagedWard(point, effectiveRadius, candidates))
            {
                __result = true;
                return false;
            }

            return true;
        }

        __result = !access.IsDenied;
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.CheckAccess))]
internal static class ContainerCheckAccessManagedPatch
{
    private static bool Prefix(Container __instance, long playerID, ref bool __result)
    {
        if (__instance == null || playerID == 0L || !WardAccess.HasEnabledManagedWards())
        {
            return true;
        }

        var candidates = WardAccess.GetCandidateManagedWards(__instance.transform.position, 0f, requireEnabled: true);
        var access = WardAccess.EvaluateRestrictionAccessAgainstCandidates(
            WardRestrictionOptions.Containers,
            __instance.transform.position,
            0f,
            playerID,
            candidates,
            flash: false);
        if (access.Decision == WardAccess.AccessDecision.NoWard)
        {
            if (WardAccess.IsInsideAnyManagedWard(__instance.transform.position, 0f, candidates))
            {
                __result = true;
                return false;
            }

            return true;
        }

        __result = !access.IsDenied;
        return false;
    }
}

[HarmonyPatch]
internal static class UseItemInteractionPatches
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return WardInteractionPatchTargets.GetCommonTargets(nameof(Container.UseItem));
    }

    private static bool Prefix(Component __instance, Humanoid __0, ref bool __result, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(__0);
        var continueOriginal = WardInteractionPatchTargets.TryGetRestriction(__instance, out var restriction)
            ? WardAccess.TryBlockInteraction(restriction, __instance, player, ref __result)
            : WardAccess.TryBlockInteraction(__instance, player, ref __result);
        if (continueOriginal && restriction != WardRestrictionOptions.None)
        {
            __state.EnterRestriction(restriction);
        }

        return continueOriginal;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

[HarmonyPatch]
internal static class StationUsePatches
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(ArmorStand), nameof(ArmorStand.UseItem));
        yield return AccessTools.DeclaredMethod(typeof(MapTable), nameof(MapTable.OnRead), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) });
        yield return AccessTools.DeclaredMethod(typeof(MapTable), nameof(MapTable.OnRead), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData), typeof(bool) });
        yield return AccessTools.DeclaredMethod(typeof(MapTable), nameof(MapTable.OnWrite));
    }

    private static bool Prefix(Component __instance, Humanoid __1, ref bool __result, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(__1);
        var continueOriginal = WardInteractionPatchTargets.TryGetRestriction(__instance, out var restriction)
            ? WardAccess.TryBlockInteraction(restriction, __instance, player, ref __result)
            : WardAccess.TryBlockInteraction(__instance, player, ref __result);
        if (continueOriginal && restriction != WardRestrictionOptions.None)
        {
            __state.EnterRestriction(restriction);
        }

        return continueOriginal;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

[HarmonyPatch]
internal static class ProcessingInteractionPatches
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnAddOre));
        yield return AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnAddFuel));
        yield return AccessTools.DeclaredMethod(typeof(Incinerator), nameof(Incinerator.OnIncinerate));
    }

    private static bool Prefix(Component __instance, Humanoid __1, ref bool __result)
    {
        return WardAccess.TryBlockAction(__instance, WardAccess.GetPlayer(__1), ref __result);
    }
}

[HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
internal static class TeleportWorldTeleportPatch
{
    private static bool Prefix(TeleportWorld __instance, Player player, out WardCheckScopeState __state)
    {
        __state = default;
        var continueOriginal = WardAccess.TryBlockVoid(WardRestrictionOptions.Portals, __instance, player);
        if (continueOriginal)
        {
            __state.EnterRestriction(WardRestrictionOptions.Portals);
        }

        return continueOriginal;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

[HarmonyPatch(typeof(TeleportWorldTrigger), nameof(TeleportWorldTrigger.OnTriggerEnter))]
[HarmonyPriority(800)]
[HarmonyBefore(new[] { "org.bepinex.plugins.targetportal" })]
internal static class TeleportWorldTriggerPatch
{
    private static bool Prefix(TeleportWorldTrigger __instance, Collider colliderIn, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(colliderIn);
        if (player == null)
        {
            return true;
        }

        var portal = __instance.GetComponentInParent<TeleportWorld>();
        if (portal == null)
        {
            TargetPortalCompat.ClearBlockedPortalEntry(player);
            return true;
        }

        if (WardAccess.TryBlockVoid(WardRestrictionOptions.Portals, portal, player))
        {
            __state.EnterRestriction(WardRestrictionOptions.Portals);
            TargetPortalCompat.ClearBlockedPortalEntry(player);
            return true;
        }

        TargetPortalCompat.MarkBlockedPortalEntry(player);
        TargetPortalCompat.ClosePortalSelection();
        return false;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

internal static class TargetPortalCompat
{
    private const string PluginGuid = "org.bepinex.plugins.targetportal";

    private static readonly Assembly? TargetPortalAssembly = GetPluginAssembly();
    private static readonly Type? MapType = TargetPortalAssembly?.GetType("TargetPortal.Map");
    private static readonly Type? OpenMapPatchType = TargetPortalAssembly?.GetType("TargetPortal.Map+OpenMapOnPortalEnter");
    private static readonly Type? TogglePortalModePatchType = TargetPortalAssembly?.GetType("TargetPortal.TargetPortal+TogglePortalMode");
    private static readonly Type? StartPortalFetchingPatchType = TargetPortalAssembly?.GetType("TargetPortal.TargetPortal+StartPortalFetching");

    private static readonly MethodInfo? OpenMapPrefixMethod = OpenMapPatchType != null
        ? AccessTools.Method(OpenMapPatchType, "Prefix", new[] { typeof(TeleportWorldTrigger), typeof(Collider) })
        : null;
    private static readonly MethodInfo? TogglePortalModePrefixMethod = TogglePortalModePatchType != null
        ? AccessTools.Method(TogglePortalModePatchType, "Prefix", new[] { typeof(TeleportWorld), typeof(bool) })
        : null;
    private static readonly MethodInfo? OnPortalModeChangeMethod = StartPortalFetchingPatchType != null
        ? AccessTools.Method(StartPortalFetchingPatchType, "OnPortalModeChange", new[] { typeof(long), typeof(ZDOID), typeof(int), typeof(string), typeof(string) })
        : null;
    private static readonly MethodInfo? HandlePortalClickMethod = MapType != null
        ? AccessTools.Method(MapType, "HandlePortalClick")
        : null;
    private static readonly MethodInfo? CancelTeleportMethod = MapType != null
        ? AccessTools.Method(MapType, "CancelTeleport", Type.EmptyTypes)
        : null;

    private static bool _blockedPortalEntry;

    private static Assembly? GetPluginAssembly()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo))
        {
            return null;
        }

        return pluginInfo.Instance?.GetType().Assembly;
    }

    internal static void TryPatch(Harmony harmony)
    {
        if (TargetPortalAssembly == null || harmony == null)
        {
            return;
        }

        PatchMethod(harmony, OpenMapPrefixMethod, nameof(TargetPortalOpenMapPrefix));
        PatchMethod(harmony, TogglePortalModePrefixMethod, nameof(TargetPortalTogglePortalModePrefix));
        PatchMethod(harmony, OnPortalModeChangeMethod, nameof(TargetPortalOnPortalModeChangePrefix));
        PatchMethod(harmony, HandlePortalClickMethod, nameof(TargetPortalHandlePortalClickPrefix));
    }

    internal static void MarkBlockedPortalEntry(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        _blockedPortalEntry = true;
    }

    internal static void ClearBlockedPortalEntry(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        _blockedPortalEntry = false;
    }

    internal static void ClosePortalSelection()
    {
        _blockedPortalEntry = false;

        try
        {
            if (Minimap.instance != null)
            {
                Minimap.instance.SetMapMode((Minimap.MapMode)1);
            }
        }
        catch
        {
            // Ignore map close failures and still attempt to cancel TargetPortal mode.
        }

        try
        {
            CancelTeleportMethod?.Invoke(null, Array.Empty<object>());
        }
        catch
        {
            // Ignore TargetPortal cleanup failures.
        }
    }

    private static void PatchMethod(Harmony harmony, MethodInfo? originalMethod, string prefixName)
    {
        if (originalMethod == null)
        {
            return;
        }

        var prefixMethod = AccessTools.DeclaredMethod(typeof(TargetPortalCompat), prefixName);
        if (prefixMethod == null)
        {
            return;
        }

        harmony.Patch(originalMethod, prefix: new HarmonyMethod(prefixMethod));
    }

    private static bool TargetPortalOpenMapPrefix(TeleportWorldTrigger __0, Collider __1)
    {
        var player = WardAccess.GetPlayer(__1);
        if (player == null || player != Player.m_localPlayer)
        {
            return true;
        }

        var portal = __0 != null ? __0.GetComponentInParent<TeleportWorld>() : null;
        if (portal == null)
        {
            ClearBlockedPortalEntry(player);
            return true;
        }

        if (WardAccess.TryBlockVoid(WardRestrictionOptions.Portals, portal, player))
        {
            ClearBlockedPortalEntry(player);
            return true;
        }

        MarkBlockedPortalEntry(player);
        ClosePortalSelection();
        return false;
    }

    private static bool TargetPortalHandlePortalClickPrefix()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            _blockedPortalEntry = false;
            return true;
        }

        if (!_blockedPortalEntry &&
            !WardAccess.ShouldBlockRestriction(WardRestrictionOptions.Portals, player.transform.position, 0f, player, flash: false))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(player);
        ClosePortalSelection();
        return false;
    }

    private static bool TargetPortalTogglePortalModePrefix(TeleportWorld __0, bool __1)
    {
        if (__1)
        {
            return true;
        }

        var player = Player.m_localPlayer;
        if (player == null || __0 == null)
        {
            return true;
        }

        return !WardAccess.ShouldBlockRestriction(WardRestrictionOptions.Portals, __0, player, 0f, flash: false);
    }

    private static bool TargetPortalOnPortalModeChangePrefix(long __0, ZDOID __1)
    {
        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(__0, out var playerId))
        {
            return false;
        }

        var portalZdo = ZDOMan.instance?.GetZDO(__1);
        if (portalZdo == null)
        {
            return true;
        }

        return WardAccess.CheckRestrictionAccess(WardRestrictionOptions.Portals, portalZdo.GetPosition(), 0f, playerId, flash: false);
    }

}

[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Pickup))]
internal static class ItemDropPickupPatch
{
    private static bool Prefix(ItemDrop __instance, Humanoid character, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(character);
        if (!WardItemPrefabPolicy.CanAnyPickupBeBlocked() || !WardItemPrefabPolicy.ShouldBlockPickup(__instance))
        {
            __state.EnterManagedWardAllow();
            return true;
        }

        if (!WardAccess.ShouldBlockPickup(__instance, player))
        {
            __state.EnterRestriction(WardRestrictionOptions.Pickup);
            return true;
        }

        WardAccess.ShowNoAccessMessage(player);
        return false;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
internal static class HumanoidPickupPatch
{
    private static bool Prefix(Humanoid __instance, GameObject go, ref bool __result, out WardCheckScopeState __state)
    {
        __state = default;
        var player = WardAccess.GetPlayer(__instance);
        if (!WardItemPrefabPolicy.CanAnyPickupBeBlocked() || !WardItemPrefabPolicy.ShouldBlockPickup(go))
        {
            __state.EnterManagedWardAllow();
            return true;
        }

        if (!WardAccess.ShouldBlockPickup(go, player))
        {
            __state.EnterRestriction(WardRestrictionOptions.Pickup);
            return true;
        }

        __result = false;
        return false;
    }

    private static void Postfix(WardCheckScopeState __state)
    {
        __state.Dispose();
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
internal static class PlayerTryPlacePiecePatch
{
    private static bool Prefix(Player __instance, ref bool __result)
    {
        var ghost = __instance.m_placementGhost;
        if (ghost == null)
        {
            return true;
        }

        if (!WardAccess.TryBlockManagedWardPlacement(__instance, ghost.transform, ghost.transform.position, ref __result))
        {
            return false;
        }

        // Player.TryPlacePiece immediately calls UpdatePlacementGhost(true), which already
        // routes through PrivateArea.CheckAccess for the current ghost position.
        // Re-running the same access pre-check here adds avoidable placement-time overhead.
        return true;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetupPlacementGhost))]
internal static class PlayerSetupPlacementGhostPatch
{
    private static void Postfix(Player __instance)
    {
        var area = __instance.m_placementGhost != null ? __instance.m_placementGhost.GetComponent<PrivateArea>() : null;
        if (StuWardArea.IsManaged(area))
        {
            WardSettings.ApplyPlacementGhostPreviewRadius(area!);
        }
    }
}

[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class PlayerUpdatePlacementGhostPatch
{
    private static void Postfix(Player __instance)
    {
        var placementGhost = __instance.m_placementGhost;
        var area = placementGhost != null ? placementGhost.GetComponent<PrivateArea>() : null;
        if (placementGhost != null && StuWardArea.IsManaged(area))
        {
            WardSettings.ApplyPlacementGhostPreviewRadius(area!);

            if (__instance.GetPlacementStatus() == Player.PlacementStatus.Valid &&
                ManagedWardPlacementPreviewService.ShouldShowAsInvalid(__instance, placementGhost.transform, placementGhost.transform.position))
            {
                __instance.m_placementStatus = Player.PlacementStatus.MoreSpace;
                __instance.SetPlacementGhostValid(false);
            }
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
internal static class PlayerPlacePiecePatch
{
    private static bool Prefix(Player __instance, Piece piece, Vector3 pos)
    {
        if (!WardAccess.TryBlockManagedWardPlacement(__instance, piece, pos))
        {
            return false;
        }

        return WardAccess.TryBlockPlacement(__instance, pos, 0f);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.CheckCanRemovePiece))]
internal static class PlayerCheckCanRemovePiecePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Player __instance, Piece __0, ref bool __result)
    {
        if (!WardPatchHelpers.ShouldBlockRemoval(__0, __instance))
        {
            return;
        }

        WardAccess.ShowNoAccessMessage(__instance);
        __result = false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.RemovePiece))]
internal static class PlayerRemovePiecePatch
{
    private static bool Prefix(Player __instance, ref bool __result)
    {
        var piece = WardPatchHelpers.FindRemoveTarget(__instance);
        if (!WardPatchHelpers.ShouldBlockRemoval(piece, __instance))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(__instance);
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Player), "Repair")]
internal static class PlayerRepairPatch
{
    private static bool Prefix(Player __instance, Piece __1)
    {
        var piece = __instance.GetHoveringPiece() ?? __1;
        if (piece == null || !WardAccess.ShouldBlock(piece.transform.position, 0f, __instance))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy), typeof(GameObject))]
internal static class ZNetSceneDestroyPatch
{
    private static bool Prefix(GameObject go)
    {
        var piece = go != null ? go.GetComponent<Piece>() ?? go.GetComponentInParent<Piece>() : null;
        if (!WardPatchHelpers.ShouldBlockLocalRemoval(piece))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(Player.m_localPlayer);
        return false;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.SpawnOnHitTerrain))]
internal static class AttackSpawnOnHitTerrainPatch
{
    private static bool Prefix(Vector3 hitPoint, GameObject prefab, Character character, ref GameObject __result)
    {
        var player = character as Player;
        if (!WardAccess.ShouldBlock(hitPoint, WardAccess.GetTerrainRadius(prefab), player))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(player);
        __result = null!;
        return false;
    }
}

[HarmonyPatch(typeof(TerrainOp), nameof(TerrainOp.Awake))]
internal static class TerrainOpAwakePatch
{
    private static bool Prefix(TerrainOp __instance)
    {
        var nview = __instance.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && !nview.IsOwner())
        {
            return true;
        }

        var player = Player.m_localPlayer;
        if (!WardAccess.ShouldBlock(__instance.transform.position, __instance.GetRadius(), player))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(player);
        Object.Destroy(__instance.gameObject);
        return false;
    }
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
internal static class WearNTearDamagePatch
{
    private static bool Prefix(WearNTear __instance, HitData hit)
    {
        var attacker = hit.GetAttacker();
        var piece = __instance.GetComponent<Piece>();
        var area = __instance.GetComponent<PrivateArea>();
        if (ManagedWardIdentity.EnsureManagedComponent(area))
        {
            var controllingPlayer = WardPatchHelpers.GetLocalPlayerForCharacter(attacker);
            if (controllingPlayer != null && !WardAccess.CanControlManagedWard(area, controllingPlayer.GetPlayerID()))
            {
                WardAccess.ShowNoAccessMessage(controllingPlayer);
            }

            return false;
        }

        var blockReason = WardPatchHelpers.GetBuildingDamageBlockReason(__instance.transform.position, piece, attacker);
        if (blockReason == BuildingDamageBlockReason.None)
        {
            return true;
        }

        var localPlayer = WardPatchHelpers.GetLocalPlayerForCharacter(attacker);
        if (blockReason == BuildingDamageBlockReason.FriendlyWardProtection)
        {
            WardAccess.ShowProtectedBuildingDamageMessage(localPlayer);
        }
        else
        {
            WardAccess.ShowNoAccessMessage(localPlayer);
        }

        return false;
    }
}

[HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
internal static class WearNTearRpcDamagePatch
{
    private static bool Prefix(WearNTear __instance, long sender, HitData hit)
    {
        var area = __instance.GetComponent<PrivateArea>();
        if (ManagedWardIdentity.EnsureManagedComponent(area))
        {
            return false;
        }

        return WardPatchHelpers.GetBuildingDamageBlockReason(__instance.transform.position, __instance.GetComponent<Piece>(), hit, sender) ==
               BuildingDamageBlockReason.None;
    }
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
internal static class WearNTearRemovePatch
{
    private static bool Prefix(WearNTear __instance)
    {
        var piece = __instance.GetComponent<Piece>();
        if (!WardPatchHelpers.ShouldBlockLocalRemoval(piece))
        {
            return true;
        }

        WardAccess.ShowNoAccessMessage(Player.m_localPlayer);
        return false;
    }
}

[HarmonyPatch(typeof(WearNTear), "RPC_Remove")]
internal static class WearNTearRpcRemovePatch
{
    private static bool Prefix(WearNTear __instance, long sender, bool blockDrop)
    {
        var piece = __instance.GetComponent<Piece>();
        if (blockDrop && WardPatchHelpers.IsPlacedConsumablePiece(piece))
        {
            var consumeDecision = WardPatchHelpers.EvaluatePlacedConsumableRemovalBySender(piece, sender);
            if (consumeDecision == WardPatchHelpers.ProtectedRpcDecision.Allow)
            {
                return true;
            }

            if (consumeDecision == WardPatchHelpers.ProtectedRpcDecision.Deny)
            {
                WardAccess.ShowNoAccessMessage(WardPatchHelpers.GetLocalPlayerForSender(sender));
            }

            return false;
        }

        var decision = WardPatchHelpers.EvaluateRemovalBySender(piece, sender);
        if (decision == WardPatchHelpers.ProtectedRpcDecision.Allow)
        {
            return true;
        }

        if (decision == WardPatchHelpers.ProtectedRpcDecision.Deny)
        {
            WardAccess.ShowNoAccessMessage(WardPatchHelpers.GetLocalPlayerForSender(sender));
        }

        return false;
    }
}

[HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
internal static class DestructibleDamagePatch
{
    private static bool Prefix(Destructible __instance, HitData hit)
    {
        var attacker = hit.GetAttacker();
        var blockReason = WardPatchHelpers.GetBuildingDamageBlockReason(
            __instance.transform.position,
            WardPatchHelpers.GetProtectedBuildingPiece(__instance),
            attacker);
        if (blockReason == BuildingDamageBlockReason.None)
        {
            return true;
        }

        var localPlayer = WardPatchHelpers.GetLocalPlayerForCharacter(attacker);
        if (blockReason == BuildingDamageBlockReason.FriendlyWardProtection)
        {
            WardAccess.ShowProtectedBuildingDamageMessage(localPlayer);
        }
        else
        {
            WardAccess.ShowNoAccessMessage(localPlayer);
        }

        return false;
    }
}

[HarmonyPatch(typeof(Destructible), "RPC_Damage")]
internal static class DestructibleRpcDamagePatch
{
    private static bool Prefix(Destructible __instance, long sender, HitData hit)
    {
        return WardPatchHelpers.GetBuildingDamageBlockReason(
                   __instance.transform.position,
                   WardPatchHelpers.GetProtectedBuildingPiece(__instance),
                   hit,
                   sender) ==
               BuildingDamageBlockReason.None;
    }
}

[HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
internal static class TreeBaseDamagePatch
{
    private static bool Prefix(TreeBase __instance, HitData hit)
    {
        return !WardPatchHelpers.ShouldBlockDamageByCharacter(__instance.transform.position, hit.GetAttacker());
    }
}

[HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
internal static class TreeBaseRpcDamagePatch
{
    private static bool Prefix(TreeBase __instance, long sender)
    {
        return WardPatchHelpers.EvaluateDamageBySender(__instance.transform.position, sender) ==
               WardPatchHelpers.ProtectedRpcDecision.Allow;
    }
}

[HarmonyPatch(typeof(Feast), "RPC_TryEat")]
internal static class FeastRpcTryEatPatch
{
    private static bool Prefix(Feast __instance, long sender)
    {
        var decision = WardPatchHelpers.EvaluateInteractionBySender(
            __instance.transform.position,
            sender,
            WardRestrictionOptions.PlacedConsumables);
        if (decision == WardPatchHelpers.ProtectedRpcDecision.Allow)
        {
            return true;
        }

        if (decision == WardPatchHelpers.ProtectedRpcDecision.Deny)
        {
            WardAccess.ShowNoAccessMessage(WardPatchHelpers.GetLocalPlayerForSender(sender));
        }

        return false;
    }
}

[HarmonyPatch(typeof(Player), "UseHotbarItem")]
[HarmonyPriority(800)]
[HarmonyBefore(new[] { "kg.TameableCollector" })]
internal static class PlayerUseHotbarItemPatch
{
    private static bool Prefix(Player __instance, int index)
    {
        if (__instance != Player.m_localPlayer || __instance.m_inventory == null)
        {
            return true;
        }

        var item = __instance.m_inventory.GetItemAt(index - 1, 0);
        return WardAccess.TryBlockItemUse(__instance, item);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class HumanoidUseItemPatch
{
    private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
    {
        return WardAccess.TryBlockItemUse(WardAccess.GetPlayer(__instance), item);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
internal static class HumanoidUpdateEquipmentPatch
{
    private const float ForceUnequipCheckIntervalSeconds = 0.15f;
    private static float _nextForceUnequipCheckTime;

    private static void Prefix(Humanoid __instance)
    {
        if (__instance != Player.m_localPlayer || !WardAccess.HasEnabledManagedWards() || !Plugin.HasBlockedItems())
        {
            return;
        }

        if (Time.unscaledTime < _nextForceUnequipCheckTime)
        {
            return;
        }

        _nextForceUnequipCheckTime = Time.unscaledTime + ForceUnequipCheckIntervalSeconds;
        WardAccess.TryForceUnequipBlockedItems(Player.m_localPlayer);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
internal static class HumanoidEquipItemPatch
{
    private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
    {
        return WardAccess.TryBlockItemUse(WardAccess.GetPlayer(__instance), item, ref __result);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackPatch
{
    private static bool Prefix(Humanoid __instance, ref bool __result)
    {
        return WardAccess.TryBlockAttack(WardAccess.GetPlayer(__instance), ref __result);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
[HarmonyPriority(800)]
[HarmonyBefore(new[] { "meldurson.valheim.PortablePals" })]
internal static class AttackStartBlockedItemTargetPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        return WardAccess.TryBlockAttack(WardAccess.GetPlayer(character), weapon, ref __result);
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnRightClickItem))]
[HarmonyPriority(800)]
[HarmonyBefore(new[] { "kg.TameableCollector" })]
internal static class InventoryGuiOnRightClickItemPatch
{
    private static bool Prefix(ItemDrop.ItemData item)
    {
        return WardAccess.TryBlockItemUse(Player.m_localPlayer, item);
    }
}

[HarmonyPatch]
internal static class TameableCollectorCollectorItemPatch
{
    private static Type? GetCollectorItemType()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            var collectorItemType = assemblies[index].GetType("TameableCollector.Mechanics+CollectorItem", throwOnError: false);
            if (collectorItemType != null)
            {
                return collectorItemType;
            }
        }

        return null;
    }

    private static bool Prepare()
    {
        return GetCollectorItemType() != null;
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var collectorItemType = GetCollectorItemType();
        if (collectorItemType == null)
        {
            yield break;
        }

        var tryCatchMethod = AccessTools.Method(collectorItemType, "TryCatch");
        if (tryCatchMethod != null)
        {
            yield return tryCatchMethod;
        }

        var summonMethod = AccessTools.Method(collectorItemType, "Summon");
        if (summonMethod != null)
        {
            yield return summonMethod;
        }
    }

    [HarmonyPriority(800)]
    private static bool Prefix(object __instance, MethodBase __originalMethod)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return true;
        }

        ItemDrop.ItemData? item = null;
        try
        {
            item = Traverse.Create(__instance).Property("Item").GetValue<ItemDrop.ItemData>();
        }
        catch
        {
            return true;
        }

        if (__originalMethod.Name == "TryCatch" && player.m_hoveringCreature != null)
        {
            return WardAccess.TryBlockItemUse(player, item, player.m_hoveringCreature.transform.position);
        }

        return WardAccess.TryBlockItemUse(player, item);
    }
}

[HarmonyPatch]
internal static class AzuCraftyBoxesNearbyContainersPatch
{
    private const string AzuCraftyBoxesPluginGuid = "Azumatt.AzuCraftyBoxes";

    private static readonly Dictionary<Type, Func<object, Vector3>?> GetPositionDelegates = new();
    private static readonly Dictionary<object, bool> FrameBlockCache = new(ReferenceEqualityComparer.Instance);
    private static readonly Type? ContainerInterfaceType = ResolveContainerInterfaceType();
    private static int _cachedFrame = -1;

    private static bool Prepare()
    {
        return ContainerInterfaceType != null;
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        if (ContainerInterfaceType == null)
        {
            yield break;
        }

        foreach (var type in GetLoadableTypes(ContainerInterfaceType.Assembly))
        {
            if (type == null || type.IsAbstract || type.IsInterface || !ContainerInterfaceType.IsAssignableFrom(type))
            {
                continue;
            }

            var itemCountMethod = AccessTools.DeclaredMethod(type, "ItemCount", new[] { typeof(string) });
            if (itemCountMethod != null)
            {
                yield return itemCountMethod;
            }
        }
    }

    private static bool Prefix(object __instance, ref int __result)
    {
        var player = Player.m_localPlayer;
        if (player == null || !WardAccess.HasEnabledManagedWards())
        {
            return true;
        }

        ResetFrameCacheIfNeeded();
        if (FrameBlockCache.TryGetValue(__instance, out var blocked))
        {
            if (!blocked)
            {
                return true;
            }

            __result = 0;
            return false;
        }

        if (!TryGetContainerPosition(__instance, out var position))
        {
            FrameBlockCache[__instance] = false;
            return true;
        }

        blocked = WardAccess.ShouldBlockRestriction(WardRestrictionOptions.Containers, position, 0f, player, flash: false);
        FrameBlockCache[__instance] = blocked;
        if (!blocked)
        {
            return true;
        }

        __result = 0;
        return false;
    }

    private static Type? ResolveContainerInterfaceType()
    {
        if (!Chainloader.PluginInfos.TryGetValue(AzuCraftyBoxesPluginGuid, out var pluginInfo))
        {
            return null;
        }

        return pluginInfo.Instance?.GetType().Assembly.GetType("AzuCraftyBoxes.IContainers.IContainer");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            if (exception.Types == null)
            {
                return Array.Empty<Type>();
            }

            var loadableTypes = new List<Type>(exception.Types.Length);
            for (var index = 0; index < exception.Types.Length; index++)
            {
                if (exception.Types[index] != null)
                {
                    loadableTypes.Add(exception.Types[index]!);
                }
            }

            return loadableTypes;
        }
    }

    private static void ResetFrameCacheIfNeeded()
    {
        var currentFrame = Time.frameCount;
        if (_cachedFrame == currentFrame)
        {
            return;
        }

        _cachedFrame = currentFrame;
        FrameBlockCache.Clear();
    }

    private static bool TryGetContainerPosition(object container, out Vector3 position)
    {
        var type = container.GetType();
        if (!GetPositionDelegates.TryGetValue(type, out var getPositionDelegate))
        {
            getPositionDelegate = CreateGetPositionDelegate(type);
            GetPositionDelegates[type] = getPositionDelegate;
        }

        if (getPositionDelegate == null)
        {
            position = default;
            return false;
        }

        try
        {
            position = getPositionDelegate(container);
            return true;
        }
        catch
        {
        }

        position = default;
        return false;
    }

    private static Func<object, Vector3>? CreateGetPositionDelegate(Type type)
    {
        var getPositionMethod = AccessTools.Method(type, "GetPosition");
        if (getPositionMethod == null ||
            getPositionMethod.IsStatic ||
            getPositionMethod.ReturnType != typeof(Vector3) ||
            getPositionMethod.GetParameters().Length != 0)
        {
            return null;
        }

        try
        {
            var targetParameter = Expression.Parameter(typeof(object), "target");
            var instanceExpression = Expression.Convert(targetParameter, type);
            var callExpression = Expression.Call(instanceExpression, getPositionMethod);
            return Expression.Lambda<Func<object, Vector3>>(callExpression, targetParameter).Compile();
        }
        catch
        {
            return null;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}

[HarmonyPatch(typeof(Player), "AutoPickup")]
internal static class PlayerAutoPickupPatch
{
    private const int MaxBufferedAutoPickupColliders = 4096;
    private static Collider[] _autoPickupColliders = new Collider[64];
    private static readonly List<PrivateArea> DeniedAutoPickupWardCandidates = new();

    private static bool Prefix(Player __instance, float dt)
    {
        if (__instance.IsTeleporting() || !Player.m_enableAutoPickup)
        {
            return false;
        }

        var checkWardAccess = WardAccess.HasEnabledManagedWards() && WardItemPrefabPolicy.CanAnyPickupBeBlocked();
        var pickupPoint = __instance.transform.position + Vector3.up;
        var playerId = __instance.GetPlayerID();
        var inventory = __instance.GetInventory();
        var autoPickupRange = __instance.m_autoPickupRange;
        var autoPickupRangeSqr = autoPickupRange * autoPickupRange;
        var wardAccessEvaluated = false;
        DeniedAutoPickupWardCandidates.Clear();
        var hasDeniedWardCandidates = false;
        var colliders = _autoPickupColliders;
        var colliderCount = Physics.OverlapSphereNonAlloc(
            pickupPoint,
            autoPickupRange,
            colliders,
            __instance.m_autoPickupMask);
        while (colliderCount == colliders.Length && colliders.Length < MaxBufferedAutoPickupColliders)
        {
            Array.Resize(
                ref _autoPickupColliders,
                Math.Min(_autoPickupColliders.Length * 2, MaxBufferedAutoPickupColliders));
            colliders = _autoPickupColliders;
            colliderCount = Physics.OverlapSphereNonAlloc(
                pickupPoint,
                autoPickupRange,
                colliders,
                __instance.m_autoPickupMask);
        }

        if (colliderCount == colliders.Length)
        {
            colliders = Physics.OverlapSphere(pickupPoint, autoPickupRange, __instance.m_autoPickupMask);
            colliderCount = colliders.Length;
        }

        for (var index = 0; index < colliderCount; index++)
        {
            var collider = colliders[index];
            if (collider == null || collider.attachedRigidbody == null)
            {
                continue;
            }

            var itemDrop = collider.attachedRigidbody.GetComponent<ItemDrop>();
            FloatingTerrainDummy? floatingTerrainDummy = null;

            if (itemDrop == null)
            {
                floatingTerrainDummy = collider.attachedRigidbody.GetComponent<FloatingTerrainDummy>();
                if (floatingTerrainDummy != null)
                {
                    var parent = floatingTerrainDummy.m_parent;
                    if (parent != null)
                    {
                        itemDrop = parent.GetComponent<ItemDrop>();
                    }
                }
            }

            if (itemDrop == null || !itemDrop.m_autoPickup || itemDrop.IsPiece() || __instance.HaveUniqueKey(itemDrop.m_itemData.m_shared.m_name))
            {
                continue;
            }

            var distanceSqr = (itemDrop.transform.position - pickupPoint).sqrMagnitude;
            if (distanceSqr > autoPickupRangeSqr)
            {
                continue;
            }

            var itemView = itemDrop.GetComponent<ZNetView>();
            if (itemView == null || !itemView.IsValid())
            {
                continue;
            }

            var shouldCheckWardAccess = checkWardAccess && WardItemPrefabPolicy.ShouldBlockPickup(itemDrop);
            if (shouldCheckWardAccess && !wardAccessEvaluated)
            {
                hasDeniedWardCandidates =
                    WardAccess.CollectDeniedManagedWardCandidates(
                        playerId,
                        WardAccess.GetCandidateManagedWards(pickupPoint, autoPickupRange, requireEnabled: true),
                        WardRestrictionOptions.Pickup,
                        DeniedAutoPickupWardCandidates) > 0;
                wardAccessEvaluated = true;
            }

            var wardBlocksPickup =
                shouldCheckWardAccess &&
                hasDeniedWardCandidates &&
                WardAccess.IsInsideAnyManagedWard(itemDrop.transform.position, 0f, DeniedAutoPickupWardCandidates);

            if (wardBlocksPickup)
            {
                continue;
            }

            if (!itemDrop.CanPickup())
            {
                itemDrop.RequestOwn();
                continue;
            }

            if (itemDrop.InTar())
            {
                continue;
            }

            itemDrop.Load();
            if (!inventory.CanAddItem(itemDrop.m_itemData) || itemDrop.m_itemData.GetWeight() + inventory.GetTotalWeight() > __instance.GetMaxCarryWeight())
            {
                continue;
            }

            if (distanceSqr < 0.09f)
            {
                __instance.Pickup(itemDrop.gameObject);
                continue;
            }

            var movement = (pickupPoint - itemDrop.transform.position).normalized * (15f * dt);
            itemDrop.transform.position += movement;
            if (floatingTerrainDummy != null)
            {
                floatingTerrainDummy.transform.position += movement;
            }
        }

        return false;
    }
}

internal static class WardPatchHelpers
{
    internal enum ProtectedRpcDecision
    {
        Allow,
        Deny,
        Unresolved
    }

    private sealed class StumpClassification
    {
        internal bool Initialized;
        internal string CachedName = string.Empty;
        internal bool IsLikelyStump;
    }

    private static readonly string[] StumpNameTokens = { "stub", "stump", "stomp" };
    private static readonly ConditionalWeakTable<GameObject, StumpClassification> StumpClassificationCache = new();

    internal static Piece? FindRemoveTarget(Player player)
    {
        var hoveringPiece = player.GetHoveringPiece();
        if (hoveringPiece != null)
        {
            return hoveringPiece;
        }

        var camera = GameCamera.instance;
        if (camera == null || player.m_eye == null)
        {
            return null;
        }

        if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 50f, player.m_removeRayMask))
        {
            return null;
        }

        if (Vector3.Distance(hit.point, player.m_eye.position) >= player.m_maxPlaceDistance)
        {
            return null;
        }

        var piece = hit.collider.GetComponentInParent<Piece>();
        if (piece != null)
        {
            return piece;
        }

        var heightmap = hit.collider.GetComponent<Heightmap>();
        return heightmap != null ? TerrainModifier.FindClosestModifierPieceInRange(hit.point, 2.5f) : null;
    }

    internal static bool ShouldBlockLocalRemoval(Piece? piece)
    {
        var player = Player.m_localPlayer;
        if (piece == null || player == null || !player.InPlaceMode())
        {
            return false;
        }

        var hoveringPiece = player.GetHoveringPiece();
        if (hoveringPiece != piece && FindRemoveTarget(player) != piece)
        {
            return false;
        }

        return ShouldBlockRemoval(piece, player);
    }

    internal static bool ShouldBlockRemoval(Piece? piece, Player? player)
    {
        if (piece == null || player == null)
        {
            return false;
        }

        var area = piece.GetComponent<PrivateArea>();
        return ManagedWardIdentity.EnsureManagedComponent(area)
            ? !WardAccess.CanControlManagedWard(area, player.GetPlayerID())
            : WardAccess.ShouldBlock(piece.transform.position, 0f, player);
    }

    internal static ProtectedRpcDecision EvaluateRemovalBySender(Piece? piece, long sender)
    {
        if (piece == null)
        {
            return ProtectedRpcDecision.Allow;
        }

        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return ProtectedRpcDecision.Unresolved;
        }

        var area = piece.GetComponent<PrivateArea>();
        if (ManagedWardIdentity.EnsureManagedComponent(area))
        {
            return WardAccess.CanControlManagedWard(area, playerId)
                ? ProtectedRpcDecision.Allow
                : ProtectedRpcDecision.Deny;
        }

        return WardAccess.CheckAccess(piece.transform.position, 0f, playerId)
            ? ProtectedRpcDecision.Allow
            : ProtectedRpcDecision.Deny;
    }

    internal static bool IsPlacedConsumablePiece(Piece? piece)
    {
        return piece != null && WardAccess.IsPlacedConsumable(piece.GetComponent<ItemDrop>());
    }

    internal static ProtectedRpcDecision EvaluatePlacedConsumableRemovalBySender(Piece? piece, long sender)
    {
        if (piece == null)
        {
            return ProtectedRpcDecision.Allow;
        }

        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return ProtectedRpcDecision.Unresolved;
        }

        return WardAccess.CheckRestrictionAccess(WardRestrictionOptions.PlacedConsumables, piece.transform.position, 0f, playerId)
            ? ProtectedRpcDecision.Allow
            : ProtectedRpcDecision.Deny;
    }

    internal static ProtectedRpcDecision EvaluateDamageBySender(Vector3 point, long sender)
    {
        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return ProtectedRpcDecision.Unresolved;
        }

        return WardAccess.CheckAccess(point, 0f, playerId)
            ? ProtectedRpcDecision.Allow
            : ProtectedRpcDecision.Deny;
    }

    internal static Piece? GetProtectedBuildingPiece(Component? target)
    {
        if (target == null || IsLikelyStumpDestructible(target))
        {
            return null;
        }

        return target.GetComponent<Piece>();
    }

    internal static BuildingDamageBlockReason GetBuildingDamageBlockReason(Vector3 point, Piece? piece, long sender)
    {
        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return BuildingDamageBlockReason.UnresolvedSender;
        }

        return EvaluateBuildingDamagePolicy(point, piece, DamageSourceKind.Player, playerId);
    }

    internal static BuildingDamageBlockReason GetBuildingDamageBlockReason(Vector3 point, Piece? piece, HitData? hit, long sender)
    {
        var attacker = hit?.GetAttacker();
        return attacker != null
            ? GetBuildingDamageBlockReason(point, piece, attacker)
            : GetBuildingDamageBlockReason(point, piece, sender);
    }

    internal static BuildingDamageBlockReason GetBuildingDamageBlockReason(Vector3 point, Piece? piece, Character? attacker)
    {
        return EvaluateBuildingDamagePolicy(point, piece, GetDamageSourceKind(attacker), GetPlayerIdFromCharacter(attacker));
    }

    internal static ProtectedRpcDecision EvaluateInteractionBySender(Vector3 point, long sender)
    {
        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return ProtectedRpcDecision.Unresolved;
        }

        return WardAccess.CheckAccess(point, 0f, playerId)
            ? ProtectedRpcDecision.Allow
            : ProtectedRpcDecision.Deny;
    }

    internal static ProtectedRpcDecision EvaluateInteractionBySender(Vector3 point, long sender, WardRestrictionOptions restriction)
    {
        if (!TryResolveAuthoritativeSenderPlayerId(sender, out var playerId))
        {
            return ProtectedRpcDecision.Unresolved;
        }

        return WardAccess.CheckRestrictionAccess(restriction, point, 0f, playerId)
            ? ProtectedRpcDecision.Allow
            : ProtectedRpcDecision.Deny;
    }

    internal static bool ShouldBlockDamageByCharacter(Vector3 point, Character? attacker)
    {
        var playerId = GetPlayerIdFromCharacter(attacker);
        if (playerId == 0L)
        {
            return false;
        }

        return !WardAccess.CheckAccess(point, 0f, playerId);
    }

    private static BuildingDamageBlockReason EvaluateBuildingDamagePolicy(
        Vector3 point,
        Piece? piece,
        DamageSourceKind sourceKind,
        long playerId)
    {
        var isBuildingTarget = piece != null;
        var insideEnabledWard = isBuildingTarget &&
                                WardAccess.FindNearestManagedWard(point, requireEnabled: true) != null;
        var blocksHostileCreatureDamage = isBuildingTarget &&
                                          sourceKind == DamageSourceKind.MonsterAI &&
                                          WardAccess.ShouldBlockHostileCreatureDamageToBuilding(point);
        var playerHasAccess = playerId == 0L ||
                              !WardAccess.EvaluateAccess(point, 0f, playerId, flash: false).IsDenied;

        return BuildingDamagePolicy.Evaluate(
            new BuildingDamagePolicyInput(
                isBuildingTarget,
                sourceKind,
                playerId,
                insideEnabledWard,
                blocksHostileCreatureDamage,
                playerHasAccess));
    }

    private static DamageSourceKind GetDamageSourceKind(Character? attacker)
    {
        if (attacker is Player)
        {
            return DamageSourceKind.Player;
        }

        if (attacker != null && attacker.IsTamed())
        {
            return DamageSourceKind.TamedCreature;
        }

        if (attacker?.GetBaseAI() is MonsterAI)
        {
            return DamageSourceKind.MonsterAI;
        }

        return DamageSourceKind.Unknown;
    }

    internal static Player? GetLocalPlayerForCharacter(Character? attacker)
    {
        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return null;
        }

        return localPlayer.GetPlayerID() == GetPlayerIdFromCharacter(attacker) ? localPlayer : null;
    }

    internal static Player? GetLocalPlayerForSender(long sender)
    {
        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return null;
        }

        return localPlayer.GetPlayerID() == GetPlayerIdFromSender(sender) ? localPlayer : null;
    }

    private static bool TryResolveAuthoritativeSenderPlayerId(long sender, out long playerId)
    {
        return WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out playerId);
    }

    private static long GetPlayerIdFromCharacter(Character? attacker)
    {
        switch (attacker)
        {
            case null:
                return 0L;
            case Player player:
                return player.GetPlayerID();
        }

        if (!attacker.IsTamed())
        {
            return 0L;
        }

        var ownerSessionId = attacker.GetOwner();
        if (ownerSessionId == 0L)
        {
            return 0L;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null && localPlayer.GetOwner() == ownerSessionId)
        {
            return localPlayer.GetPlayerID();
        }

        return GetPlayerIdFromSender(ownerSessionId);
    }

    private static long GetPlayerIdFromSender(long sender)
    {
        return WardOwnership.GetPlayerIdFromSender(sender);
    }

    private static bool IsLikelyStumpDestructible(Component target)
    {
        var gameObject = target.gameObject;
        var classification = StumpClassificationCache.GetOrCreateValue(gameObject);
        var targetName = gameObject.name ?? string.Empty;
        if (!classification.Initialized || !string.Equals(classification.CachedName, targetName, StringComparison.Ordinal))
        {
            classification.CachedName = targetName;
            classification.IsLikelyStump = ComputeIsLikelyStumpDestructible(gameObject, targetName);
            classification.Initialized = true;
        }

        return classification.IsLikelyStump;
    }

    private static bool ComputeIsLikelyStumpDestructible(GameObject target, string targetName)
    {
        return target.GetComponent<Destructible>() != null &&
               target.GetComponent<StaticPhysics>() != null &&
               IsLikelyStumpName(targetName);
    }

    private static bool IsLikelyStumpName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = NormalizeName(name);
        for (var index = 0; index < StumpNameTokens.Length; index++)
        {
            if (normalizedName.IndexOf(StumpNameTokens[index], StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeName(string name)
    {
        var normalizedName = name.Trim();
        var cloneIndex = normalizedName.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
        if (cloneIndex >= 0)
        {
            normalizedName = normalizedName.Substring(0, cloneIndex);
        }

        return normalizedName.Trim().ToLowerInvariant();
    }
}
