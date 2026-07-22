using System;
using HarmonyLib;
using LocalizationManager;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace STUWard;

[DisallowMultipleComponent]
internal sealed class ManagedWardLocalInitializationState : MonoBehaviour
{
    internal bool LocalInitializationComplete;
}

internal static class ManagedWardInitializationCoordinator
{
    internal static void EnsureLocalInitialization(PrivateArea area)
    {
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(area))
        {
            return;
        }

        var state = GetOrAddState(area);
        if (state.LocalInitializationComplete)
        {
            return;
        }

        WardSettings.CaptureAreaDefaults(area);
        state.LocalInitializationComplete = true;
    }

    internal static void EnsureNetworkInitialization(PrivateArea area, bool matchedByComponent, bool matchedByZdo)
    {
        ManagedWardLifecycle.NotifyAreaReady(area, matchedByComponent, matchedByZdo);
    }

    internal static bool TryGetValidZdo(PrivateArea area, out ZDO zdo)
    {
        return TryGetValidZdo(ManagedWardRef.FromArea(area), out zdo);
    }

    internal static bool TryGetValidZdo(ManagedWardRef ward, out ZDO zdo)
    {
        zdo = null!;
        if (!ward.HasValidNetworkIdentity)
        {
            return false;
        }

        zdo = ward.Zdo!;
        return true;
    }

    internal static ManagedWardLocalInitializationState GetOrAddState(PrivateArea area)
    {
        var state = area.GetComponent<ManagedWardLocalInitializationState>();
        if (state != null)
        {
            return state;
        }

        return area.gameObject.AddComponent<ManagedWardLocalInitializationState>();
    }
}

internal static class ManagedWardLifecycle
{
    internal static void NotifyAreaReady(PrivateArea? area, bool matchedByComponent, bool matchedByZdo)
    {
        var ward = ManagedWardRef.FromArea(area);
        if (area == null ||
            (!matchedByComponent && !matchedByZdo) ||
            !ManagedWardInitializationCoordinator.TryGetValidZdo(ward, out _))
        {
            return;
        }

        if (!ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        if (!context.NetworkInitializationComplete)
        {
            WardOwnership.TryStampLocalManagedWardOwnerAccount(ward);
            WardAccess.RegisterManagedWard(ward);
            WardPermittedSnapshots.Backfill(ward);
            WardSettings.ApplyAreaState(ward);
            context.NetworkInitializationComplete = true;
        }

        if (context.OwnershipObserved)
        {
            return;
        }

        WardOwnership.ObserveManagedWard(ward);
        context.OwnershipObserved = true;
    }

    internal static void NotifyAreaDestroyed(PrivateArea? area)
    {
        if (area == null)
        {
            return;
        }

        WardAccess.UnregisterManagedWard(area);
        ManagedWardRuntimeContexts.Forget(area);
    }

    internal static void NotifySessionReset()
    {
        ManagedWardRuntimeLifecycle.ResetSession();
        ManagedWardRuntimeLifecycle.BindNetwork();
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Awake))]
internal static class PrivateAreaAwakePatch
{
    private static void Postfix(PrivateArea __instance)
    {
        var ward = ManagedWardRef.FromArea(__instance);
        var zdo = ward.Zdo;
        var matchedByComponent = ward.HasManagedComponent;
        var matchedByZdo = ward.IsManagedZdo;
        if (ShouldSkipPlacementGhostAwake(__instance, matchedByComponent, matchedByZdo, zdo))
        {
            return;
        }

        if (!matchedByComponent && !matchedByZdo)
        {
            return;
        }

        ManagedWardIdentity.TryResolve(ward, repairComponent: true, out matchedByComponent, out matchedByZdo);
        ManagedWardInitializationCoordinator.EnsureLocalInitialization(__instance);
    }

    private static bool ShouldSkipPlacementGhostAwake(PrivateArea area, bool matchedByComponent, bool matchedByZdo, ZDO? zdo)
    {
        if (Player.IsPlacementGhost(area.gameObject))
        {
            return true;
        }

        // During Player.SetupPlacementGhost, Awake can run before Player.m_placementGhost is assigned.
        // Those ghost clones look like managed wards by component, but they never have a live ZDO.
        if (matchedByComponent &&
            !matchedByZdo &&
            zdo == null &&
            ZNetView.m_forceDisableInit)
        {
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.OnDestroy))]
internal static class PrivateAreaOnDestroyPatch
{
    private static void Prefix(PrivateArea __instance)
    {
        ManagedWardLifecycle.NotifyAreaDestroyed(__instance);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.UpdateStatus))]
internal static class PrivateAreaUpdateStatusPatch
{
    private static void Postfix(PrivateArea __instance)
    {
        var ward = ManagedWardRef.FromArea(__instance);
        if (ward.IsPlacementGhost || !WardAccess.IsManagedWard(ward, false))
        {
            WardAccess.UnregisterManagedWard(ward);
            WardRuntimeStateTracker.Forget(__instance);
            return;
        }

        if (!WardRuntimeStateTracker.TryConsumeChanges(__instance, out var enabledChanged, out var dataRevisionChanged))
        {
            return;
        }

        var suppressEnabledFanOut = false;
        var suppressDataRevisionFanOut = false;
        if (ManagedWardRuntimeContexts.TryGet(__instance, out var contextAfterUpdate))
        {
            suppressEnabledFanOut = enabledChanged &&
                                    ManagedWardRuntimeContexts.TryConsumeEnabledFanOutSuppression(
                                        __instance,
                                        contextAfterUpdate.ObservedState.Enabled);
            suppressDataRevisionFanOut = dataRevisionChanged &&
                                         ManagedWardRuntimeContexts.TryConsumeDataRevisionFanOutSuppression(
                                             __instance,
                                             contextAfterUpdate.ObservedState.DataRevision);
        }

        if (enabledChanged)
        {
            ManagedWardInteractionRpc.NotifyLocalEnabledStateObserved(__instance);
            WardAccess.RefreshManagedWardState(ward);
        }

        if (dataRevisionChanged)
        {
            ManagedWardInteractionRpc.NotifyLocalPermittedStateObserved(__instance);
            WardSettings.ApplyAreaState(ward);
        }

        var notifyEnabledChange = enabledChanged && !suppressEnabledFanOut;
        var notifyDataRevisionChange = dataRevisionChanged && !suppressDataRevisionFanOut;
        if (!notifyEnabledChange && !notifyDataRevisionChange)
        {
            return;
        }

        ManagedWardMapStateService.NotifyLiveWardMutation(__instance);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Setup))]
internal static class PrivateAreaSetupPatch
{
    private static void Postfix(PrivateArea __instance)
    {
        var ward = ManagedWardRef.FromArea(__instance);
        if (!ManagedWardIdentity.TryResolve(
                ward,
                repairComponent: true,
                out var matchedByComponent,
                out var matchedByZdo))
        {
            return;
        }

        ManagedWardInitializationCoordinator.EnsureNetworkInitialization(__instance, matchedByComponent, matchedByZdo);
    }
}

[HarmonyPatch(typeof(ZNetScene), "CreateObject", typeof(ZDO))]
internal static class ZNetSceneCreateObjectManagedWardPatch
{
    private static void Postfix(ZDO zdo, GameObject __result)
    {
        if (__result == null || zdo == null || !zdo.IsValid())
        {
            return;
        }

        var area = __result.GetComponent<PrivateArea>() ?? __result.GetComponentInChildren<PrivateArea>();
        if (area == null)
        {
            return;
        }

        if (!ManagedWardIdentity.TryResolve(
                area,
                zdo,
                repairComponent: true,
                out var matchedByComponent,
                out var matchedByZdo))
        {
            return;
        }

        ManagedWardInitializationCoordinator.EnsureLocalInitialization(area);
        ManagedWardInitializationCoordinator.EnsureNetworkInitialization(area, matchedByComponent, matchedByZdo);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.ShowAreaMarker))]
internal static class PrivateAreaShowAreaMarkerPatch
{
    private static bool Prefix(PrivateArea __instance)
    {
        if (!WardAccess.IsManagedWard(__instance, false))
        {
            return true;
        }

        if (!WardSettings.ShouldShowAreaMarker(__instance))
        {
            return true;
        }

        WardSettings.ShowManagedAreaMarker(__instance);
        return false;
    }
}

internal static class ManagedWardInteractionRpc
{
    private const string RpcRequestToggleEnabled = "STUWard_RequestToggleEnabled";
    private const string RpcRequestTogglePermitted = "STUWard_RequestTogglePermitted";

    private readonly struct PendingLocalEnabledToggleRequest
    {
        internal PendingLocalEnabledToggleRequest(bool expectedEnabled, float expiresAt)
        {
            ExpectedEnabled = expectedEnabled;
            ExpiresAt = expiresAt;
        }

        internal bool ExpectedEnabled { get; }
        internal float ExpiresAt { get; }
    }

    private readonly struct PendingLocalPermittedToggleRequest
    {
        internal PendingLocalPermittedToggleRequest(bool expectedPermitted, float expiresAt)
        {
            ExpectedPermitted = expectedPermitted;
            ExpiresAt = expiresAt;
        }

        internal bool ExpectedPermitted { get; }
        internal float ExpiresAt { get; }
    }

    // PrivateArea.Interact can be invoked repeatedly while the local ward state still
    // reflects the pre-toggle value, so keep exactly one enabled-toggle request in flight
    // per ward until the synchronized enabled state is observed or the request times out.
    private static readonly Dictionary<ZDOID, PendingLocalEnabledToggleRequest> PendingLocalEnabledToggleRequests = new();
    private static readonly Dictionary<ZDOID, PendingLocalPermittedToggleRequest> PendingLocalPermittedToggleRequests = new();
    private const float PendingLocalEnabledToggleTimeoutSeconds = 1.5f;
    private const float PendingLocalPermittedToggleTimeoutSeconds = 1.5f;

    internal static void RegisterRoutedRpcs(ZRoutedRpc routedRpc)
    {
        routedRpc.Register<ZPackage>(RpcRequestToggleEnabled, HandleRoutedToggleEnabled);
        routedRpc.Register<ZPackage>(RpcRequestTogglePermitted, HandleRoutedTogglePermitted);
    }

    internal static bool IsManagedWardForHooks(PrivateArea? area)
    {
        if (area == null)
        {
            return false;
        }

        return ManagedWardIdentity.EnsureManagedComponent(area);
    }

    internal static void ResetLocalInteractionState()
    {
        PendingLocalEnabledToggleRequests.Clear();
        PendingLocalPermittedToggleRequests.Clear();
    }

    internal static bool TryHandleInteract(PrivateArea area, Player player, bool hold, ref bool result)
    {
        result = false;

        if (hold)
        {
            return false;
        }

        if (area.m_ownerFaction != 0)
        {
            return false;
        }

        var nview = GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        if (Plugin.IsWardSettingsShortcutDown())
        {
            result = false;
            return false;
        }

        var playerId = player.GetPlayerID();
        var canControl = WardAccess.CanControlManagedWard(area, playerId) ||
                         WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(area, player);
        if (canControl)
        {
            result = RequestToggleEnabled(area, player);
            return false;
        }

        if (area.IsEnabled())
        {
            return false;
        }

        result = RequestTogglePermitted(area, player);
        return false;
    }

    internal static string? GetHoverActionLine(PrivateArea area, Player? player)
    {
        if (player == null || area.m_ownerFaction != 0)
        {
            return null;
        }

        if (WardAccess.CanControlManagedWard(area, player.GetPlayerID()) ||
            WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(area, player))
        {
            return LocalizeUseAction(
                area.IsEnabled() ? "$piece_guardstone_deactivate" : "$piece_guardstone_activate",
                area.IsEnabled() ? "Deactivate" : "Activate");
        }

        if (area.IsEnabled())
        {
            return null;
        }

        var isPermitted = WardPrivateAreaSafeAccess.IsPlayerPermitted(area, player.GetPlayerID());
        return LocalizeUseAction(
            isPermitted ? "$piece_guardstone_remove" : "$piece_guardstone_add",
            isPermitted ? "Remove" : "Add");
    }

    private static bool RequestToggleEnabled(PrivateArea area, Player player)
    {
        var nview = GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        if (IsLocalEnabledToggleRequestInFlight(zdo.m_uid, area.IsEnabled()))
        {
            return true;
        }

        var expectedEnabled = !area.IsEnabled();
        var request = new ZPackage();
        request.Write(zdo.m_uid);
        if (!WardOwnership.TryInvokeServerRoutedRpc(RpcRequestToggleEnabled, request))
        {
            return false;
        }

        PendingLocalEnabledToggleRequests[zdo.m_uid] = new PendingLocalEnabledToggleRequest(
            expectedEnabled,
            Time.time + PendingLocalEnabledToggleTimeoutSeconds);
        return true;
    }

    private static bool RequestTogglePermitted(PrivateArea area, Player player)
    {
        var nview = GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        var isPermitted = WardPrivateAreaSafeAccess.IsPlayerPermitted(area, player.GetPlayerID());
        if (IsLocalPermittedToggleRequestInFlight(zdo.m_uid, isPermitted))
        {
            return true;
        }

        var expectedPermitted = !isPermitted;
        var request = new ZPackage();
        request.Write(zdo.m_uid);
        if (!WardOwnership.TryInvokeServerRoutedRpc(RpcRequestTogglePermitted, request))
        {
            return false;
        }

        PendingLocalPermittedToggleRequests[zdo.m_uid] = new PendingLocalPermittedToggleRequest(
            expectedPermitted,
            Time.time + PendingLocalPermittedToggleTimeoutSeconds);
        return true;
    }

    private static void HandleRoutedToggleEnabled(long sender, ZPackage? request)
    {
        if (!TryResolveServerWardRequest(sender, request, out var zdo, out var requesterId))
        {
            return;
        }

        if (!WardAccess.CanControlManagedWard(zdo, requesterId))
        {
            return;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(zdo))
        {
            return;
        }

        var enabled = !zdo.GetBool(ZDOVars.s_enabled, false);
        zdo.Set(ZDOVars.s_enabled, enabled);
        WardOwnership.CompleteAuthoritativeManagedWardMutation(zdo);
        PlayAuthoritativeToggleEffects(zdo, enabled);
    }

    private static void PlayAuthoritativeToggleEffects(ZDO zdo, bool enabled)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        // Toggle effects have a ZNetView and must be instantiated only once by the server.
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return;
        }

        var instance = scene.FindInstance(zdo.m_uid);
        var area = instance != null
            ? instance.GetComponent<PrivateArea>() ?? instance.GetComponentInChildren<PrivateArea>()
            : null;
        var position = zdo.GetPosition();
        var rotation = zdo.GetRotation();
        if (area == null)
        {
            // Dedicated servers commonly keep remote wards as ZDOs without loaded scene instances.
            var prefab = scene.GetPrefab(zdo.GetPrefab());
            area = prefab != null
                ? prefab.GetComponent<PrivateArea>() ?? prefab.GetComponentInChildren<PrivateArea>()
                : null;
        }
        else
        {
            position = area.transform.position;
            rotation = area.transform.rotation;
        }

        if (area == null)
        {
            return;
        }

        var effectList = enabled ? area.m_activateEffect : area.m_deactivateEffect;
        if (effectList == null)
        {
            return;
        }

        _ = effectList.Create(position, rotation, null, 1f, -1);
    }

    private static void HandleRoutedTogglePermitted(long sender, ZPackage? request)
    {
        if (!TryReadRoutedWardRequest(request, out var wardZdoId))
        {
            return;
        }

        if (!TryResolveServerWardRequest(sender, wardZdoId, out var zdo, out var requesterId))
        {
            return;
        }

        if (WardAccess.CanControlManagedWard(zdo, requesterId) || zdo.GetBool(ZDOVars.s_enabled, false))
        {
            return;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(zdo))
        {
            return;
        }

        var requesterName = WardOwnership.GetPlayerName(requesterId);
        if (string.IsNullOrWhiteSpace(requesterName))
        {
            requesterName = requesterId.ToString();
        }

        if (!WardPrivateAreaSafeAccess.TogglePermittedPlayer(zdo, requesterId, requesterName))
        {
            return;
        }

        WardOwnership.CompleteAuthoritativePermittedMutation(zdo);
    }

    private static bool TryResolveServerWardRequest(
        long sender,
        ZPackage? request,
        out ZDO zdo,
        out long requesterId)
    {
        zdo = null!;
        requesterId = 0L;
        return TryReadRoutedWardRequest(request, out var wardZdoId) &&
               TryResolveServerWardRequest(sender, wardZdoId, out zdo, out requesterId);
    }

    private static bool TryResolveServerWardRequest(
        long sender,
        ZDOID wardZdoId,
        out ZDO zdo,
        out long requesterId)
    {
        zdo = null!;
        requesterId = 0L;
        return WardOwnership.TryResolveAuthoritativeManagedWardRequest(
            sender,
            wardZdoId,
            out zdo,
            out requesterId);
    }

    private static bool TryReadRoutedWardRequest(ZPackage? request, out ZDOID wardZdoId)
    {
        wardZdoId = ZDOID.None;
        if (request == null)
        {
            return false;
        }

        try
        {
            wardZdoId = request.ReadZDOID();
            return !wardZdoId.IsNone();
        }
        catch
        {
            wardZdoId = ZDOID.None;
            return false;
        }
    }

    internal static bool TryHandleVanillaToggleEnabled(PrivateArea area, long sender, long claimedPlayerId)
    {
        var nview = GetNView(area);
        if (!WardOwnership.CanHandleManagedWardStateRpc(nview))
        {
            return false;
        }

        if (!WardOwnership.TryResolveClaimedPlayerIdFromSender(sender, claimedPlayerId, out var requesterId))
        {
            return false;
        }

        return TryApplyResolvedToggleEnabled(area, requesterId);
    }

    internal static bool TryHandleVanillaTogglePermitted(PrivateArea area, long sender, long claimedPlayerId, string name)
    {
        var nview = GetNView(area);
        if (!WardOwnership.CanHandleManagedWardStateRpc(nview))
        {
            return false;
        }

        if (!WardOwnership.TryResolveClaimedPlayerIdFromSender(sender, claimedPlayerId, out var requesterId))
        {
            return false;
        }

        if (requesterId == 0L)
        {
            return false;
        }

        if (WardAccess.CanControlManagedWard(area, requesterId))
        {
            return false;
        }

        if (area.IsEnabled())
        {
            return false;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(area))
        {
            return false;
        }

        if (WardPrivateAreaSafeAccess.IsPlayerPermitted(area, requesterId))
        {
            var hadBaselineRevision = ManagedWardRuntimeContexts.TryGetCurrentDataRevision(area, out var baselineDataRevision);
            area.RemovePermitted(requesterId);
            if (hadBaselineRevision)
            {
                ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppressionIfChanged(area, baselineDataRevision);
            }

            return true;
        }

        var requesterName = !string.IsNullOrWhiteSpace(name) ? name : WardOwnership.GetPlayerName(requesterId);
        if (string.IsNullOrWhiteSpace(requesterName))
        {
            requesterName = requesterId.ToString();
        }

        ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppression(area);
        area.AddPermitted(requesterId, requesterName);
        return true;
    }

    internal static bool TryApplyResolvedToggleEnabled(PrivateArea area, long requesterId)
    {
        return ApplyToggleEnabled(area, requesterId);
    }

    private static bool ApplyToggleEnabled(PrivateArea area, long requesterId)
    {
        if (!WardAccess.CanControlManagedWard(area, requesterId))
        {
            return false;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(area))
        {
            return false;
        }

        var expectedEnabled = !area.IsEnabled();
        ManagedWardRuntimeContexts.ArmNextEnabledFanOutSuppression(area, expectedEnabled);
        ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppression(area);
        area.SetEnabled(expectedEnabled);
        return true;
    }

    private static ZNetView? GetNView(PrivateArea area)
    {
        return WardPrivateAreaSafeAccess.GetNView(area);
    }

    internal static void NotifyLocalEnabledStateObserved(PrivateArea area)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo == null || !zdo.IsValid())
        {
            return;
        }

        PendingLocalEnabledToggleRequests.Remove(zdo.m_uid);
    }

    internal static void NotifyLocalPermittedStateObserved(PrivateArea area)
    {
        var localPlayer = Player.m_localPlayer;
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (localPlayer == null || zdo == null || !zdo.IsValid())
        {
            return;
        }

        var currentPermitted = WardPrivateAreaSafeAccess.IsPlayerPermitted(area, localPlayer.GetPlayerID());
        if (!PendingLocalPermittedToggleRequests.TryGetValue(zdo.m_uid, out var pendingRequest))
        {
            return;
        }

        if (Time.time >= pendingRequest.ExpiresAt || currentPermitted == pendingRequest.ExpectedPermitted)
        {
            PendingLocalPermittedToggleRequests.Remove(zdo.m_uid);
        }
    }

    private static bool IsLocalEnabledToggleRequestInFlight(ZDOID wardZdoId, bool currentEnabled)
    {
        if (!PendingLocalEnabledToggleRequests.TryGetValue(wardZdoId, out var pendingRequest))
        {
            return false;
        }

        if (Time.time >= pendingRequest.ExpiresAt || currentEnabled == pendingRequest.ExpectedEnabled)
        {
            PendingLocalEnabledToggleRequests.Remove(wardZdoId);
            return false;
        }

        return true;
    }

    private static bool IsLocalPermittedToggleRequestInFlight(ZDOID wardZdoId, bool currentPermitted)
    {
        if (!PendingLocalPermittedToggleRequests.TryGetValue(wardZdoId, out var pendingRequest))
        {
            return false;
        }

        if (Time.time >= pendingRequest.ExpiresAt || currentPermitted == pendingRequest.ExpectedPermitted)
        {
            PendingLocalPermittedToggleRequests.Remove(wardZdoId);
            return false;
        }

        return true;
    }

    private static string LocalizeUseAction(string token, string fallback)
    {
        return Localization.instance != null
            ? Localization.instance.Localize($"[<color=yellow><b>$KEY_Use</b></color>] {token}")
            : $"[E] {fallback}";
    }
}

internal static class ManagedWardHoverTextCache
{
    internal static void Store(
        PrivateArea area,
        uint dataRevision,
        string originalText,
        long playerId,
        bool canConfigure,
        bool canAttemptAnyWardControl,
        bool hasSettingsShortcutBinding,
        string shortcutLabel,
        string guildName,
        string finalText)
    {
        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        context.HoverText = new ManagedWardHoverTextCacheEntry(
            dataRevision,
            originalText,
            playerId,
            canConfigure,
            canAttemptAnyWardControl,
            hasSettingsShortcutBinding,
            shortcutLabel,
            guildName,
            finalText);
        context.HasHoverText = true;
    }

    internal static void Forget(PrivateArea? area)
    {
        ManagedWardRuntimeContexts.ClearHoverText(area);
    }

    internal static void Reset()
    {
        ManagedWardRuntimeContexts.ClearHoverTexts();
    }

    internal static bool TryGet(
        PrivateArea area,
        uint dataRevision,
        string originalText,
        long playerId,
        bool canConfigure,
        bool canAttemptAnyWardControl,
        bool hasSettingsShortcutBinding,
        string shortcutLabel,
        string guildName,
        out string cachedHoverText)
    {
        cachedHoverText = string.Empty;
        if (!ManagedWardRuntimeContexts.TryGet(area, out var context) || !context.HasHoverText)
        {
            return false;
        }

        var cachedEntry = context.HoverText;
        if (cachedEntry.DataRevision != dataRevision ||
            !string.Equals(cachedEntry.OriginalText, originalText, StringComparison.Ordinal) ||
            cachedEntry.PlayerId != playerId ||
            cachedEntry.CanConfigure != canConfigure ||
            cachedEntry.CanAttemptAnyWardControl != canAttemptAnyWardControl ||
            cachedEntry.HasSettingsShortcutBinding != hasSettingsShortcutBinding ||
            !string.Equals(cachedEntry.ShortcutLabel, shortcutLabel, StringComparison.Ordinal) ||
            !string.Equals(cachedEntry.GuildName, guildName, StringComparison.Ordinal))
        {
            return false;
        }

        cachedHoverText = cachedEntry.FinalText;
        return true;
    }
}

internal static class ManagedWardHoverTextService
{
    private readonly struct HoverTextLine
    {
        internal HoverTextLine(int start, int length)
        {
            Start = start;
            Length = length;
            Text = null;
        }

        internal HoverTextLine(string text)
        {
            Start = 0;
            Length = 0;
            Text = text;
        }

        internal int Start { get; }
        internal int Length { get; }
        internal string? Text { get; }
        internal bool IsInserted => Text != null;
    }

    internal static bool TryRewriteHoverText(PrivateArea? area, string originalText, out string rewrittenText)
    {
        rewrittenText = originalText;
        if (area == null || !WardAccess.IsManagedWard(area, false) || string.IsNullOrEmpty(originalText))
        {
            return false;
        }

        var player = Player.m_localPlayer;
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        var dataRevision = zdo?.DataRevision ?? 0u;
        var playerId = player != null ? player.GetPlayerID() : 0L;
        var guildName = GuildsCompat.GetWardGuildName(area) ?? string.Empty;
        var guildLine = string.IsNullOrWhiteSpace(guildName)
            ? null
            : WardLocalization.LocalizeFormat(
                WardLocalization.UiGuildToken,
                WardLocalization.UiGuildFallback,
                guildName);

        var canConfigure = WardAccess.CanConfigureWard(area, player);
        var canAttemptAnyWardControl = WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(area, player);
        var actionLine = ManagedWardInteractionRpc.GetHoverActionLine(area, player);

        string? settingsLine = null;
        var hasSettingsShortcutBinding = Plugin.HasWardSettingsShortcutBinding();
        var shortcutLabel = hasSettingsShortcutBinding ? Plugin.GetWardSettingsShortcutLabel() : string.Empty;
        if (player != null &&
            hasSettingsShortcutBinding &&
            (canConfigure || canAttemptAnyWardControl))
        {
            settingsLine = WardLocalization.LocalizeFormat(
                WardLocalization.HoverSettingsToken,
                WardLocalization.HoverSettingsFallback,
                shortcutLabel);
        }

        if (guildLine == null && actionLine == null && settingsLine == null)
        {
            ManagedWardHoverTextCache.Forget(area);
            return false;
        }

        if (ManagedWardHoverTextCache.TryGet(
                area,
                dataRevision,
                originalText,
                playerId,
                canConfigure,
                canAttemptAnyWardControl,
                hasSettingsShortcutBinding,
                shortcutLabel,
                guildName,
                out var cachedHoverText))
        {
            rewrittenText = cachedHoverText;
            return true;
        }

        var lines = CollectHoverTextLines(originalText, out var actionLineIndex);
        if (lines.Count < 2)
        {
            return false;
        }

        if (guildLine != null)
        {
            lines.Insert(2, new HoverTextLine(guildLine));
        }

        if (actionLine != null)
        {
            if (actionLineIndex >= 0)
            {
                if (guildLine != null && actionLineIndex >= 2)
                {
                    actionLineIndex++;
                }

                lines[actionLineIndex] = new HoverTextLine(actionLine);
            }
            else
            {
                lines.Insert(Mathf.Max(2, lines.Count - 1), new HoverTextLine(actionLine));
            }
        }

        if (settingsLine != null && lines.Count >= 3)
        {
            lines.Insert(lines.Count - 1, new HoverTextLine(settingsLine));
        }

        rewrittenText = BuildHoverText(originalText, lines, guildLine, actionLine, settingsLine);
        ManagedWardHoverTextCache.Store(
            area,
            dataRevision,
            originalText,
            playerId,
            canConfigure,
            canAttemptAnyWardControl,
            hasSettingsShortcutBinding,
            shortcutLabel,
            guildName,
            rewrittenText);
        return true;
    }

    private static List<HoverTextLine> CollectHoverTextLines(string text, out int actionLineIndex)
    {
        var lines = new List<HoverTextLine>(4);
        actionLineIndex = -1;
        var lineStart = 0;

        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var lineLength = lineEnd - lineStart;
            if (actionLineIndex < 0 && lineLength > 0 && text[lineStart] == '[')
            {
                actionLineIndex = lines.Count;
            }

            lines.Add(new HoverTextLine(lineStart, lineLength));
            if (lineEnd >= text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return lines;
    }

    private static string BuildHoverText(
        string originalText,
        List<HoverTextLine> lines,
        string? guildLine,
        string? toggleLine,
        string? settingsLine)
    {
        var estimatedLength = originalText.Length +
                              (guildLine?.Length ?? 0) +
                              (toggleLine?.Length ?? 0) +
                              (settingsLine?.Length ?? 0) +
                              4;
        var builder = new StringBuilder(estimatedLength);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            var line = lines[index];
            if (line.IsInserted)
            {
                builder.Append(line.Text);
                continue;
            }

            builder.Append(originalText, line.Start, line.Length);
        }

        return builder.ToString();
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.GetHoverText))]
internal static class PrivateAreaGetHoverTextPatch
{
    private static void Postfix(PrivateArea __instance, ref string __result)
    {
        if (!ManagedWardHoverTextService.TryRewriteHoverText(__instance, __result, out var rewrittenText))
        {
            return;
        }

        __result = rewrittenText;
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Interact))]
internal static class PrivateAreaInteractAdminDebugPatch
{
    private static bool Prefix(PrivateArea __instance, Humanoid human, bool hold, bool alt, ref bool __result)
    {
        if (human is not Player player || player != Player.m_localPlayer)
        {
            return true;
        }

        if (!WardAccess.IsManagedWard(__instance, false))
        {
            return true;
        }

        return ManagedWardInteractionRpc.TryHandleInteract(__instance, player, hold, ref __result);
    }
}

[HarmonyPatch(typeof(PrivateArea), "RPC_ToggleEnabled")]
internal static class PrivateAreaRpcToggleEnabledAdminDebugPatch
{
    private static bool Prefix(PrivateArea __instance, long uid, long playerID)
    {
        if (!ManagedWardInteractionRpc.IsManagedWardForHooks(__instance))
        {
            return true;
        }

        _ = ManagedWardInteractionRpc.TryHandleVanillaToggleEnabled(__instance, uid, playerID);
        return false;
    }
}

[HarmonyPatch(typeof(PrivateArea), "RPC_TogglePermitted")]
internal static class PrivateAreaRpcTogglePermittedManagedPatch
{
    private static bool Prefix(PrivateArea __instance, long uid, long playerID, string name)
    {
        if (!ManagedWardInteractionRpc.IsManagedWardForHooks(__instance))
        {
            return true;
        }

        _ = ManagedWardInteractionRpc.TryHandleVanillaTogglePermitted(__instance, uid, playerID, name);
        return false;
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.HideMarker))]
internal static class PrivateAreaHideMarkerPatch
{
    private static bool Prefix(PrivateArea __instance)
    {
        if (!WardAccess.IsManagedWard(__instance, false))
        {
            return true;
        }

        return !WardSettings.ShouldShowAreaMarker(__instance);
    }
}

[HarmonyPatch(typeof(CircleProjector), nameof(CircleProjector.CreateSegments))]
internal static class CircleProjectorCreateSegmentsPatch
{
    private static void Prefix(CircleProjector __instance)
    {
        var area = __instance.GetComponentInParent<PrivateArea>();
        if (area == null || area.m_areaMarker != __instance || !WardAccess.IsManagedWard(area, false))
        {
            return;
        }

        __instance.m_nrOfSegments = WardSettings.ManagedAreaMarkerSegments;
    }

    private static void Postfix(CircleProjector __instance)
    {
        var area = __instance.GetComponentInParent<PrivateArea>();
        var ward = ManagedWardRef.FromArea(area);
        if (area == null || area.m_areaMarker != __instance || !WardAccess.IsManagedWard(ward, false))
        {
            return;
        }

        WardSettings.InvalidateAreaMarkerVisuals(area);
        WardSettings.ApplyAreaState(ward);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.RPC_FlashShield))]
internal static class PrivateAreaRpcFlashShieldVolumePatch
{
    private static bool Prefix(PrivateArea __instance)
    {
        return WardSettings.HandleManagedFlashEffect(__instance);
    }
}

[HarmonyPatch(typeof(Door), nameof(Door.RPC_UseDoor))]
internal static class DoorRpcUseDoorPatch
{
    private static void Postfix(Door __instance)
    {
        var nview = __instance.m_nview != null ? __instance.m_nview : __instance.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid())
        {
            return;
        }

        var state = nview.GetZDO()?.GetInt(ZDOVars.s_state, 0) ?? 0;
        if (state == 0)
        {
            WardGuiController.Instance?.CancelDoorAutoClose(__instance);
            return;
        }

        WardGuiController.Instance?.ScheduleDoorAutoClose(__instance);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.SetEnabled))]
internal static class PrivateAreaSetEnabledWardMinimapVisibilityPatch
{
    private static void Postfix(PrivateArea __instance)
    {
        if (!ManagedWardInteractionRpc.IsManagedWardForHooks(__instance))
        {
            return;
        }

        var ward = ManagedWardRef.FromArea(__instance);
        ManagedWardMapStateService.NotifyLiveWardMutation(__instance);
        WardOwnership.ForceSyncManagedWardZdoToServer(ward);
    }
}
