using System.Collections.Generic;

namespace STUWard;

internal readonly struct ManagedWardScanEntry
{
    internal ManagedWardScanEntry(
        ZDO zdo,
        ZDOID zdoId,
        long ownerPlayerId,
        string accountId,
        string ownerName,
        bool isEnabled,
        float radius)
    {
        Zdo = zdo;
        ZdoId = zdoId;
        OwnerPlayerId = ownerPlayerId;
        AccountId = accountId ?? string.Empty;
        OwnerName = ownerName ?? string.Empty;
        IsEnabled = isEnabled;
        Radius = radius;
    }

    internal ZDO Zdo { get; }
    internal ZDOID ZdoId { get; }
    internal long OwnerPlayerId { get; }
    internal string AccountId { get; }
    internal string OwnerName { get; }
    internal bool IsEnabled { get; }
    internal float Radius { get; }
}

internal static partial class WardOwnership
{
    internal static bool TryPrepareManagedWardPrefabScan(out int scannedZdoCount)
    {
        scannedZdoCount = 0;
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return false;
        }

        var zdoMan = ZDOMan.instance;
        if (zdoMan == null)
        {
            return false;
        }

        scannedZdoCount = PrepareManagedWardPrefabScan(zdoMan);
        return true;
    }

    internal static ZDO? GetPreparedManagedWardPrefabScanEntry(int index)
    {
        return index >= 0 && index < ManagedWardPrefabScanBuffer.Count
            ? ManagedWardPrefabScanBuffer[index]
            : null;
    }

    internal static bool TryBuildManagedWardScanEntries(List<ManagedWardScanEntry> scanEntries, out int scannedZdoCount)
    {
        scanEntries.Clear();
        if (!TryPrepareManagedWardPrefabScan(out scannedZdoCount))
        {
            return false;
        }

        for (var index = 0; index < scannedZdoCount; index++)
        {
            if (!TryBuildManagedWardScanEntry(GetPreparedManagedWardPrefabScanEntry(index), out var scanEntry))
            {
                continue;
            }

            scanEntries.Add(scanEntry);
        }

        return true;
    }

    private static bool TryBuildManagedWardScanEntry(ZDO? zdo, out ManagedWardScanEntry scanEntry)
    {
        scanEntry = default;
        if (!IsManagedWardZdo(zdo))
        {
            return false;
        }

        var managedWardZdo = zdo!;
        var ownerPlayerId = managedWardZdo.GetLong(ZDOVars.s_creator, 0L);
        var accountId = NormalizeAccountIdValue(
            ResolveWardSteamAccountId(
                managedWardZdo,
                ownerPlayerId,
                GetWardSteamAccountId(managedWardZdo)));
        var ownerName = (WardPrivateAreaSafeAccess.GetCreatorName(managedWardZdo) ?? string.Empty).Trim();
        scanEntry = new ManagedWardScanEntry(
            managedWardZdo,
            managedWardZdo.m_uid,
            ownerPlayerId,
            accountId,
            ownerName,
            managedWardZdo.GetBool(ZDOVars.s_enabled, false),
            WardSettings.GetStoredRadius(managedWardZdo, WardSettings.MinRadius));
        return true;
    }
}
