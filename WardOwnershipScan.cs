using System.Collections.Generic;

namespace STUWard;

internal readonly struct ManagedWardScanEntry
{
    internal ManagedWardScanEntry(
        long ownerPlayerId,
        string accountId)
    {
        OwnerPlayerId = ownerPlayerId;
        AccountId = accountId ?? string.Empty;
    }

    internal long OwnerPlayerId { get; }
    internal string AccountId { get; }
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
        scanEntry = new ManagedWardScanEntry(
            ownerPlayerId,
            accountId);
        return true;
    }
}
