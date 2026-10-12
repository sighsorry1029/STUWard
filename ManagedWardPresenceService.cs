using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal static class ManagedWardPresenceService
{
    private const float TrustedPresenceSweepActiveDurationSeconds = 15f;
    private const float TrustedPlayerRangeBuffer = 8f;
    private const float TrustedPresenceGraceSeconds = 10f;
    private const float PresenceRefreshIntervalSeconds = 1f;

    private static readonly List<PrivateArea> TrustedPresenceCandidateBuffer = new();
    private static readonly List<PrivateArea> TrustedPresenceCoverageBuffer = new();

    private static bool _trustedPresenceSweepWasActive;
    private static float _trustedPresenceSweepActiveUntilTime = float.NegativeInfinity;
    private static float _nextTrustedPresenceSweepTime = float.NegativeInfinity;

    internal static void Invalidate()
    {
        ManagedWardRuntimeContexts.ResetPresenceStates();
        _trustedPresenceSweepActiveUntilTime = float.NegativeInfinity;
        _nextTrustedPresenceSweepTime = float.NegativeInfinity;
        _trustedPresenceSweepWasActive = false;
    }

    internal static void ResetRuntimeState()
    {
        ManagedWardRuntimeContexts.ResetPresenceStates();
        TrustedPresenceCandidateBuffer.Clear();
        TrustedPresenceCoverageBuffer.Clear();
        _trustedPresenceSweepWasActive = false;
        _trustedPresenceSweepActiveUntilTime = float.NegativeInfinity;
        _nextTrustedPresenceSweepTime = float.NegativeInfinity;
    }

    internal static void Update()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var requiresTrustedPresenceSweep =
            GetHostileCreatureStructureProtectionMode() == Plugin.HostileCreatureStructureProtectionMode.UnattendedOnly;
        if (!requiresTrustedPresenceSweep || !WardAccess.HasEnabledManagedWards())
        {
            if (_trustedPresenceSweepWasActive)
            {
                ManagedWardRuntimeContexts.ResetPresenceStates();
                _trustedPresenceSweepWasActive = false;
                _trustedPresenceSweepActiveUntilTime = float.NegativeInfinity;
                _nextTrustedPresenceSweepTime = float.NegativeInfinity;
            }

            return;
        }

        var now = Time.time;
        if (!_trustedPresenceSweepWasActive || now > _trustedPresenceSweepActiveUntilTime)
        {
            _trustedPresenceSweepWasActive = false;
            _trustedPresenceSweepActiveUntilTime = float.NegativeInfinity;
            _nextTrustedPresenceSweepTime = float.NegativeInfinity;
            return;
        }

        if (now < _nextTrustedPresenceSweepTime)
        {
            return;
        }

        RefreshTrustedPlayerPresence(now);
        _nextTrustedPresenceSweepTime = now + PresenceRefreshIntervalSeconds;
    }

    internal static bool ShouldBlockHostileCreatureDamageToBuilding(Vector3 point)
    {
        return GetHostileCreatureStructureProtectionMode() switch
        {
            Plugin.HostileCreatureStructureProtectionMode.Off => false,
            Plugin.HostileCreatureStructureProtectionMode.Always => IsInsideEnabledWard(point),
            _ => ShouldBlockUnattendedHostileCreatureDamageToBuilding(point)
        };
    }

    private static bool ShouldBlockUnattendedHostileCreatureDamageToBuilding(Vector3 point)
    {
        var now = Time.time;
        WardAccess.FillCandidateManagedWards(point, 0f, requireEnabled: true, TrustedPresenceCandidateBuffer);
        if (TrustedPresenceCandidateBuffer.Count == 0)
        {
            return false;
        }

        TrustedPresenceCoverageBuffer.Clear();
        for (var index = 0; index < TrustedPresenceCandidateBuffer.Count; index++)
        {
            var area = TrustedPresenceCandidateBuffer[index];
            if (area == null || !area.IsInside(point, 0f))
            {
                continue;
            }

            TrustedPresenceCoverageBuffer.Add(area);
        }

        if (TrustedPresenceCoverageBuffer.Count == 0)
        {
            return false;
        }

        EnsureTrustedPlayerPresenceSweepActivity(now, TrustedPresenceCoverageBuffer, TrustedPresenceGraceSeconds);
        for (var index = 0; index < TrustedPresenceCoverageBuffer.Count; index++)
        {
            var area = TrustedPresenceCoverageBuffer[index];
            if (!IsWardConsideredAttended(area, now, TrustedPresenceGraceSeconds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideEnabledWard(Vector3 point)
    {
        WardAccess.FillCandidateManagedWards(point, 0f, requireEnabled: true, TrustedPresenceCandidateBuffer);
        if (TrustedPresenceCandidateBuffer.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < TrustedPresenceCandidateBuffer.Count; index++)
        {
            var area = TrustedPresenceCandidateBuffer[index];
            if (area != null && area.IsInside(point, 0f))
            {
                return true;
            }
        }

        return false;
    }

    private static Plugin.HostileCreatureStructureProtectionMode GetHostileCreatureStructureProtectionMode()
    {
        return Plugin.HostileCreatureStructureProtection != null
            ? Plugin.HostileCreatureStructureProtection.Value
            : Plugin.HostileCreatureStructureProtectionMode.UnattendedOnly;
    }

    private static void EnsureTrustedPlayerPresenceSweepActivity(
        float now,
        IReadOnlyList<PrivateArea> coveringAreas,
        float graceSeconds)
    {
        var isCurrentlyActive = _trustedPresenceSweepWasActive && now <= _trustedPresenceSweepActiveUntilTime;
        if (!isCurrentlyActive || HasUnattendedPresenceState(coveringAreas, now, graceSeconds))
        {
            RefreshTrustedPlayerPresence(now, coveringAreas);
            _nextTrustedPresenceSweepTime = now + PresenceRefreshIntervalSeconds;
        }

        _trustedPresenceSweepWasActive = true;
        _trustedPresenceSweepActiveUntilTime = now + TrustedPresenceSweepActiveDurationSeconds;
    }

    private static void RefreshTrustedPlayerPresence(float now, IReadOnlyList<PrivateArea>? coveringAreas = null)
    {
        if (coveringAreas?.Count == 0)
        {
            return;
        }

        var players = Player.GetAllPlayers();
        if (players == null || players.Count == 0)
        {
            return;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            if (player == null)
            {
                continue;
            }

            var playerId = player.GetPlayerID();
            if (playerId == 0L)
            {
                continue;
            }

            var playerPosition = player.transform.position;
            var actor = ManagedWardAccessEvaluator.CreateActor(playerId);
            var areas = coveringAreas;
            if (areas == null)
            {
                // A damage check already has exact coverage; a periodic sweep
                // queries candidates per player. Wards occupy all cells touched
                // by their radius, so only the extra presence range is needed.
                WardAccess.FillCandidateManagedWards(
                    playerPosition,
                    TrustedPlayerRangeBuffer,
                    requireEnabled: true,
                    TrustedPresenceCandidateBuffer);
                areas = TrustedPresenceCandidateBuffer;
            }

            for (var areaIndex = 0; areaIndex < areas.Count; areaIndex++)
            {
                var area = areas[areaIndex];
                if (area == null || !area.IsInside(playerPosition, TrustedPlayerRangeBuffer))
                {
                    continue;
                }

                if (!ManagedWardAccessEvaluator.HasPlayerAccess(area, actor))
                {
                    continue;
                }

                ManagedWardRuntimeContexts.GetOrCreate(area).PresenceLastTrustedNearbyTime = now;
            }
        }
    }

    private static bool IsWardConsideredAttended(PrivateArea area, float now, float graceSeconds)
    {
        return ManagedWardRuntimeContexts.TryGet(area, out var context) &&
               now - context.PresenceLastTrustedNearbyTime <= graceSeconds;
    }

    private static bool HasUnattendedPresenceState(
        IReadOnlyList<PrivateArea> coveringAreas,
        float now,
        float graceSeconds)
    {
        for (var index = 0; index < coveringAreas.Count; index++)
        {
            var area = coveringAreas[index];
            if (area == null || IsWardConsideredAttended(area, now, graceSeconds))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
