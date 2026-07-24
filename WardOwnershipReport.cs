using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace STUWard;

internal static partial class WardOwnership
{
    private const string ReportFileName = "STUWard.WardCountReport.yml";

    private readonly struct ManagedWardScanEntry
    {
        internal ManagedWardScanEntry(long ownerPlayerId, string accountId)
        {
            OwnerPlayerId = ownerPlayerId;
            AccountId = accountId ?? string.Empty;
        }

        internal long OwnerPlayerId { get; }
        internal string AccountId { get; }
    }

    internal static string GetReportFilePath()
    {
        return Path.Combine(Paths.ConfigPath, ReportFileName);
    }

    private static string GetCurrentWorldName()
    {
        var worldName = ZNet.instance?.GetWorldName();
        return string.IsNullOrWhiteSpace(worldName) ? "unknown_world" : worldName!.Trim();
    }

    private static long GetCurrentWorldUid()
    {
        return ZNet.instance?.GetWorldUID() ?? 0L;
    }

    internal static bool TryWriteWardCountReport(out string reportPath, out int trackedAccounts, out int totalWards, out int unresolvedOwners)
    {
        reportPath = GetReportFilePath();
        if (!TryBuildWardCountReport(out var reportContents, out trackedAccounts, out totalWards, out unresolvedOwners))
        {
            return false;
        }

        try
        {
            File.WriteAllText(reportPath, reportContents);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to write ward report file '{reportPath}': {exception.Message}");
            return false;
        }
    }

    internal static bool TryBuildWardCountReport(out string reportContents, out int trackedAccounts, out int totalWards, out int unresolvedOwners)
    {
        reportContents = string.Empty;
        trackedAccounts = 0;
        totalWards = 0;
        unresolvedOwners = 0;

        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return false;
        }

        try
        {
            ReloadOverrides(force: false);
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null)
            {
                return false;
            }

            var scanEntries = BuildManagedWardScanEntries(zdoMan);
            var accounts = CollectReportWardCountsByAccount(scanEntries);
            accounts.Sort(static (left, right) =>
            {
                var countCompare = right.Value.CompareTo(left.Value);
                return countCompare != 0
                    ? countCompare
                    : string.CompareOrdinal(left.Key, right.Key);
            });

            var reportAccounts = new List<ManagedWardReportAccountEntry>(accounts.Count);
            for (var index = 0; index < accounts.Count; index++)
            {
                var account = accounts[index];
                reportAccounts.Add(new ManagedWardReportAccountEntry(
                    account.Key,
                    account.Value,
                    GetEffectiveWardLimitForAccount(account.Key),
                    WardLimitOverrides.ContainsKey(account.Key)));
            }

            var unresolvedPlayerEntries = CollectUnresolvedWardOwnerCounts(scanEntries);
            var playerAccountMapGapEntries = CollectPlayerAccountMapGapCounts(scanEntries);
            var snapshot = new ManagedWardReportSnapshot(
                Plugin.ModName,
                DateTime.UtcNow,
                GetCurrentWorldName(),
                GetCurrentWorldUid(),
                scanEntries.Count,
                reportAccounts,
                unresolvedPlayerEntries,
                playerAccountMapGapEntries);
            var buildResult = ManagedWardReportBuilder.Build(snapshot);
            reportContents = buildResult.Contents;
            trackedAccounts = buildResult.TrackedAccounts;
            totalWards = buildResult.TotalWards;
            unresolvedOwners = buildResult.UnresolvedOwners;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to build ward report: {exception.Message}");
            return false;
        }
    }

    private static List<ManagedWardScanEntry> BuildManagedWardScanEntries(ZDOMan zdoMan)
    {
        var scannedZdoCount = PrepareManagedWardPrefabScan(zdoMan);
        var scanEntries = new List<ManagedWardScanEntry>(scannedZdoCount);
        for (var index = 0; index < scannedZdoCount; index++)
        {
            var zdo = ManagedWardPrefabScanBuffer[index];
            if (!IsManagedWardZdo(zdo))
            {
                continue;
            }

            var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
            scanEntries.Add(new ManagedWardScanEntry(
                ownerPlayerId,
                NormalizeAccountIdValue(ResolveWardSteamAccountId(zdo, ownerPlayerId, string.Empty))));
        }

        return scanEntries;
    }

    private static List<KeyValuePair<string, int>> CollectReportWardCountsByAccount(IReadOnlyList<ManagedWardScanEntry> scanEntries)
    {
        var countsByAccount = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < scanEntries.Count; index++)
        {
            var accountId = scanEntries[index].AccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                continue;
            }

            countsByAccount[accountId] =
                countsByAccount.TryGetValue(accountId, out var accountCount)
                    ? accountCount + 1
                    : 1;
        }

        return new List<KeyValuePair<string, int>>(countsByAccount);
    }

    private static List<KeyValuePair<long, int>> CollectUnresolvedWardOwnerCounts(IReadOnlyList<ManagedWardScanEntry> scanEntries)
    {
        var unresolvedCountsByCreatorId = new Dictionary<long, int>();
        for (var index = 0; index < scanEntries.Count; index++)
        {
            var scanEntry = scanEntries[index];
            var accountId = scanEntry.AccountId;
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                continue;
            }

            var creatorId = scanEntry.OwnerPlayerId;
            unresolvedCountsByCreatorId[creatorId] =
                unresolvedCountsByCreatorId.TryGetValue(creatorId, out var wardCount)
                    ? wardCount + 1
                    : 1;
        }

        return SortWardCountEntries(unresolvedCountsByCreatorId);
    }

    private static List<KeyValuePair<long, int>> CollectPlayerAccountMapGapCounts(IReadOnlyList<ManagedWardScanEntry> scanEntries)
    {
        var gapCountsByCreatorId = new Dictionary<long, int>();
        var cachedPlayerAccountIds = new Dictionary<long, string>();
        for (var index = 0; index < scanEntries.Count; index++)
        {
            var creatorId = scanEntries[index].OwnerPlayerId;
            if (creatorId == 0L)
            {
                continue;
            }

            var mappedAccountId = GetCachedReportPlayerAccountId(creatorId, cachedPlayerAccountIds);
            if (!string.IsNullOrWhiteSpace(mappedAccountId))
            {
                continue;
            }

            gapCountsByCreatorId[creatorId] =
                gapCountsByCreatorId.TryGetValue(creatorId, out var wardCount)
                    ? wardCount + 1
                    : 1;
        }

        return SortWardCountEntries(gapCountsByCreatorId);
    }

    private static string GetCachedReportPlayerAccountId(long playerId, Dictionary<long, string> cachedPlayerAccountIds)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        if (cachedPlayerAccountIds.TryGetValue(playerId, out var cachedAccountId))
        {
            return cachedAccountId;
        }

        var accountId = GetPlayerAccountId(playerId);
        cachedPlayerAccountIds[playerId] = accountId;
        return accountId;
    }

    private static List<KeyValuePair<long, int>> SortWardCountEntries(Dictionary<long, int> countsByCreatorId)
    {
        var entries = new List<KeyValuePair<long, int>>(countsByCreatorId);
        entries.Sort(static (left, right) =>
        {
            var countCompare = right.Value.CompareTo(left.Value);
            return countCompare != 0
                ? countCompare
                : left.Key.CompareTo(right.Key);
        });

        return entries;
    }
}
