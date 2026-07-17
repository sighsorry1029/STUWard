using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace STUWard;

internal static partial class WardOwnership
{
    // Stored Steam/platform account id used only for ward count/report/grouping.
    // Direct owner/control semantics remain creator playerId-based.
    private const string ManagedWardMarkerKey = "stuw_is_managed_ward";
    private const string SteamAccountIdKey = "stuw_owner_account_id";
    private const string LimitRefundProcessedKey = "stuw_limit_refund_processed";
    private const string ReceiveWardPlacementRejectedRpc = "STUWard_ReceiveWardPlacementRejected";
    private const string NotifyManagedWardPlacedRpc = "STUWard_NotifyManagedWardPlaced";
    private const string NotifyManagedWardMapStateChangedRpc = "STUWard_NotifyManagedWardMapStateChanged";
    private const string ReportFileName = "STUWard.WardCountReport.yml";

    private static readonly Dictionary<string, int> WardLimitOverrides = new(StringComparer.Ordinal);
    private static readonly List<ZDO> ManagedWardPrefabScanBuffer = new();
    private static readonly int ManagedWardPrefabHash = StringExtensionMethods.GetStableHashCode(StuWardArea.PrefabName);
    private static ZDOMan? _trackedZdoMan;
    private static bool _managedWardObservationInitialized;
    private static bool _rpcsRegistered;

    internal static void Initialize()
    {
        ReloadOverrides(force: true);
    }

    private static bool ReloadOverrides(bool force)
    {
        if (ZNet.instance != null && !ZNet.instance.IsServer())
        {
            return false;
        }

        var snapshotOverrides = ManagedWardConfigFileService.CurrentSnapshot.WardLimitOverrides;
        var changed = force || WardLimitOverrides.Count != snapshotOverrides.Count;
        if (!changed)
        {
            foreach (var entry in snapshotOverrides)
            {
                if (!WardLimitOverrides.TryGetValue(entry.Key, out var currentValue) || currentValue != entry.Value)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        WardLimitOverrides.Clear();
        foreach (var entry in snapshotOverrides)
        {
            WardLimitOverrides[entry.Key] = entry.Value;
        }

        return true;
    }

    internal static void Update()
    {
        ProcessPendingManagedWardPlacementObserves();
        ProcessPendingManagedWardMapStateRefreshes();
    }

    internal static bool HasPendingRuntimeWork()
    {
        return PendingManagedWardPlacementObserves.Count > 0 ||
               PendingManagedWardMapStateRefreshes.Count > 0;
    }

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register<ZPackage>(ReceiveWardPlacementRejectedRpc, HandleReceiveWardPlacementRejected);
        routedRpc.Register<ZPackage>(NotifyManagedWardPlacedRpc, HandleNotifyManagedWardPlaced);
        routedRpc.Register<ZPackage>(NotifyManagedWardMapStateChangedRpc, HandleNotifyManagedWardMapStateChanged);
        ManagedWardReportService.RegisterRpcs();
        _rpcsRegistered = true;
        Plugin.LogWardDiagnosticVerbose(
            "Rpcs.Register",
            $"Registered managed ward routed RPCs. isServer={ZNet.instance != null && ZNet.instance.IsServer()}, " +
            $"placementReject='{ReceiveWardPlacementRejectedRpc}', placementNotify='{NotifyManagedWardPlacedRpc}', mapStateNotify='{NotifyManagedWardMapStateChangedRpc}'");
    }

    internal static void ResetRuntimeState()
    {
        Plugin.LogWardDiagnosticVerbose(
            "Rpcs.ZNetAwake",
            $"WardOwnership.OnZNetAwake invoked. isServer={ZNet.instance != null && ZNet.instance.IsServer()}, hadRegisteredRpcs={_rpcsRegistered}");
        _rpcsRegistered = false;
        ManagedWardReportService.OnZNetAwake();
        ResetServerRuntimeState();
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterRpcs();
    }

    internal static void OnZNetAwake()
    {
        ResetRuntimeState();
        EnsureRuntimeBindings();
    }

    internal static void ObserveManagedWard(PrivateArea? area)
    {
        ObserveManagedWard(ManagedWardRef.FromArea(area));
    }

    internal static void ObserveManagedWard(ManagedWardRef ward)
    {
        if (ward.Area == null || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var zdo = ward.Zdo;
        if (zdo == null)
        {
            return;
        }

        PromoteRuntimeManagedWardZdo(zdo, "runtime area observe");

        EnsureManagedWardObservationInitialized();
        ObserveManagedWard(zdo);
    }

    internal static bool TryStampLocalManagedWardOwnerAccount(PrivateArea? area)
    {
        return TryStampLocalManagedWardOwnerAccount(ManagedWardRef.FromArea(area));
    }

    internal static bool TryStampLocalManagedWardOwnerAccount(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return false;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        if (!WardAccess.IsDirectWardOwner(ward, localPlayer.GetPlayerID()))
        {
            return false;
        }

        var accountId = GetPlayerAccountId(localPlayer);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        if (!ward.HasValidNetworkIdentity || !ward.IsOwner)
        {
            return false;
        }

        var zdo = ward.Zdo;
        if (zdo == null)
        {
            return false;
        }

        var changed = false;
        if (!zdo.GetBool(ManagedWardMarkerKey, false))
        {
            zdo.Set(ManagedWardMarkerKey, true);
            changed = true;
        }

        var storedAccountId = NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty));
        if (!SameAccountId(storedAccountId, accountId))
        {
            zdo.Set(SteamAccountIdKey, accountId);
            changed = true;
        }

        if (GuildsCompat.TryStampLocalWardGuildMetadata(ward))
        {
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        ManagedWardMetadataMutationService.SynchronizeRegistryEntry(zdo);
        ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.LocalStamp",
            $"Stamped managed ward steamAccountId='{accountId}' locally before replication. {WardDiagnosticInfo.DescribeWard(area)}");
        return true;
    }

    internal static void RefreshServerPlayerAccountIdForPlayer(Player? player)
    {
        if (player == null || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var accountId = GetPlayerAccountId(player);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            RememberServerPlayerAccountId(player.GetPlayerID(), accountId);
        }
    }

    internal static void RefreshServerPlayerAccountIdForResolvedPlayer(long playerId, string accountId)
    {
        RememberServerPlayerAccountId(playerId, accountId);
    }

    private static void SendWardPlacementRejectedResponse(long receiverUid, int limit, bool showLimitMessage)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null)
        {
            return;
        }

        if (IsLocalReceiver(receiverUid))
        {
            ManagedWardMapStateService.RequestLocalDisplayRefresh(
                "local managed ward placement rejected",
                refreshImmediatelyIfVisible: true);
            if (showLimitMessage)
            {
                WardAccess.ShowWardLimitMessage(Player.m_localPlayer, limit);
            }

            return;
        }

        var pkg = new ZPackage();
        pkg.Write(showLimitMessage);
        pkg.Write(limit);
        routedRpc.InvokeRoutedRPC(receiverUid, ReceiveWardPlacementRejectedRpc, pkg);
    }

    private static void RejectManagedWardPlacement(ZDO? zdo, long receiverUid, int limit, bool showLimitMessage, string context, string reason, string wardDescription)
    {
        Plugin.LogWardDiagnosticFailure(
            context,
            $"Rejected managed ward placement because {reason}. {wardDescription}");
        SendWardPlacementRejectedResponse(receiverUid, limit, showLimitMessage);
        if (zdo != null && zdo.IsValid())
        {
            DropManagedWardPlacementRefundOnce(zdo);
            ManagedWardRegistry.RemoveEntry(zdo.m_uid);
            WardPrivateAreaSafeAccess.ForgetPermittedPlayerIds(zdo.m_uid);
            WardPermittedSnapshots.Forget(zdo.m_uid);
            ManagedWardMapStateService.NotifyWardRemoved(zdo.m_uid, "managed ward placement rejected");

            _ = TryClaimManagedWardMutationOwnership(zdo, context);
            var instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
            if (instance != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(instance);
            }
            else
            {
                ZDOMan.instance?.DestroyZDO(zdo);
            }
        }
    }

    private static void DropManagedWardPlacementRefundOnce(ZDO zdo)
    {
        if (zdo == null || !zdo.IsValid() || zdo.GetBool(LimitRefundProcessedKey, false))
        {
            return;
        }

        zdo.Set(LimitRefundProcessedKey, true);
        DropManagedWardPlacementRefund(zdo.GetPosition());
    }

    private static void DropManagedWardPlacementRefund(Vector3 position)
    {
        var requirements = StuWardPrefab.GetCurrentStuWardRequirements();
        if (requirements.Length == 0)
        {
            return;
        }

        var dropPosition = position + Vector3.up;
        for (var requirementIndex = 0; requirementIndex < requirements.Length; requirementIndex++)
        {
            var requirement = requirements[requirementIndex];
            var itemDrop = requirement.m_resItem;
            if (itemDrop == null || !requirement.m_recover)
            {
                continue;
            }

            var totalAmount = requirement.GetAmount(1);
            if (totalAmount <= 0)
            {
                continue;
            }

            var itemPrefab = itemDrop.gameObject;
            var maxStackSize = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
            var remainingAmount = totalAmount;
            while (remainingAmount > 0)
            {
                var stackAmount = Math.Min(remainingAmount, maxStackSize);
                remainingAmount -= stackAmount;

                var itemData = itemDrop.m_itemData.Clone();
                itemData.m_dropPrefab = itemPrefab;
                itemData.m_stack = stackAmount;
                ItemDrop.DropItem(
                    itemData,
                    stackAmount,
                    dropPosition,
                    Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            }
        }
    }

    private static bool IsLocalReceiver(long receiverUid)
    {
        var localPlayer = Player.m_localPlayer;
        return localPlayer != null && receiverUid != 0L && localPlayer.GetOwner() == receiverUid;
    }

    private static void HandleReceiveWardPlacementRejected(long sender, ZPackage pkg)
    {
        if (!IsAuthoritativeServerSender(sender) || pkg == null)
        {
            Plugin.LogWardDiagnosticFailure(
                "Placement.Rejected",
                $"Rejected managed ward placement response from a non-server sender. sender={sender}");
            return;
        }

        bool showLimitMessage;
        int limit;
        try
        {
            showLimitMessage = pkg.ReadBool();
            limit = pkg.ReadInt();
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure(
                "Placement.Rejected",
                $"Failed to deserialize managed ward placement response. sender={sender}");
            return;
        }

        ManagedWardMapStateService.RequestDisplayRefresh("managed ward placement rejected", liveDisplayRefresh: true);
        if (showLimitMessage)
        {
            WardAccess.ShowWardLimitMessage(Player.m_localPlayer, limit);
        }
    }

    private static int GetEffectiveWardLimitForAccount(string accountId)
    {
        ReloadOverrides(force: false);
        var overrideAccountId = NormalizeOverrideAccountId(accountId);
        return WardLimitPolicy.GetEffectiveLimit(
            overrideAccountId,
            WardLimitOverrides,
            Plugin.MaxWardsPerSteamId?.Value ?? 3);
    }

    private static void ResetServerRuntimeState()
    {
        ResetPlacementLifecycleState();
        ManagedWardRegistry.Reset();
        ResetIdentityAuthState();
    }

    private static void ResetPlacementLifecycleState()
    {
        if (_trackedZdoMan != null)
        {
            _trackedZdoMan.m_onZDODestroyed -= HandleTrackedWardDestroyed;
            _trackedZdoMan = null;
        }

        PendingManagedWardPlacementObserves.Clear();
        PendingManagedWardMapStateRefreshes.Clear();
        _managedWardObservationInitialized = false;
    }

    private static void EnsureManagedWardObservationInitialized()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var zdoMan = ZDOMan.instance;
        if (zdoMan == null)
        {
            return;
        }

        EnsureTrackedZdoManHooked(zdoMan);

        if (_managedWardObservationInitialized)
        {
            return;
        }

        RunInitialManagedWardObservationPass(zdoMan, "initialization");
    }

    private static void EnsureTrackedZdoManHooked(ZDOMan zdoMan)
    {
        if (ReferenceEquals(_trackedZdoMan, zdoMan))
        {
            return;
        }

        if (_trackedZdoMan != null)
        {
            _trackedZdoMan.m_onZDODestroyed -= HandleTrackedWardDestroyed;
        }

        zdoMan.m_onZDODestroyed += HandleTrackedWardDestroyed;
        _trackedZdoMan = zdoMan;
    }

    private static void RunInitialManagedWardObservationPass(ZDOMan zdoMan, string reason)
    {
        EnsureTrackedZdoManHooked(zdoMan);
        var previousManagedWardObservationInitialized = _managedWardObservationInitialized;
        _managedWardObservationInitialized = false;
        ManagedWardRegistry.Reset();
        var scannedZdoCount = PrepareManagedWardPrefabScan(zdoMan);

        for (var index = 0; index < scannedZdoCount; index++)
        {
            ObserveManagedWard(ManagedWardPrefabScanBuffer[index]);
        }
        _managedWardObservationInitialized = true;
        Plugin.LogWardDiagnosticVerbose(
            "Placement.ServerObserve",
            $"Completed initial managed ward observation pass. reason='{reason}', previousInitialized={previousManagedWardObservationInitialized}, scannedZdos={scannedZdoCount}, objectsByIdCount={zdoMan.m_objectsByID.Count}, managedWardPrefabHash={ManagedWardPrefabHash}");
    }

    internal static void OnAuthoritativeWorldZdosLoaded(ZDOMan zdoMan)
    {
        if (zdoMan == null || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        RunInitialManagedWardObservationPass(zdoMan, "world ZDO load completed");
    }

    private static int PrepareManagedWardPrefabScan(ZDOMan zdoMan)
    {
        ManagedWardPrefabScanBuffer.Clear();
        var scanIndex = 0;
        while (!zdoMan.GetAllZDOsWithPrefabIterative(StuWardArea.PrefabName, ManagedWardPrefabScanBuffer, ref scanIndex))
        {
        }

        return ManagedWardPrefabScanBuffer.Count;
    }

    private static void ObserveManagedWard(ZDO? zdo)
    {
        if (!IsManagedWardZdo(zdo))
        {
            return;
        }

        var managedZdo = zdo!;
        var playerId = managedZdo.GetLong(ZDOVars.s_creator, 0L);
        var authoritativeMetadataChanged = false;
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            authoritativeMetadataChanged = TryCanonicalizeWardSteamAccountIdFromCreator(managedZdo, playerId);
        }

        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            _managedWardObservationInitialized)
        {
            if (!TryFinalizeAuthoritativeManagedWardPlacement(managedZdo, out var finalizedMetadataChanged))
            {
                return;
            }

            authoritativeMetadataChanged |= finalizedMetadataChanged;
        }

        var accountId = ResolveWardSteamAccountId(managedZdo, playerId);
        Plugin.LogWardDiagnosticVerbose(
            "WardObserve",
            $"Observing managed ward. ownerPlayerId={playerId}, accountId='{accountId}', {DescribeManagedWardZdo(managedZdo)}");

        _ = ManagedWardMetadataMutationService.ObserveAuthoritativeWard(
            managedZdo,
            playerId,
            accountId,
            authoritativeMetadataChanged,
            "managed ward observed");
    }

    private static void ObserveAuthoritativeManagedWardPlacement(ZDO? zdo)
    {
        EnsureManagedWardObservationInitialized();
        ObserveManagedWard(zdo);
    }

    private static bool TryCanonicalizeWardSteamAccountIdFromCreator(ZDO zdo, long ownerPlayerId)
    {
        if (zdo == null || ownerPlayerId == 0L || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return false;
        }

        var creatorAccountId = GetPlayerAccountId(ownerPlayerId);
        if (string.IsNullOrWhiteSpace(creatorAccountId))
        {
            return false;
        }

        var storedAccountId = NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty));
        if (SameAccountId(storedAccountId, creatorAccountId))
        {
            return false;
        }

        zdo.Set(SteamAccountIdKey, creatorAccountId);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.Server",
            $"Canonicalized managed ward steamAccountId from creator mapping. ownerPlayerId={ownerPlayerId}, oldSteamAccountId='{storedAccountId}', newSteamAccountId='{creatorAccountId}', {DescribeManagedWardZdo(zdo)}");
        return true;
    }

    private static bool TryFinalizeAuthoritativeManagedWardPlacement(ZDO zdo, out bool metadataChanged)
    {
        metadataChanged = false;
        if (!IsManagedWardZdo(zdo))
        {
            return false;
        }

        var storedAccountId = NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty));
        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        if (ownerPlayerId == 0L)
        {
            return true;
        }

        var senderUid = TryGetServerSessionSenderUid(ownerPlayerId, out var resolvedSenderUid) ? resolvedSenderUid : 0L;
        var accountId = storedAccountId;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = GetPlayerAccountId(ownerPlayerId);
        }
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = GetAuthoritativeAccountIdFromSender(senderUid, ownerPlayerId);
        }
        Plugin.LogWardDiagnosticVerbose(
            "WardLimit.Server",
            $"Authoritative placement validation resolved owner identity. ownerPlayerId={ownerPlayerId}, senderUid={senderUid}, storedAccountId='{storedAccountId}', resolvedAccountId='{accountId}', {DescribeManagedWardZdo(zdo)}");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            RejectManagedWardPlacement(
                zdo,
                senderUid,
                0,
                showLimitMessage: false,
                "Placement.Server",
                $"the owner's authoritative steamAccountId could not be resolved. ownerPlayerId={ownerPlayerId}",
                DescribeManagedWardZdo(zdo));
            return false;
        }

        var limit = GetEffectiveWardLimitForAccount(accountId);
        if (!TryCountAuthoritativeManagedWardsForAccount(accountId, ownerPlayerId, zdo.m_uid, out var currentCount))
        {
            RejectManagedWardPlacement(
                zdo,
                senderUid,
                0,
                showLimitMessage: false,
                "Placement.Server",
                $"the authoritative ward count could not be read. ownerPlayerId={ownerPlayerId}, accountId='{accountId}'",
                DescribeManagedWardZdo(zdo));
            return false;
        }

        var limitEvaluation = WardLimitPolicy.EvaluatePlacement(limit, currentCount);

        Plugin.LogWardDiagnosticVerbose(
            "WardLimit.Server",
            $"Authoritative placement validation counted direct managed wards. ownerPlayerId={ownerPlayerId}, accountId='{accountId}', limit={limit}, currentCount={currentCount}, {DescribeManagedWardZdo(zdo)}");

        if (!limitEvaluation.Allowed)
        {
            RejectManagedWardPlacement(
                zdo,
                senderUid,
                limitEvaluation.Limit,
                showLimitMessage: true,
                "Placement.Server",
                $"the authoritative ward limit was reached. ownerPlayerId={ownerPlayerId}, accountId='{accountId}', limit={limitEvaluation.Limit}, currentCount={limitEvaluation.CurrentCount}",
                DescribeManagedWardZdo(zdo));
            return false;
        }

        if (!SameAccountId(storedAccountId, accountId))
        {
            zdo.Set(SteamAccountIdKey, accountId);
            metadataChanged = true;
        }

        Plugin.LogWardDiagnosticVerbose(
            "Placement.Server",
            $"Validated managed ward steamAccountId='{accountId}' using direct managed ward count. ownerPlayerId={ownerPlayerId}, limit={limit}, currentCountBeforePlacement={currentCount}, metadataChanged={metadataChanged}, {DescribeManagedWardZdo(zdo)}");
        return true;
    }

    private static bool TryCountAuthoritativeManagedWardsForAccount(
        string accountId,
        long ownerPlayerId,
        ZDOID ignoredZdoId,
        out int count)
    {
        count = 0;
        var canonicalAccountId = NormalizeAccountId(accountId);
        var zdoMan = ZDOMan.instance;
        if (string.IsNullOrWhiteSpace(canonicalAccountId) ||
            zdoMan == null ||
            ZNet.instance == null ||
            !ZNet.instance.IsServer())
        {
            return false;
        }

        var scannedZdoCount = PrepareManagedWardPrefabScan(zdoMan);
        for (var index = 0; index < scannedZdoCount; index++)
        {
            var candidate = ManagedWardPrefabScanBuffer[index];
            if (!IsManagedWardZdo(candidate) || candidate!.m_uid == ignoredZdoId)
            {
                continue;
            }

            var candidateOwnerPlayerId = candidate.GetLong(ZDOVars.s_creator, 0L);
            var candidateAccountId = ResolveWardSteamAccountId(candidate, candidateOwnerPlayerId);
            if (SameAccountId(candidateAccountId, canonicalAccountId) ||
                (string.IsNullOrWhiteSpace(candidateAccountId) && candidateOwnerPlayerId == ownerPlayerId))
            {
                count++;
            }
        }

        return true;
    }

    private static string DescribeManagedWardZdo(ZDO? zdo)
    {
        if (zdo == null)
        {
            return "wardZdo=null";
        }

        var steamAccountId = NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty));
        return $"wardZdo={zdo.m_uid}, zdoValid={zdo.IsValid()}, prefab={zdo.GetPrefab()}, prefabMatchesManaged={zdo.GetPrefab() == ManagedWardPrefabHash}, managedMarker={zdo.GetBool(ManagedWardMarkerKey, false)}, zdoCreator={zdo.GetLong(ZDOVars.s_creator, 0L)}, steamAccountId='{steamAccountId}'";
    }

    private static void HandleTrackedWardDestroyed(ZDO zdo)
    {
        if (zdo == null)
        {
            return;
        }

        ManagedWardRegistry.RemoveEntry(zdo.m_uid);
        WardPrivateAreaSafeAccess.ForgetPermittedPlayerIds(zdo.m_uid);
        WardPermittedSnapshots.Forget(zdo.m_uid);
        ManagedWardMapStateService.NotifyWardRemoved(zdo.m_uid, "managed ward destroyed");
    }

    internal static bool IsManagedWardZdo(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        if (zdo.GetPrefab() == ManagedWardPrefabHash)
        {
            return true;
        }

        if (zdo.GetBool(ManagedWardMarkerKey, false))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty))))
        {
            return true;
        }

        return false;
    }

    private static bool PromoteRuntimeManagedWardZdo(ZDO zdo, string reason)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        var changed = false;
        if (!zdo.GetBool(ManagedWardMarkerKey, false))
        {
            zdo.Set(ManagedWardMarkerKey, true);
            changed = true;
        }

        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        var ownerAccountId = GetPlayerAccountId(ownerPlayerId);
        var storedAccountId = NormalizeAccountId(zdo.GetString(SteamAccountIdKey, string.Empty));
        if (!string.IsNullOrWhiteSpace(ownerAccountId) && !SameAccountId(storedAccountId, ownerAccountId))
        {
            zdo.Set(SteamAccountIdKey, ownerAccountId);
            changed = true;
        }

        if (zdo.GetPrefab() != ManagedWardPrefabHash)
        {
            Plugin.LogWardDiagnosticVerbose(
                "Placement.ServerObserve",
                $"Observed runtime managed ward with unexpected prefab hash during {reason}. expected={ManagedWardPrefabHash}, actual={zdo.GetPrefab()}, {DescribeManagedWardZdo(zdo)}");
        }

        if (!changed)
        {
            return false;
        }

        ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.ServerObserve",
            $"Promoted runtime managed ward ZDO during {reason}. {DescribeManagedWardZdo(zdo)}");
        return true;
    }

}

[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Load))]
internal static class ZdoManLoadWardOwnershipPatch
{
    private static void Postfix(ZDOMan __instance)
    {
        WardOwnership.OnAuthoritativeWorldZdosLoaded(__instance);
    }
}

[HarmonyPatch(typeof(ZNet), "Awake")]
internal static class ZNetAwakeWardOwnershipPatch
{
    private static void Postfix()
    {
        ManagedWardLifecycle.NotifySessionReset();
    }
}

[HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
internal static class ZNetRpcPeerInfoWardOwnershipPatch
{
    private static void Postfix(ZNet __instance, ZRpc rpc)
    {
        if (!__instance.IsServer())
        {
            return;
        }

        WardOwnership.RefreshServerSessionIdentity(__instance.GetPeer(rpc));
    }
}

[HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_CharacterID))]
internal static class ZNetRpcCharacterIdWardOwnershipPatch
{
    private static void Postfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
    {
        if (!__instance.IsServer())
        {
            return;
        }

        WardOwnership.RefreshServerSessionIdentity(__instance.GetPeer(rpc), characterID);
    }
}

[HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
internal static class ZNetDisconnectWardOwnershipPatch
{
    private static void Prefix(ZNet __instance, ZNetPeer peer)
    {
        if (!__instance.IsServer())
        {
            return;
        }

        WardOwnership.ForgetServerSessionIdentity(peer);
    }
}

[HarmonyPatch(typeof(Player), "Start")]
internal static class PlayerStartWardOwnershipPatch
{
    private static void Postfix(Player __instance)
    {
        WardGuiController.Instance?.CloseWardUi();
        WardOwnership.RefreshServerPlayerAccountIdForPlayer(__instance);
        WardAdminDebugAccess.UpdateLocalState(__instance, force: true);
        WardMinimapPinsManager.UpdateLocalState(__instance, force: true);
        GuildsCompat.OnLocalPlayerStarted(__instance);
    }
}

[HarmonyPatch(typeof(Player), "OnDeath")]
internal static class PlayerOnDeathWardUiPatch
{
    private static void Prefix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
        {
            return;
        }

        WardGuiController.Instance?.CloseWardUi();
    }
}

[HarmonyPatch(typeof(Player), "OnRespawn")]
internal static class PlayerOnRespawnWardUiPatch
{
    private static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
        {
            return;
        }

        WardGuiController.Instance?.CloseWardUi();
    }
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
internal static class TerminalTryRunCommandWardReportPatch
{
    private static bool Prefix(Terminal __instance, string text)
    {
        return !ManagedWardReportService.TryHandleConsoleCommand(__instance, text);
    }
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.Awake))]
internal static class TerminalAwakeWardReportCommandPatch
{
    private static void Postfix(Terminal __instance)
    {
        ManagedWardReportService.EnsureConsoleCommandRegistered(__instance);
    }
}
