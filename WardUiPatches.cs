using System.Collections;
using HarmonyLib;
using LocalizationManager;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace STUWard;

internal static class ManagedWardInitializationCoordinator
{
    internal static void EnsureLocalInitialization(PrivateArea area)
    {
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(area))
        {
            return;
        }

        WardSettings.CaptureNativeAreaMarkerSpeed(area);
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

        ManagedWardInitializationCoordinator.EnsureLocalInitialization(area);

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        if (!context.NetworkInitializationComplete)
        {
            WardOwnership.TryStampLocalManagedWardOwnerAccount(ward);
            WardAccess.RegisterManagedWard(ward);
            WardPermittedSnapshots.ReconcileExisting(ward);
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
            WardSettings.ApplyAreaState(ward);
        }

        var notifyEnabledChange = enabledChanged && !suppressEnabledFanOut;
        var notifyDataRevisionChange = dataRevisionChanged && !suppressDataRevisionFanOut;
        if (!notifyEnabledChange && !notifyDataRevisionChange)
        {
            return;
        }

        ManagedWardMapStateService.NotifyWardMutation(__instance);
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

        ManagedWardLifecycle.NotifyAreaReady(__instance, matchedByComponent, matchedByZdo);
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
        ManagedWardLifecycle.NotifyAreaReady(area, matchedByComponent, matchedByZdo);
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
            return false;
        }

        WardSettings.ShowManagedAreaMarker(__instance);
        return false;
    }
}

internal static class ManagedWardInteractionRpc
{
    private const string RpcRequestToggleEnabled = "STUWard_RequestToggleEnabled";
    private const string RpcPlayToggleEffect = "STUWard_PlayToggleEffect";

    private readonly struct PendingLocalBoolToggleRequest
    {
        internal PendingLocalBoolToggleRequest(bool expectedValue, float expiresAt)
        {
            ExpectedValue = expectedValue;
            ExpiresAt = expiresAt;
        }

        internal bool ExpectedValue { get; }
        internal float ExpiresAt { get; }
    }

    // PrivateArea.Interact can be invoked repeatedly while the local ward state still
    // reflects the pre-toggle value, so keep exactly one enabled-toggle request in flight
    // per ward until the synchronized enabled state is observed or the request times out.
    private static readonly Dictionary<ZDOID, PendingLocalBoolToggleRequest> PendingLocalEnabledToggleRequests = new();
    private const float PendingLocalToggleTimeoutSeconds = 1.5f;

    internal static void RegisterRoutedRpcs(ZRoutedRpc routedRpc)
    {
        routedRpc.Register<ZPackage>(RpcRequestToggleEnabled, HandleRoutedToggleEnabled);
        routedRpc.Register<ZPackage>(RpcPlayToggleEffect, HandleRoutedPlayToggleEffect);
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
        var isTrusted = WardAccess.HasManagedWardTrust(area, playerId) ||
                        WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(area, player);
        if (isTrusted)
        {
            result = RequestToggleEnabled(area);
        }

        return false;
    }

    internal static string? GetHoverActionLine(PrivateArea area, Player? player)
    {
        if (player == null || area.m_ownerFaction != 0)
        {
            return null;
        }

        if (WardAccess.HasManagedWardTrust(area, player.GetPlayerID()) ||
            WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(area, player))
        {
            return LocalizeUseAction(
                area.IsEnabled() ? "$piece_guardstone_deactivate" : "$piece_guardstone_activate",
                area.IsEnabled() ? "Deactivate" : "Activate");
        }

        return null;
    }

    private static bool RequestToggleEnabled(PrivateArea area)
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

        if (IsLocalToggleRequestInFlight(PendingLocalEnabledToggleRequests, zdo.m_uid, area.IsEnabled()))
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

        PendingLocalEnabledToggleRequests[zdo.m_uid] = new PendingLocalBoolToggleRequest(
            expectedEnabled,
            Time.time + PendingLocalToggleTimeoutSeconds);
        return true;
    }

    private static void HandleRoutedToggleEnabled(long sender, ZPackage? request)
    {
        if (!TryResolveServerWardRequest(sender, request, out var zdo, out var requesterId))
        {
            return;
        }

        if (!WardAccess.HasManagedWardTrust(zdo, requesterId))
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
        SendToggleEffect(sender, zdo.m_uid, enabled);
    }

    private static void SendToggleEffect(long receiverUid, ZDOID wardZdoId, bool enabled)
    {
        if (receiverUid == 0L || wardZdoId.IsNone())
        {
            return;
        }

        var response = new ZPackage();
        response.Write(wardZdoId);
        response.Write(enabled);
        ZRoutedRpc.instance?.InvokeRoutedRPC(receiverUid, RpcPlayToggleEffect, response);
    }

    private static void HandleRoutedPlayToggleEffect(long sender, ZPackage? response)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) ||
            !TryReadToggleEffect(response, out var wardZdoId, out var enabled))
        {
            return;
        }

        var instance = ZNetScene.instance?.FindInstance(wardZdoId);
        var area = instance != null
            ? instance.GetComponent<PrivateArea>() ?? instance.GetComponentInChildren<PrivateArea>()
            : null;
        if (area == null || !WardAccess.IsManagedWard(area, false))
        {
            return;
        }

        var effectList = enabled ? area.m_activateEffect : area.m_deactivateEffect;
        if (effectList == null)
        {
            return;
        }

        var transform = area.transform;
        _ = effectList.Create(transform.position, transform.rotation, null, 1f, -1);
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

    private static bool TryReadToggleEffect(ZPackage? response, out ZDOID wardZdoId, out bool enabled)
    {
        wardZdoId = ZDOID.None;
        enabled = false;
        if (response == null)
        {
            return false;
        }

        try
        {
            wardZdoId = response.ReadZDOID();
            enabled = response.ReadBool();
            return !wardZdoId.IsNone();
        }
        catch
        {
            wardZdoId = ZDOID.None;
            enabled = false;
            return false;
        }
    }

    internal static bool TryHandleVanillaToggleEnabled(PrivateArea area, long sender, long claimedPlayerId)
    {
        var nview = GetNView(area);
        if (!WardOwnership.CanApplyManagedWardStateLocally(nview))
        {
            return false;
        }

        if (!WardOwnership.TryResolveClaimedPlayerIdFromSender(sender, claimedPlayerId, out var requesterId))
        {
            return false;
        }

        return ApplyToggleEnabled(area, requesterId);
    }

    private static bool ApplyToggleEnabled(PrivateArea area, long requesterId)
    {
        if (!WardAccess.HasManagedWardTrust(area, requesterId))
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

    private static bool IsLocalToggleRequestInFlight(
        Dictionary<ZDOID, PendingLocalBoolToggleRequest> pendingRequests,
        ZDOID wardZdoId,
        bool currentValue)
    {
        if (!pendingRequests.TryGetValue(wardZdoId, out var pendingRequest))
        {
            return false;
        }

        if (Time.time >= pendingRequest.ExpiresAt || currentValue == pendingRequest.ExpectedValue)
        {
            pendingRequests.Remove(wardZdoId);
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

        var hasVanillaActionLine = originalText[0] == '[' ||
                                   originalText.IndexOf("\n[", System.StringComparison.Ordinal) >= 0;
        if (guildLine == null &&
            actionLine == null &&
            settingsLine == null &&
            !hasVanillaActionLine)
        {
            ManagedWardRuntimeContexts.ClearHoverText(area);
            return false;
        }

        var cacheKey = new ManagedWardHoverTextCacheEntry(
            dataRevision,
            originalText,
            playerId,
            canConfigure,
            canAttemptAnyWardControl,
            hasSettingsShortcutBinding,
            shortcutLabel,
            guildName,
            string.Empty);
        if (ManagedWardRuntimeContexts.TryGet(area, out var cachedContext) &&
            cachedContext.HasHoverText &&
            cachedContext.HoverText.Matches(cacheKey))
        {
            rewrittenText = cachedContext.HoverText.FinalText;
            return true;
        }

        var lines = CollectHoverTextLines(originalText, out var actionLineIndex);
        if (lines.Count < 2)
        {
            return false;
        }

        // Vanilla adds an opt-in/opt-out action for every non-owner looking at an
        // inactive ward. Managed wards no longer support self-registration, so
        // remove the vanilla action before adding the trusted-only control action.
        if (actionLineIndex >= 0)
        {
            lines.RemoveAt(actionLineIndex);
        }

        if (guildLine != null)
        {
            lines.Insert(Mathf.Min(2, lines.Count), new HoverTextLine(guildLine));
        }

        if (actionLine != null)
        {
            var actionInsertIndex = lines.Count > 1 ? lines.Count - 1 : lines.Count;
            lines.Insert(actionInsertIndex, new HoverTextLine(actionLine));
        }

        if (settingsLine != null && lines.Count >= 3)
        {
            lines.Insert(lines.Count - 1, new HoverTextLine(settingsLine));
        }

        rewrittenText = BuildHoverText(originalText, lines, guildLine, actionLine, settingsLine);
        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        context.HoverText = cacheKey.WithFinalText(rewrittenText);
        context.HasHoverText = true;
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
    private static bool Prefix(PrivateArea __instance)
    {
        if (!ManagedWardInteractionRpc.IsManagedWardForHooks(__instance))
        {
            return true;
        }

        // Managed wards never use vanilla's disabled-ward self-registration path.
        // Membership changes are handled only by STUWard's server-authorized list operations.
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

        ManagedWardInitializationCoordinator.EnsureLocalInitialization(area);
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
    private const float AutoCloseDelaySeconds = 5f;
    private const float AutoCloseRetryIntervalSeconds = 0.2f;
    private const float AutoCloseRetryTimeoutSeconds = 60f;

    private sealed class ScheduledDoorClose
    {
        internal ScheduledDoorClose(int generation, Coroutine coroutine)
        {
            Generation = generation;
            Coroutine = coroutine;
        }

        internal int Generation { get; }
        internal Coroutine Coroutine { get; }
    }

    private static readonly Dictionary<int, ScheduledDoorClose> DoorCloseCoroutines = new();
    private static int _nextScheduleGeneration;

    private static void Prefix(Door __instance, out int __state)
    {
        __state = GetDoorState(__instance);
    }

    private static void Postfix(Door __instance, int __state)
    {
        var currentState = GetDoorState(__instance);
        if (__state != 0 && currentState == 0)
        {
            CancelDoorAutoClose(__instance);
            return;
        }

        if (__state != 0 || currentState == 0 || currentState == __state)
        {
            return;
        }

        if (!CanAutoClose(__instance) ||
            !WardSettings.IsDoorAutoCloseEnabledAt(__instance.transform.position))
        {
            CancelDoorAutoClose(__instance);
            return;
        }

        ScheduleDoorAutoClose(__instance);
    }

    internal static void Reset()
    {
        var plugin = Plugin.Instance;
        if (plugin != null)
        {
            foreach (var scheduledClose in DoorCloseCoroutines.Values)
            {
                plugin.StopCoroutine(scheduledClose.Coroutine);
            }
        }

        DoorCloseCoroutines.Clear();
        _nextScheduleGeneration = 0;
    }

    private static void ScheduleDoorAutoClose(Door? door)
    {
        if (door == null || !CanAutoClose(door))
        {
            return;
        }

        var nview = GetValidNView(door);
        var plugin = Plugin.Instance;
        if (nview == null || !nview.IsOwner() || plugin == null)
        {
            return;
        }

        var key = door.GetInstanceID();
        if (DoorCloseCoroutines.TryGetValue(key, out var existing))
        {
            plugin.StopCoroutine(existing.Coroutine);
        }

        var generation = AllocateScheduleGeneration();
        var coroutine = plugin.StartCoroutine(CloseDoorAfterDelay(door, key, generation));
        DoorCloseCoroutines[key] = new ScheduledDoorClose(generation, coroutine);
    }

    private static void CancelDoorAutoClose(Door? door)
    {
        if (door == null)
        {
            return;
        }

        var key = door.GetInstanceID();
        if (!DoorCloseCoroutines.TryGetValue(key, out var scheduledClose))
        {
            return;
        }

        DoorCloseCoroutines.Remove(key);
        var plugin = Plugin.Instance;
        if (plugin != null)
        {
            plugin.StopCoroutine(scheduledClose.Coroutine);
        }
    }

    private static IEnumerator CloseDoorAfterDelay(Door door, int key, int generation)
    {
        yield return new WaitForSeconds(AutoCloseDelaySeconds);

        var retryElapsed = 0f;
        while (IsCurrentSchedule(key, generation))
        {
            if (!CanAutoClose(door))
            {
                break;
            }

            var nview = GetValidNView(door);
            if (nview == null ||
                !nview.IsOwner() ||
                !WardSettings.IsDoorAutoCloseEnabledAt(door.transform.position))
            {
                break;
            }

            var state = nview.GetZDO()?.GetInt(ZDOVars.s_state, 0) ?? 0;
            if (state == 0)
            {
                break;
            }

            if (door.CanInteract())
            {
                RemoveCurrentSchedule(key, generation);
                nview.InvokeRPC("UseDoor", new object[] { true });
                yield break;
            }

            if (retryElapsed >= AutoCloseRetryTimeoutSeconds)
            {
                break;
            }

            yield return new WaitForSeconds(AutoCloseRetryIntervalSeconds);
            retryElapsed += AutoCloseRetryIntervalSeconds;
        }

        RemoveCurrentSchedule(key, generation);
    }

    private static bool IsCurrentSchedule(int key, int generation)
    {
        return DoorCloseCoroutines.TryGetValue(key, out var scheduledClose) &&
               scheduledClose.Generation == generation;
    }

    private static void RemoveCurrentSchedule(int key, int generation)
    {
        if (IsCurrentSchedule(key, generation))
        {
            DoorCloseCoroutines.Remove(key);
        }
    }

    private static ZNetView? GetValidNView(Door? door)
    {
        if (door == null)
        {
            return null;
        }

        var nview = door.m_nview != null ? door.m_nview : door.GetComponent<ZNetView>();
        return nview != null && nview.IsValid() ? nview : null;
    }

    private static int GetDoorState(Door? door)
    {
        return GetValidNView(door)?.GetZDO()?.GetInt(ZDOVars.s_state, 0) ?? 0;
    }

    private static bool CanAutoClose(Door? door)
    {
        return door != null && !door.m_canNotBeClosed && door.m_keyItem == null;
    }

    private static int AllocateScheduleGeneration()
    {
        if (_nextScheduleGeneration == int.MaxValue)
        {
            _nextScheduleGeneration = 0;
        }

        return ++_nextScheduleGeneration;
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
        ManagedWardMapStateService.NotifyWardMutation(__instance);
        WardOwnership.ForceSyncManagedWardZdoToServer(ward);
    }
}
