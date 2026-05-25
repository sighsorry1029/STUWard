using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal static class ManagedWardPlacementPreviewService
{
    private sealed class PlacementPreviewOverlapState
    {
        internal int CandidateInstanceId;
        internal Vector3 Point;
        internal long PlayerId;
        internal int GuildId;
        internal int SpatialRevision = -1;
        internal bool BlocksPlacement;
        internal bool HasValue;
    }

    private static readonly PlacementPreviewOverlapState OverlapCache = new();

    internal static bool ShouldShowAsInvalid(Player? player, Component? candidate, Vector3 point)
    {
        if (!WardAccess.IsManagedWardPlacementCandidate(candidate) || candidate == null)
        {
            return false;
        }

        var playerId = player != null ? player.GetPlayerID() : 0L;
        var guildId = player != null ? GuildsCompat.GetPlayerGuildId(player) : 0;
        var candidateInstanceId = candidate.GetInstanceID();
        var spatialRevision = WardAccess.GetManagedWardSpatialIndexRevision();
        if (OverlapCache.HasValue &&
            OverlapCache.CandidateInstanceId == candidateInstanceId &&
            OverlapCache.PlayerId == playerId &&
            OverlapCache.GuildId == guildId &&
            OverlapCache.SpatialRevision == spatialRevision &&
            PointsMatch(OverlapCache.Point, point))
        {
            return OverlapCache.BlocksPlacement;
        }

        var blocksPlacement = WardAccess.WouldBlockManagedWardPlacement(player, candidate, point, flash: false);
        OverlapCache.CandidateInstanceId = candidateInstanceId;
        OverlapCache.Point = point;
        OverlapCache.PlayerId = playerId;
        OverlapCache.GuildId = guildId;
        OverlapCache.SpatialRevision = spatialRevision;
        OverlapCache.BlocksPlacement = blocksPlacement;
        OverlapCache.HasValue = true;
        return blocksPlacement;
    }

    internal static void Invalidate()
    {
        OverlapCache.HasValue = false;
    }

    private static bool PointsMatch(Vector3 left, Vector3 right)
    {
        return Mathf.Approximately(left.x, right.x) &&
               Mathf.Approximately(left.z, right.z);
    }
}

internal static class WardAccess
{
    private const string NoAccessMessageKey = "$piece_noaccess";
    private static readonly ManagedWardIndex AllWardIndex = new(area => IsTrackableManagedWard(area, requireEnabled: false));
    private static readonly ManagedWardIndex EnabledWardIndex = new(area => IsTrackableManagedWard(area, requireEnabled: true));
    private static readonly List<PrivateArea> SpatialQueryBuffer = new();
    private static bool _wardCacheInitialized;
    private static bool _managedWardSpatialIndexRequiresFullRebuild = true;
    private static float _managedWardSpatialIndexMaxRadius = -1f;
    private static int _managedWardSpatialIndexRevision;

    internal enum AccessDecision
    {
        NoWard,
        Allowed,
        Denied
    }

    internal readonly struct AccessResult
    {
        internal AccessResult(AccessDecision decision)
        {
            Decision = decision;
        }

        internal AccessDecision Decision { get; }
        internal bool IsDenied => Decision == AccessDecision.Denied;
        internal bool IsCoveredAndAllowed => Decision == AccessDecision.Allowed;
    }

    internal static void RegisterManagedWard(PrivateArea? area)
    {
        RegisterManagedWard(ManagedWardRef.FromArea(area));
    }

    internal static void RegisterManagedWard(ManagedWardRef ward)
    {
        RefreshManagedWardState(ward);
    }

    internal static void RefreshManagedWardState(PrivateArea? area)
    {
        RefreshManagedWardState(ManagedWardRef.FromArea(area));
    }

    internal static void RefreshManagedWardState(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        EnsureManagedWardCacheInitialized();
        if (!IsTrackableManagedWard(ward, requireEnabled: false))
        {
            UnregisterManagedWard(ward);
            return;
        }

        var instanceId = area.GetInstanceID();
        var managedMembershipChanged = false;
        var enabledMembershipChanged = false;
        if (AllWardIndex.Add(area))
        {
            managedMembershipChanged = true;
        }

        if (area.IsEnabled())
        {
            if (EnabledWardIndex.Add(area))
            {
                enabledMembershipChanged = true;
            }
        }
        else if (EnabledWardIndex.Remove(area))
        {
            ManagedWardRuntimeContexts.ResetPresenceState(area);
            enabledMembershipChanged = true;
        }

        if (managedMembershipChanged || enabledMembershipChanged)
        {
            UpdateManagedWardSpatialIndexMembership(area, instanceId, managedMembershipChanged, enabledMembershipChanged);
        }

        if (enabledMembershipChanged)
        {
            ManagedWardRuntimeInvalidationService.PublishWardEnabledChanged(ward, "managed ward enabled membership changed");
        }
    }

    internal static void UnregisterManagedWard(PrivateArea? area)
    {
        UnregisterManagedWard(ManagedWardRef.FromArea(area));
    }

    internal static void UnregisterManagedWard(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        var instanceId = area.GetInstanceID();
        var managedMembershipChanged = false;
        var enabledMembershipChanged = false;
        if (AllWardIndex.Remove(area))
        {
            managedMembershipChanged = true;
        }

        if (EnabledWardIndex.Remove(area))
        {
            enabledMembershipChanged = true;
        }

        ManagedWardRuntimeContexts.ResetPresenceState(area);
        if (managedMembershipChanged || enabledMembershipChanged)
        {
            UpdateManagedWardSpatialIndexMembership(area, instanceId, managedMembershipChanged, enabledMembershipChanged);
        }

        if (enabledMembershipChanged)
        {
            ManagedWardRuntimeInvalidationService.PublishWardEnabledChanged(ward, "managed ward unregistered");
        }
    }

    internal static bool HasEnabledManagedWards()
    {
        EnsureManagedWardCacheInitialized();
        return EnabledWardIndex.Count > 0;
    }

    internal static void InvalidateWardPresenceCache()
    {
        ManagedWardRuntimeInvalidationService.PublishPresencePolicyChanged("ward presence cache invalidated");
    }

    internal static void InvalidateManagedWardSpatialIndex()
    {
        if (_managedWardSpatialIndexRequiresFullRebuild)
        {
            ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward spatial index rebuild already pending");
            return;
        }

        _managedWardSpatialIndexRequiresFullRebuild = true;
        BumpManagedWardSpatialRevision();
        ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward spatial index invalidated");
    }

    internal static void RefreshManagedWardSpatialIndexEntry(PrivateArea? area)
    {
        RefreshManagedWardSpatialIndexEntry(ManagedWardRef.FromArea(area));
    }

    internal static void RefreshManagedWardSpatialIndexEntry(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        EnsureManagedWardCacheInitialized();
        if (_managedWardSpatialIndexRequiresFullRebuild ||
            !Mathf.Approximately(_managedWardSpatialIndexMaxRadius, WardSettings.MaxRadius))
        {
            if (!_managedWardSpatialIndexRequiresFullRebuild)
            {
                _managedWardSpatialIndexRequiresFullRebuild = true;
                BumpManagedWardSpatialRevision();
            }

            ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward spatial index requires rebuild");
            return;
        }

        var instanceId = area.GetInstanceID();
        var updateAllWardIndex = AllWardIndex.Contains(instanceId);
        var updateEnabledWardIndex = EnabledWardIndex.Contains(instanceId);
        if (!updateAllWardIndex && !updateEnabledWardIndex)
        {
            return;
        }

        UpdateManagedWardSpatialIndexMembership(area, instanceId, updateAllWardIndex, updateEnabledWardIndex);
    }

    internal static void ResetManagedWardCache()
    {
        AllWardIndex.Clear();
        EnabledWardIndex.Clear();
        SpatialQueryBuffer.Clear();
        ManagedWardRuntimeInvalidationService.ResetPresence();
        _wardCacheInitialized = false;
        _managedWardSpatialIndexRequiresFullRebuild = true;
        _managedWardSpatialIndexMaxRadius = -1f;
        _managedWardSpatialIndexRevision = 0;
        ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward cache reset");
    }

    internal static void UpdateTrustedPlayerPresenceSweep()
    {
        ManagedWardPresenceService.Update();
    }

    internal static IReadOnlyList<PrivateArea> GetManagedWards(bool requireEnabled)
    {
        EnsureManagedWardCacheInitialized();
        return requireEnabled ? EnabledWardIndex.Areas : AllWardIndex.Areas;
    }

    internal static bool CheckAccess(Vector3 point, float radius, long playerId, bool flash = true, bool wardCheck = false)
    {
        return !EvaluateAccess(point, radius, playerId, flash, wardCheck).IsDenied;
    }

    internal static AccessResult EvaluateAccess(Vector3 point, float radius, long playerId, bool flash = true, bool wardCheck = false)
    {
        var areas = GetCandidateManagedWards(point, radius, requireEnabled: true);
        return EvaluateAccessAgainstCandidates(point, radius, playerId, areas, flash, wardCheck);
    }

    internal static AccessResult EvaluateAccessAgainstCandidates(
        Vector3 point,
        float radius,
        long playerId,
        IReadOnlyList<PrivateArea> areas,
        bool flash = true,
        bool wardCheck = false)
    {
        if (areas.Count == 0)
        {
            return new AccessResult(AccessDecision.NoWard);
        }

        var includeDiagnosticData = Plugin.ShouldLogWardDiagnosticVerbose();
        var hasActor = ManagedWardAccessEvaluator.TryCreateActorForAccessCheck(playerId, out var actor);
        var foundWard = false;
        var anyDenied = false;
        List<PrivateArea>? blockedAreas = null;

        foreach (var area in areas)
        {
            if (area == null || !area.IsInside(point, radius))
            {
                continue;
            }

            foundWard = true;

            if (hasActor && ManagedWardAccessEvaluator.HasPlayerAccess(area, actor, includeDiagnosticData))
            {
                continue;
            }

            anyDenied = true;
            if (flash)
            {
                blockedAreas ??= new List<PrivateArea>();
                blockedAreas.Add(area);
            }

            if (wardCheck)
            {
                break;
            }
        }

        if (!foundWard)
        {
            return new AccessResult(AccessDecision.NoWard);
        }

        // Overlapping wards are additive: a single foreign ward in range is enough to deny access.
        if (!anyDenied)
        {
            return new AccessResult(AccessDecision.Allowed);
        }

        if (blockedAreas != null)
        {
            foreach (var area in blockedAreas)
            {
                area.FlashShield(false);
            }
        }

        return new AccessResult(AccessDecision.Denied);
    }

    internal static int CollectDeniedManagedWardCandidates(
        long playerId,
        IReadOnlyList<PrivateArea> areas,
        List<PrivateArea> deniedAreas)
    {
        deniedAreas.Clear();
        if (playerId == 0L || areas.Count == 0)
        {
            return 0;
        }

        var includeDiagnosticData = Plugin.ShouldLogWardDiagnosticVerbose();
        var actor = ManagedWardAccessEvaluator.CreateActor(playerId);
        foreach (var area in areas)
        {
            if (area == null || ManagedWardAccessEvaluator.HasPlayerAccess(area, actor, includeDiagnosticData))
            {
                continue;
            }

            deniedAreas.Add(area);
        }

        return deniedAreas.Count;
    }

    internal static bool IsInsideAnyManagedWard(Vector3 point, float radius, IReadOnlyList<PrivateArea> areas)
    {
        for (var index = 0; index < areas.Count; index++)
        {
            var area = areas[index];
            if (area != null && area.IsInside(point, radius))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryBlockInteraction(Component target, Player? player, ref bool result)
    {
        var blocked = ShouldBlock(target, player, 0f);
        LogLocalInteractionAttemptVerbose("Interaction", target, player, blocked);
        if (!blocked)
        {
            return true;
        }

        ShowNoAccessMessage(player);
        result = true;
        return false;
    }

    internal static bool TryBlockAction(Component target, Player? player, ref bool result)
    {
        var blocked = ShouldBlock(target, player, 0f);
        LogLocalInteractionAttemptVerbose("Action", target, player, blocked);
        if (!blocked)
        {
            return true;
        }

        ShowNoAccessMessage(player);
        result = false;
        return false;
    }

    internal static bool TryBlockVoid(Component target, Player? player)
    {
        var blocked = ShouldBlock(target, player, 0f);
        LogLocalInteractionAttemptVerbose("Void", target, player, blocked);
        if (!blocked)
        {
            return true;
        }

        ShowNoAccessMessage(player);
        return false;
    }

    internal static bool TryBlockPlacement(Player? player, Vector3 point, float radius, ref bool result)
    {
        if (!ShouldBlock(point, radius, player))
        {
            return true;
        }

        ShowNoAccessMessage(player);
        result = false;
        return false;
    }

    internal static bool TryBlockPlacement(Player? player, Vector3 point, float radius)
    {
        if (!ShouldBlock(point, radius, player))
        {
            return true;
        }

        ShowNoAccessMessage(player);
        return false;
    }

    internal static bool TryBlockItemUse(Player? player, ItemDrop.ItemData? item, ref bool result)
    {
        if (!ShouldBlockConfiguredItemUse(player, item))
        {
            return true;
        }

        ShowBlockedItemMessage(player);
        result = false;
        return false;
    }

    internal static bool TryBlockItemUse(Player? player, ItemDrop.ItemData? item)
    {
        if (!ShouldBlockConfiguredItemUse(player, item))
        {
            return true;
        }

        ShowBlockedItemMessage(player);
        return false;
    }

    internal static bool TryBlockItemUse(Player? player, ItemDrop.ItemData? item, Vector3 targetPoint)
    {
        if (!ShouldBlockConfiguredItemUse(player, item, targetPoint))
        {
            return true;
        }

        ShowBlockedItemMessage(player);
        return false;
    }

    internal static bool TryForceUnequipBlockedItems(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return false;
        }

        if (!HasEnabledManagedWards())
        {
            return false;
        }

        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        var inventoryItems = inventory.m_inventory;
        if (inventoryItems == null || inventoryItems.Count == 0)
        {
            return false;
        }

        var hasBlockedEquippedItem = false;
        for (var index = 0; index < inventoryItems.Count; index++)
        {
            var item = inventoryItems[index];
            if (item == null || !item.m_equipped)
            {
                continue;
            }

            if (!IsConfiguredBlockedItem(item))
            {
                continue;
            }

            hasBlockedEquippedItem = true;
            break;
        }

        if (!hasBlockedEquippedItem)
        {
            return false;
        }

        // This path runs continuously from UpdateEquipment, so ward checks here must stay silent.
        if (!ShouldBlock(player.transform.position, 0f, player, flash: false))
        {
            return false;
        }

        var unequippedAny = false;
        for (var index = 0; index < inventoryItems.Count; index++)
        {
            var item = inventoryItems[index];
            if (item == null || !item.m_equipped)
            {
                continue;
            }

            if (!IsConfiguredBlockedItem(item))
            {
                continue;
            }

            player.UnequipItem(item, false);
            unequippedAny = true;
        }

        if (unequippedAny)
        {
            ShowBlockedItemMessage(player);
        }

        return unequippedAny;
    }

    internal static bool TryBlockAttack(Player? player, ref bool result)
    {
        return TryBlockAttack(player, player?.GetCurrentWeapon(), ref result);
    }

    internal static bool TryBlockAttack(Player? player, ItemDrop.ItemData? item, ref bool result)
    {
        if (!ShouldBlockConfiguredItemUse(player, item) && !ShouldBlockConfiguredItemUseAgainstHoveredTamedCreature(player, item))
        {
            return true;
        }

        if (!TryForceUnequipBlockedItems(player))
        {
            ShowBlockedItemMessage(player);
        }
        result = false;
        return false;
    }

    internal static bool ShouldBlock(Component? target, Player? player, float radius, bool flash = true)
    {
        if (target == null)
        {
            return false;
        }

        return ShouldBlock(target.transform.position, radius, player, flash);
    }

    internal static bool ShouldBlock(Vector3 point, float radius, Player? player, bool flash = true)
    {
        if (player == null)
        {
            return false;
        }

        if (!HasEnabledManagedWards())
        {
            return false;
        }

        return EvaluateAccess(point, radius, player.GetPlayerID(), flash).IsDenied;
    }

    internal static bool ShouldBlockPickup(GameObject? go, Player? player)
    {
        if (go == null || !WardItemPrefabPolicy.CanAnyPickupBeBlocked())
        {
            return false;
        }

        return WardItemPrefabPolicy.ShouldBlockPickup(go) && ShouldBlock(go.transform.position, 0f, player);
    }

    internal static bool ShouldBlockPickup(ItemDrop? itemDrop, Player? player)
    {
        if (itemDrop == null || !WardItemPrefabPolicy.CanAnyPickupBeBlocked())
        {
            return false;
        }

        return WardItemPrefabPolicy.ShouldBlockPickup(itemDrop) && ShouldBlock(itemDrop, player, 0f);
    }

    internal static void ShowNoAccessMessage(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        player.Message(MessageHud.MessageType.Center, NoAccessMessageKey, 0, null);
    }

    private static void LogLocalInteractionAttemptVerbose(string context, Component? target, Player? player, bool blocked)
    {
        if (!Plugin.ShouldLogWardDiagnosticVerbose() || target == null || player == null)
        {
            return;
        }

        var playerId = player.GetPlayerID();
        Plugin.LogWardDiagnosticVerbose(
            $"Access.{context}",
            $"Evaluated local interaction before server handling. blocked={blocked}, targetType={target.GetType().Name}, targetName='{target.name}', playerId={playerId}, playerName='{player.GetPlayerName()}', position={target.transform.position}");
    }

    internal static void ShowBlockedItemMessage(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        player.Message(
            MessageHud.MessageType.Center,
            WardLocalization.Localize(WardLocalization.MessageBlockedItemToken, WardLocalization.MessageBlockedItemFallback),
            0,
            null);
    }

    internal static void ShowProtectedBuildingDamageMessage(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        player.Message(
            MessageHud.MessageType.Center,
            WardLocalization.Localize(
                WardLocalization.MessageBuildingDamageProtectedToken,
                WardLocalization.MessageBuildingDamageProtectedFallback),
            0,
            null);
    }

    internal static void ShowWardOverlapMessage(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        player.Message(
            MessageHud.MessageType.Center,
            WardLocalization.Localize(WardLocalization.MessageOverlapToken, WardLocalization.MessageOverlapFallback),
            0,
            null);
    }

    internal static void ShowWardLimitMessage(Player? player, int limit)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        var message = WardLocalization.LocalizeFormat(
            WardLocalization.MessageLimitWithMaxToken,
            WardLocalization.MessageLimitWithMaxFallback,
            limit);
        player.Message(MessageHud.MessageType.Center, message, 0, null);
    }

    internal static Player? GetPlayer(Humanoid? humanoid)
    {
        return humanoid as Player;
    }

    internal static Player? GetPlayer(Collider? collider)
    {
        if (collider == null)
        {
            return null;
        }

        return collider.GetComponentInParent<Player>();
    }

    internal static float GetTerrainRadius(GameObject? prefab)
    {
        if (prefab == null)
        {
            return 0f;
        }

        var terrainModifier = prefab.GetComponent<TerrainModifier>();
        if (terrainModifier != null)
        {
            return terrainModifier.GetRadius();
        }

        var terrainOp = prefab.GetComponent<TerrainOp>();
        return terrainOp != null ? terrainOp.GetRadius() : 0f;
    }

    internal static bool IsRelevantWard(PrivateArea? area)
    {
        return IsManagedWard(area, true);
    }

    internal static bool IsManagedWard(PrivateArea? area, bool requireEnabled)
    {
        return IsManagedWard(ManagedWardRef.FromArea(area), requireEnabled);
    }

    internal static bool IsManagedWard(ManagedWardRef ward, bool requireEnabled)
    {
        return IsTrackableManagedWard(ward, requireEnabled);
    }

    internal static bool CanConfigureWard(PrivateArea? area, Player? player)
    {
        return CanConfigureWard(ManagedWardRef.FromArea(area), player);
    }

    internal static bool CanConfigureWard(ManagedWardRef ward, Player? player)
    {
        if (ward.Area == null || player == null || player != Player.m_localPlayer || !IsManagedWard(ward, false))
        {
            return false;
        }

        return IsDirectWardOwner(ward, player.GetPlayerID()) || WardAdminDebugAccess.CanLocallyControlAnyWard(ward.Area, player);
    }

    internal static bool CanControlManagedWard(PrivateArea? area, long playerId)
    {
        return CanControlManagedWard(ManagedWardRef.FromArea(area), playerId);
    }

    internal static bool CanControlManagedWard(ManagedWardRef ward, long playerId)
    {
        if (ward.Area == null || playerId == 0L || !IsManagedWard(ward, false))
        {
            return false;
        }

        return IsDirectWardOwner(ward, playerId) || WardAdminDebugAccess.IsPlayerAdminDebugController(playerId);
    }

    internal static bool IsDirectWardOwner(PrivateArea? area, Player? player)
    {
        return area != null && player != null && IsDirectWardOwner(area, player.GetPlayerID());
    }

    internal static bool IsDirectWardOwner(PrivateArea? area, long playerId)
    {
        return IsDirectWardOwner(ManagedWardRef.FromArea(area), playerId);
    }

    internal static bool IsDirectWardOwner(ManagedWardRef ward, long playerId)
    {
        if (ward.Area == null || playerId == 0L || !IsManagedWard(ward, false))
        {
            return false;
        }

        return GetCanonicalCreatorPlayerId(ward.Area) == playerId;
    }

    internal static bool IsPlayerInWardGuild(Player? player, PrivateArea? area)
    {
        return IsPlayerGuildMatchingWardGuild(
            GuildsCompat.GetPlayerGuildIdentity(player),
            GuildsCompat.GetWardGuildIdentity(area));
    }

    internal static bool IsPlayerIdInWardGuild(long playerId, PrivateArea? area)
    {
        if (playerId == 0L)
        {
            return false;
        }

        return IsPlayerGuildMatchingWardGuild(
            GuildsCompat.GetPlayerGuildIdentity(playerId),
            GuildsCompat.GetWardGuildIdentity(area));
    }

    internal static bool IsPlayerIdInWardGuild(long playerId, ZDO? zdo)
    {
        if (playerId == 0L)
        {
            return false;
        }

        return IsPlayerGuildMatchingWardGuild(
            GuildsCompat.GetPlayerGuildIdentity(playerId),
            GuildsCompat.GetWardGuildIdentity(zdo));
    }

    internal static bool IsPlayerGuildMatchingWardGuild(WardGuildIdentity playerGuild, WardGuildIdentity wardGuild)
    {
        return ManagedWardAccessEvaluator.HasMatchingGuild(playerGuild, wardGuild);
    }

    internal static bool ShouldBlockHostileCreatureDamageToBuilding(Vector3 point)
    {
        return ManagedWardPresenceService.ShouldBlockHostileCreatureDamageToBuilding(point);
    }

    internal static bool TryBlockManagedWardPlacement(Player? player, Component? candidate, Vector3 point, ref bool result)
    {
        if (!IsManagedWardPlacementCandidate(candidate))
        {
            return true;
        }

        if (!WouldBlockManagedWardPlacement(player, candidate, point, flash: true))
        {
            return true;
        }

        ShowWardOverlapMessage(player);
        result = false;
        return false;
    }

    internal static bool TryBlockManagedWardPlacement(Player? player, Component? candidate, Vector3 point)
    {
        if (!IsManagedWardPlacementCandidate(candidate))
        {
            return true;
        }

        if (!WouldBlockManagedWardPlacement(player, candidate, point, flash: true))
        {
            return true;
        }

        ShowWardOverlapMessage(player);
        return false;
    }

    internal static float GetMaxNonOverlappingRadius(PrivateArea? area)
    {
        return GetMaxNonOverlappingRadius(area, WardSettings.MaxRadius);
    }

    internal static float GetMaxNonOverlappingRadius(PrivateArea? area, float fallbackRadius)
    {
        var ownerCreatorPlayerId = GetCanonicalCreatorPlayerId(area);
        var guildId = GuildsCompat.IsAvailable() ? GuildsCompat.GetWardGuildId(area) : 0;
        return area == null
            ? fallbackRadius
            : GetMaxNonOverlappingRadius(area.transform.position, ownerCreatorPlayerId, guildId, area, fallbackRadius);
    }

    internal static float GetMaxNonOverlappingRadius(
        Vector3 point,
        long ownerCreatorPlayerId,
        int guildId,
        PrivateArea? ignoredWard,
        float fallbackRadius)
    {
        var allAreas = GetCandidateManagedWards(point, fallbackRadius, requireEnabled: false);
        if (allAreas.Count == 0)
        {
            return fallbackRadius;
        }

        return WardOverlapPolicy.GetMaxNonOverlappingRadius(
            fallbackRadius,
            CreateWardOverlapQuery(point, fallbackRadius, ownerCreatorPlayerId, guildId, ignoredWard),
            BuildWardOverlapAreas(allAreas, guildId));
    }

    internal static long GetCanonicalCreatorPlayerId(PrivateArea? area)
    {
        var zdoCreator = GetWardCreatorId(area);
        if (zdoCreator != 0L)
        {
            return zdoCreator;
        }

        var piece = area?.m_piece != null ? area.m_piece : area?.GetComponent<Piece>();
        return piece != null ? piece.GetCreator() : 0L;
    }

    internal static long GetWardCreatorId(PrivateArea? area)
    {
        return ManagedWardRef.FromArea(area).CreatorPlayerId;
    }

    internal static PrivateArea? FindNearestManagedWard(
        Vector3 point,
        float radius = 0f,
        bool requireEnabled = true,
        Predicate<PrivateArea>? predicate = null)
    {
        var allAreas = GetCandidateManagedWards(point, radius, requireEnabled);
        if (allAreas.Count == 0)
        {
            return null;
        }

        PrivateArea? closest = null;
        var closestDistance = float.MaxValue;

        foreach (var area in allAreas)
        {
            if (area == null || !area.IsInside(point, radius))
            {
                continue;
            }

            if (predicate != null && !predicate(area))
            {
                continue;
            }

            var distance = Utils.DistanceXZ(area.transform.position, point);
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closest = area;
        }

        return closest;
    }

    private static bool ShouldBlockConfiguredItemUse(Player? player, ItemDrop.ItemData? item)
    {
        return ShouldBlockConfiguredItemUse(player, item, null);
    }

    private static bool ShouldBlockConfiguredItemUse(Player? player, ItemDrop.ItemData? item, Vector3? targetPoint)
    {
        if (player == null || item == null)
        {
            return false;
        }

        if (!HasEnabledManagedWards())
        {
            return false;
        }

        if (!IsConfiguredBlockedItem(item))
        {
            return false;
        }

        if (ShouldBlock(player.transform.position, 0f, player))
        {
            return true;
        }

        return targetPoint.HasValue && ShouldBlock(targetPoint.Value, 0f, player);
    }

    private static bool ShouldBlockConfiguredItemUseAgainstHoveredTamedCreature(Player? player, ItemDrop.ItemData? item)
    {
        return TryGetHoveredTamedCreaturePoint(player, out var targetPoint) && ShouldBlockConfiguredItemUse(player, item, targetPoint);
    }

    private static bool IsConfiguredBlockedItem(ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            return false;
        }

        return WardItemPrefabPolicy.IsBlockedItem(item);
    }

    private static bool TryGetHoveredTamedCreaturePoint(Player? player, out Vector3 targetPoint)
    {
        targetPoint = default;
        if (player == null)
        {
            return false;
        }

        var hoveringCreature = player.m_hoveringCreature;
        if (hoveringCreature != null && hoveringCreature.IsTamed())
        {
            targetPoint = hoveringCreature.transform.position;
            return true;
        }

        try
        {
            Character? hoveredCharacter = null;
            GameObject? hoveredObject = null;
            player.FindHoverObject(out hoveredObject, out hoveredCharacter);
            if (hoveredCharacter == null || !hoveredCharacter.IsTamed())
            {
                return false;
            }

            targetPoint = hoveredCharacter.transform.position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool WouldBlockManagedWardPlacement(Player? player, Component? candidate, Vector3 point, bool flash)
    {
        if (player == null)
        {
            return false;
        }

        var candidateArea = candidate != null
            ? candidate.GetComponent<PrivateArea>() ?? candidate.GetComponentInParent<PrivateArea>()
            : null;
        if (!StuWardArea.IsManaged(candidateArea))
        {
            return false;
        }

        // Placement overlap is evaluated against the minimum legal ward size.
        // The actual configured radius is still clamped later when the ward is edited.
        var radius = WardSettings.MinRadius;
        return OverlapsForeignManagedWard(
            point,
            radius,
            player.GetPlayerID(),
            GuildsCompat.GetPlayerGuildId(player),
            null,
            flash);
    }

    private static bool OverlapsForeignManagedWard(
        Vector3 point,
        float radius,
        long ownerCreatorPlayerId,
        int guildId,
        PrivateArea? ignoredWard,
        bool flash)
    {
        var overlappingAreas = flash ? new List<PrivateArea>() : null;
        var allAreas = GetCandidateManagedWards(point, radius, requireEnabled: false);
        if (allAreas.Count == 0)
        {
            return false;
        }

        var overlaps = false;
        var query = CreateWardOverlapQuery(point, radius, ownerCreatorPlayerId, guildId, ignoredWard);
        foreach (var area in allAreas)
        {
            if (area == null || area == ignoredWard)
            {
                continue;
            }

            var overlapArea = CreateWardOverlapArea(area, guildId);
            if (WardOverlapPolicy.SharesTrustedWardGroup(overlapArea, query) ||
                !WardOverlapPolicy.Overlaps(query, overlapArea))
            {
                continue;
            }

            overlaps = true;
            overlappingAreas?.Add(area);
        }

        if (!overlaps || overlappingAreas == null)
        {
            return overlaps;
        }

        foreach (var area in overlappingAreas)
        {
            var nview = WardPrivateAreaSafeAccess.GetNView(area);
            if (nview == null || !nview.IsValid())
            {
                continue;
            }

            area.FlashShield(false);
        }

        return true;
    }

    private static WardOverlapQuery CreateWardOverlapQuery(
        Vector3 point,
        float radius,
        long ownerCreatorPlayerId,
        int guildId,
        PrivateArea? ignoredWard)
    {
        return new WardOverlapQuery(
            point.x,
            point.z,
            radius,
            ownerCreatorPlayerId,
            guildId,
            ignoredWard != null ? ignoredWard.GetInstanceID() : 0);
    }

    private static List<WardOverlapArea> BuildWardOverlapAreas(IReadOnlyList<PrivateArea> areas, int queryGuildId)
    {
        var overlapAreas = new List<WardOverlapArea>(areas.Count);
        for (var index = 0; index < areas.Count; index++)
        {
            var area = areas[index];
            if (area != null)
            {
                overlapAreas.Add(CreateWardOverlapArea(area, queryGuildId));
            }
        }

        return overlapAreas;
    }

    private static WardOverlapArea CreateWardOverlapArea(PrivateArea area, int queryGuildId)
    {
        var position = area.transform.position;
        return new WardOverlapArea(
            area.GetInstanceID(),
            position.x,
            position.z,
            WardSettings.GetStoredRadiusOrMin(area),
            GetCanonicalCreatorPlayerId(area),
            queryGuildId != 0 && GuildsCompat.IsAvailable() ? GuildsCompat.GetWardGuildId(area) : 0);
    }

    internal static int GetManagedWardSpatialIndexRevision()
    {
        return _managedWardSpatialIndexRevision;
    }

    internal static bool IsManagedWardPlacementCandidate(Component? candidate)
    {
        var candidateArea = candidate != null
            ? candidate.GetComponent<PrivateArea>() ?? candidate.GetComponentInParent<PrivateArea>()
            : null;
        return StuWardArea.IsManaged(candidateArea);
    }

    private static void EnsureManagedWardCacheInitialized()
    {
        if (_wardCacheInitialized)
        {
            return;
        }

        _wardCacheInitialized = true;
        AllWardIndex.Clear();
        EnabledWardIndex.Clear();

        var allAreas = PrivateArea.m_allAreas;
        if (allAreas == null)
        {
            return;
        }

        for (var index = 0; index < allAreas.Count; index++)
        {
            var area = allAreas[index];
            if (!IsTrackableManagedWard(area, requireEnabled: false))
            {
                continue;
            }

            AllWardIndex.Add(area);
            if (!area.IsEnabled())
            {
                continue;
            }

            EnabledWardIndex.Add(area);
        }

        _managedWardSpatialIndexRequiresFullRebuild = true;
    }

    private static bool IsTrackableManagedWard(PrivateArea? area, bool requireEnabled)
    {
        return IsTrackableManagedWard(ManagedWardRef.FromArea(area), requireEnabled);
    }

    private static bool IsTrackableManagedWard(ManagedWardRef ward, bool requireEnabled)
    {
        var area = ward.Area;
        if (area == null || ward.IsPlacementGhost || !ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return false;
        }

        if (requireEnabled && !area.IsEnabled())
        {
            return false;
        }

        return ward.HasValidNetworkIdentity;
    }

    internal static IReadOnlyList<PrivateArea> GetCandidateManagedWards(Vector3 point, float radius, bool requireEnabled)
    {
        // Shared scratch result; use FillCandidateManagedWards with an owned list when nesting or storing candidates.
        FillCandidateManagedWards(point, radius, requireEnabled, SpatialQueryBuffer);
        return SpatialQueryBuffer;
    }

    internal static void FillCandidateManagedWards(
        Vector3 point,
        float radius,
        bool requireEnabled,
        List<PrivateArea> destination)
    {
        EnsureManagedWardSpatialIndexInitialized();
        (requireEnabled ? EnabledWardIndex : AllWardIndex).FillCandidates(point, radius, destination);
    }

    private static void EnsureManagedWardSpatialIndexInitialized()
    {
        EnsureManagedWardCacheInitialized();

        var maxRadius = WardSettings.MaxRadius;
        if (!_managedWardSpatialIndexRequiresFullRebuild && Mathf.Approximately(_managedWardSpatialIndexMaxRadius, maxRadius))
        {
            return;
        }

        AllWardIndex.ClearSpatialIndex();
        EnabledWardIndex.ClearSpatialIndex();
        SpatialQueryBuffer.Clear();

        AllWardIndex.RebuildSpatialIndex();
        EnabledWardIndex.RebuildSpatialIndex();

        _managedWardSpatialIndexRequiresFullRebuild = false;
        _managedWardSpatialIndexMaxRadius = maxRadius;
    }

    private static void UpdateManagedWardSpatialIndexMembership(
        PrivateArea area,
        int instanceId,
        bool updateAllWardIndex,
        bool updateEnabledWardIndex)
    {
        EnsureManagedWardCacheInitialized();
        if (_managedWardSpatialIndexRequiresFullRebuild ||
            !Mathf.Approximately(_managedWardSpatialIndexMaxRadius, WardSettings.MaxRadius))
        {
            if (!_managedWardSpatialIndexRequiresFullRebuild)
            {
                _managedWardSpatialIndexRequiresFullRebuild = true;
                if (updateAllWardIndex)
                {
                    BumpManagedWardSpatialRevision();
                }
            }

            if (updateAllWardIndex)
            {
                ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward spatial index deferred rebuild");
            }

            return;
        }

        if (updateAllWardIndex)
        {
            AllWardIndex.UpdateSpatialIndex(area, instanceId, AllWardIndex.Contains(instanceId));
            BumpManagedWardSpatialRevision();
            ManagedWardRuntimeInvalidationService.PublishSpatialIndexChanged("managed ward spatial index membership changed");
        }

        if (updateEnabledWardIndex)
        {
            EnabledWardIndex.UpdateSpatialIndex(area, instanceId, EnabledWardIndex.Contains(instanceId));
        }
    }

    private static void BumpManagedWardSpatialRevision()
    {
        _managedWardSpatialIndexRevision = _managedWardSpatialIndexRevision == int.MaxValue
            ? 1
            : _managedWardSpatialIndexRevision + 1;
    }

}
