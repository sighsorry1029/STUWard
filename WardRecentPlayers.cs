using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using YamlDotNet.Serialization;

namespace STUWard;

internal readonly struct WardRecentPlayerEntry
{
    internal WardRecentPlayerEntry(
        long playerId,
        string name,
        string guildName,
        string accountId,
        bool isOnline,
        long lastSeenUtcTicks)
    {
        PlayerId = playerId;
        Name = name ?? string.Empty;
        GuildName = guildName ?? string.Empty;
        AccountId = accountId ?? string.Empty;
        IsOnline = isOnline;
        LastSeenUtcTicks = lastSeenUtcTicks;
    }

    internal long PlayerId { get; }
    internal string Name { get; }
    internal string GuildName { get; }
    internal string AccountId { get; }
    internal bool IsOnline { get; }
    internal long LastSeenUtcTicks { get; }
}

internal readonly struct WardPlayerActivityEntry
{
    internal WardPlayerActivityEntry(long playerId, bool isOnline, long lastSeenUtcTicks)
    {
        PlayerId = playerId;
        IsOnline = isOnline;
        LastSeenUtcTicks = lastSeenUtcTicks;
    }

    internal long PlayerId { get; }
    internal bool IsOnline { get; }
    internal long LastSeenUtcTicks { get; }
}

internal readonly struct WardRecentPlayersSnapshot
{
    internal WardRecentPlayersSnapshot(
        long requestId,
        ZDOID wardZdoId,
        IReadOnlyList<WardRecentPlayerEntry> players,
        IReadOnlyList<WardPlayerActivityEntry> registeredActivity)
    {
        RequestId = requestId;
        WardZdoId = wardZdoId;
        Players = players ?? Array.Empty<WardRecentPlayerEntry>();
        RegisteredActivity = registeredActivity ?? Array.Empty<WardPlayerActivityEntry>();
    }

    internal long RequestId { get; }
    internal ZDOID WardZdoId { get; }
    internal IReadOnlyList<WardRecentPlayerEntry> Players { get; }
    internal IReadOnlyList<WardPlayerActivityEntry> RegisteredActivity { get; }
}

internal static class WardRecentPlayers
{
    private const string RequestSnapshotRpc = "STUWard_RequestRecentPlayers";
    private const string RequestAddRpc = "STUWard_RequestAddRecentPlayer";
    private const string ReceiveSnapshotRpc = "STUWard_ReceiveRecentPlayers";
    private const string FileNamePrefix = "STUWard.RecentPlayers.";
    private const string FileNameSuffix = ".yml";
    private const int FormatVersion = 1;
    private const int MaxStoredPlayers = 4096;
    private const int MaxSnapshotPlayers = 1024;
    private const int MaxPlayerNameLength = 64;
    private const int MaxGuildNameLength = 256;
    private const int MaxAccountIdLength = 128;
    private const int MaxYamlBytes = 2 * 1024 * 1024;
    private const int MaxResponseBytes = 256 * 1024;
    private const int MaxRequestsPerWindow = 12;
    private static readonly TimeSpan RecentLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(2);
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithDuplicateKeyChecking()
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();
    private static readonly Dictionary<long, StoredPlayer> PlayersById = new();
    private static readonly HashSet<long> OnlinePlayerIds = new();
    private static readonly Dictionary<long, ServerSessionIdentity> AuthenticatedIdentitiesBySender = new();
    private static readonly Dictionary<long, RequestBudget> RequestBudgetsBySender = new();

    private static bool _rpcsRegistered;
    private static long _nextRequestId;
    private static long _loadedWorldUid;
    private static bool _serverStoreLoaded;
    private static bool _dirty;
    private static DateTime _saveAfterUtc = DateTime.MaxValue;
    private static DateTime _nextPruneUtc = DateTime.MinValue;

    internal static event Action<WardRecentPlayersSnapshot>? SnapshotReceived;

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register<ZPackage>(RequestSnapshotRpc, HandleRequestSnapshot);
        routedRpc.Register<ZPackage>(RequestAddRpc, HandleRequestAdd);
        routedRpc.Register<ZPackage>(ReceiveSnapshotRpc, HandleReceiveSnapshot);
        _rpcsRegistered = true;
    }

    internal static void Update()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        EnsureServerStoreLoaded();
        var now = DateTime.UtcNow;
        if (now >= _nextPruneUtc)
        {
            _nextPruneUtc = now.Add(PruneInterval);
            RefreshAuthenticatedLastSeen(now);
            PruneAndBound(now);
        }

        if (_dirty && now >= _saveAfterUtc)
        {
            SaveServerStore();
        }
    }

    internal static void ResetRuntimeState()
    {
        FlushServerStore();
        _rpcsRegistered = false;
        _nextRequestId = 0L;
        _loadedWorldUid = 0L;
        _serverStoreLoaded = false;
        _dirty = false;
        _saveAfterUtc = DateTime.MaxValue;
        _nextPruneUtc = DateTime.MinValue;
        PlayersById.Clear();
        OnlinePlayerIds.Clear();
        AuthenticatedIdentitiesBySender.Clear();
        RequestBudgetsBySender.Clear();
    }

    internal static void Shutdown()
    {
        FlushServerStore();
    }

    internal static void RememberAuthenticatedIdentity(ServerSessionIdentity identity)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || identity.PlayerId == 0L)
        {
            return;
        }

        if (AuthenticatedIdentitiesBySender.TryGetValue(identity.SenderUid, out var previousIdentity) &&
            previousIdentity.PlayerId != identity.PlayerId)
        {
            AuthenticatedIdentitiesBySender[identity.SenderUid] = identity;
            RefreshOnlineState(previousIdentity.PlayerId);
        }
        else
        {
            AuthenticatedIdentitiesBySender[identity.SenderUid] = identity;
        }
        if (!EnsureServerStoreLoaded())
        {
            return;
        }
        var now = DateTime.UtcNow;
        var name = NormalizeText(identity.PlayerName, MaxPlayerNameLength);
        var accountId = NormalizeText(identity.AccountId, MaxAccountIdLength);
        if (PlayersById.TryGetValue(identity.PlayerId, out var existing))
        {
            existing.Name = !string.IsNullOrWhiteSpace(name) ? name : existing.Name;
            existing.AccountId = !string.IsNullOrWhiteSpace(accountId) ? accountId : existing.AccountId;
            existing.LastSeenUtcTicks = now.Ticks;
        }
        else
        {
            PlayersById[identity.PlayerId] = new StoredPlayer
            {
                PlayerId = identity.PlayerId,
                Name = name,
                AccountId = accountId,
                LastSeenUtcTicks = now.Ticks
            };
        }

        OnlinePlayerIds.Add(identity.PlayerId);
        MarkDirty(now);
    }

    internal static void ForgetAuthenticatedIdentity(ServerSessionIdentity identity)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        AuthenticatedIdentitiesBySender.Remove(identity.SenderUid);
        RequestBudgetsBySender.Remove(identity.SenderUid);
        if (identity.PlayerId == 0L)
        {
            return;
        }

        if (!EnsureServerStoreLoaded())
        {
            return;
        }
        var now = DateTime.UtcNow;
        var name = NormalizeText(identity.PlayerName, MaxPlayerNameLength);
        var accountId = NormalizeText(identity.AccountId, MaxAccountIdLength);
        if (!PlayersById.TryGetValue(identity.PlayerId, out var player))
        {
            player = new StoredPlayer { PlayerId = identity.PlayerId };
            PlayersById[identity.PlayerId] = player;
        }

        player.Name = !string.IsNullOrWhiteSpace(name) ? name : player.Name;
        player.AccountId = !string.IsNullOrWhiteSpace(accountId) ? accountId : player.AccountId;
        player.LastSeenUtcTicks = now.Ticks;
        RefreshOnlineState(identity.PlayerId);
        MarkDirty(now);
    }

    internal static void RememberLocalServerPlayer(Player? player)
    {
        if (player == null || player != Player.m_localPlayer || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var playerId = player.GetPlayerID();
        var senderUid = player.GetOwner();
        if (playerId == 0L)
        {
            return;
        }

        if (senderUid == 0L)
        {
            senderUid = ZDOMan.GetSessionID();
        }

        RememberAuthenticatedIdentity(new ServerSessionIdentity(
            senderUid,
            ZDOID.None,
            playerId,
            WardOwnership.GetPlayerAccountId(player),
            player.GetPlayerName()));
    }

    internal static long RequestSnapshot(PrivateArea? ward)
    {
        return SendRequest(ward, 0L, RequestSnapshotRpc);
    }

    internal static long RequestAdd(PrivateArea? ward, long playerId)
    {
        if (playerId == 0L)
        {
            return 0L;
        }

        return SendRequest(ward, playerId, RequestAddRpc);
    }

    private static long SendRequest(PrivateArea? ward, long targetPlayerId, string method)
    {
        RegisterRpcs();
        var zdo = WardPrivateAreaSafeAccess.GetZdo(ward);
        if (zdo == null || !zdo.IsValid() || ZRoutedRpc.instance == null)
        {
            return 0L;
        }

        var requestId = NextRequestId();
        var request = new ZPackage();
        request.Write(requestId);
        request.Write(zdo.m_uid);
        if (targetPlayerId != 0L)
        {
            request.Write(targetPlayerId);
        }

        return WardOwnership.TryInvokeServerRoutedRpc(method, request) ? requestId : 0L;
    }

    private static void HandleRequestSnapshot(long sender, ZPackage? request)
    {
        if (!TryConsumeRequestBudget(sender) ||
            !TryReadWardRequest(request, out var requestId, out var wardZdoId) ||
            !TryAuthorize(sender, wardZdoId, out var zdo, out _))
        {
            return;
        }

        SendSnapshot(sender, requestId, zdo);
    }

    private static void HandleRequestAdd(long sender, ZPackage? request)
    {
        if (!TryConsumeRequestBudget(sender) ||
            !TryReadAddRequest(request, out var requestId, out var wardZdoId, out var targetPlayerId) ||
            !TryAuthorize(sender, wardZdoId, out var zdo, out _))
        {
            return;
        }

        if (!TryGetEligibleTarget(targetPlayerId, DateTime.UtcNow, out var target))
        {
            SendSnapshot(sender, requestId, zdo);
            return;
        }

        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        if (ownerPlayerId != targetPlayerId &&
            !WardPrivateAreaSafeAccess.IsPlayerPermitted(zdo, targetPlayerId))
        {
            WardOwnership.RefreshServerPlayerAccountIdForResolvedPlayer(targetPlayerId, target.AccountId);
            if (!WardOwnership.TryClaimManagedWardMutationOwnership(zdo) ||
                !WardPrivateAreaSafeAccess.AddPermittedPlayer(zdo, targetPlayerId, GetDisplayName(target), out var added))
            {
                SendSnapshot(sender, requestId, zdo);
                return;
            }

            if (added)
            {
                WardOwnership.CompleteAuthoritativePermittedMutation(zdo);
            }
        }

        // Successful retries and targets that became registered since the click are
        // acknowledged with the same request id and the current canonical snapshot.
        SendSnapshot(sender, requestId, zdo);
    }

    private static bool TryAuthorize(long sender, ZDOID wardZdoId, out ZDO zdo, out long requesterId)
    {
        zdo = null!;
        requesterId = 0L;
        return WardOwnership.TryResolveAuthoritativeManagedWardRequest(sender, wardZdoId, out zdo, out requesterId) &&
               WardAccess.HasManagedWardTrust(zdo, requesterId);
    }

    private static void SendSnapshot(long receiverUid, long requestId, ZDO wardZdo)
    {
        if (receiverUid == 0L || requestId <= 0L)
        {
            return;
        }

        if (!EnsureServerStoreLoaded())
        {
            return;
        }
        var nowUtc = DateTime.UtcNow;
        var permittedPlayerIds = WardPrivateAreaSafeAccess.GetPermittedPlayerIds(wardZdo);
        var permittedPlayerIdSet = new HashSet<long>(permittedPlayerIds);
        var entries = BuildSnapshot(wardZdo, permittedPlayerIdSet, nowUtc);
        var registeredActivity = BuildRegisteredActivity(permittedPlayerIds);
        var includedCount = GetSnapshotPrefixCount(requestId, wardZdo.m_uid, entries, registeredActivity);
        var response = CreateSnapshotResponse(
            requestId,
            wardZdo.m_uid,
            entries,
            includedCount,
            registeredActivity);
        var bytes = response.GetArray();
        if (bytes.Length > MaxResponseBytes)
        {
            Plugin.Log.LogWarning(
                $"Recent-player response for ward {wardZdo.m_uid} exceeded the size limit; omitting unregistered players.");
            response = CreateSnapshotResponse(requestId, wardZdo.m_uid, entries, 0, registeredActivity);
            bytes = response.GetArray();
        }

        if (bytes.Length > MaxResponseBytes)
        {
            Plugin.Log.LogError($"Registered-player activity response for ward {wardZdo.m_uid} exceeded the size limit.");
            return;
        }

        ZRoutedRpc.instance?.InvokeRoutedRPC(receiverUid, ReceiveSnapshotRpc, response);
    }

    private static int GetSnapshotPrefixCount(
        long requestId,
        ZDOID wardZdoId,
        IReadOnlyList<WardRecentPlayerEntry> entries,
        IReadOnlyList<WardPlayerActivityEntry> registeredActivity)
    {
        var header = new ZPackage();
        WriteSnapshotHeader(header, requestId, wardZdoId, 0, registeredActivity.Count);
        var encodedBytes = header.GetArray().Length;
        if (encodedBytes > MaxResponseBytes)
        {
            return 0;
        }

        for (var index = 0; index < registeredActivity.Count; index++)
        {
            var encodedActivity = new ZPackage();
            WriteActivityEntry(encodedActivity, registeredActivity[index]);
            var activityBytes = encodedActivity.GetArray().Length;
            if (activityBytes > MaxResponseBytes - encodedBytes)
            {
                return 0;
            }

            encodedBytes += activityBytes;
        }

        var includedCount = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var encodedEntry = new ZPackage();
            WriteSnapshotEntry(encodedEntry, entries[index]);
            var entryBytes = encodedEntry.GetArray().Length;
            if (entryBytes > MaxResponseBytes - encodedBytes)
            {
                break;
            }

            encodedBytes += entryBytes;
            includedCount++;
        }

        return includedCount;
    }

    private static ZPackage CreateSnapshotResponse(
        long requestId,
        ZDOID wardZdoId,
        IReadOnlyList<WardRecentPlayerEntry> entries,
        int count,
        IReadOnlyList<WardPlayerActivityEntry> registeredActivity)
    {
        var response = new ZPackage();
        WriteSnapshotHeader(response, requestId, wardZdoId, count, registeredActivity.Count);
        for (var index = 0; index < count; index++)
        {
            WriteSnapshotEntry(response, entries[index]);
        }

        for (var index = 0; index < registeredActivity.Count; index++)
        {
            WriteActivityEntry(response, registeredActivity[index]);
        }

        return response;
    }

    private static void WriteSnapshotHeader(
        ZPackage response,
        long requestId,
        ZDOID wardZdoId,
        int unregisteredCount,
        int registeredActivityCount)
    {
        response.Write(requestId);
        response.Write(wardZdoId);
        response.Write(unregisteredCount);
        response.Write(registeredActivityCount);
    }

    private static void WriteSnapshotEntry(ZPackage response, WardRecentPlayerEntry entry)
    {
        response.Write(entry.PlayerId);
        response.Write(entry.Name);
        response.Write(entry.GuildName);
        response.Write(entry.AccountId);
        response.Write(entry.IsOnline);
        response.Write(entry.LastSeenUtcTicks);
    }

    private static void WriteActivityEntry(ZPackage response, WardPlayerActivityEntry entry)
    {
        response.Write(entry.PlayerId);
        response.Write(entry.IsOnline);
        response.Write(entry.LastSeenUtcTicks);
    }

    private static List<WardRecentPlayerEntry> BuildSnapshot(
        ZDO wardZdo,
        HashSet<long> permittedPlayerIds,
        DateTime nowUtc)
    {
        PruneAndBound(nowUtc);
        var ownerId = wardZdo.GetLong(ZDOVars.s_creator, 0L);
        var candidates = new List<SnapshotCandidate>();
        foreach (var player in PlayersById.Values)
        {
            var online = OnlinePlayerIds.Contains(player.PlayerId);
            if (player.PlayerId == 0L || player.PlayerId == ownerId ||
                permittedPlayerIds.Contains(player.PlayerId) ||
                (!online && !IsRecent(player, nowUtc)))
            {
                continue;
            }

            candidates.Add(new SnapshotCandidate(
                player,
                GetDisplayName(player),
                GetDisplayAccountId(player),
                online,
                player.LastSeenUtcTicks));
        }

        candidates.Sort(CompareCandidates);
        if (candidates.Count > MaxSnapshotPlayers)
        {
            candidates.RemoveRange(MaxSnapshotPlayers, candidates.Count - MaxSnapshotPlayers);
        }

        var entries = new List<WardRecentPlayerEntry>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            entries.Add(new WardRecentPlayerEntry(
                candidate.Player.PlayerId,
                candidate.Name,
                GetAuthoritativeGuildName(candidate.Player),
                candidate.AccountId,
                candidate.IsOnline,
                candidate.LastSeenUtcTicks));
        }

        return entries;
    }

    private static List<WardPlayerActivityEntry> BuildRegisteredActivity(IReadOnlyList<long> permittedPlayerIds)
    {
        var activity = new List<WardPlayerActivityEntry>(
            Math.Min(permittedPlayerIds.Count, MaxSnapshotPlayers));
        for (var index = 0; index < permittedPlayerIds.Count && activity.Count < MaxSnapshotPlayers; index++)
        {
            var playerId = permittedPlayerIds[index];
            if (playerId == 0L || !PlayersById.TryGetValue(playerId, out var player))
            {
                continue;
            }

            activity.Add(new WardPlayerActivityEntry(
                playerId,
                OnlinePlayerIds.Contains(playerId),
                player.LastSeenUtcTicks));
        }

        return activity;
    }

    private static int CompareCandidates(SnapshotCandidate left, SnapshotCandidate right)
    {
        var onlineComparison = right.IsOnline.CompareTo(left.IsOnline);
        if (onlineComparison != 0)
        {
            return onlineComparison;
        }

        var seenComparison = right.LastSeenUtcTicks.CompareTo(left.LastSeenUtcTicks);
        if (seenComparison != 0)
        {
            return seenComparison;
        }

        var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        return nameComparison != 0 ? nameComparison : left.PlayerId.CompareTo(right.PlayerId);
    }

    private static void HandleReceiveSnapshot(long sender, ZPackage? response)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || response == null)
        {
            return;
        }

        try
        {
            if (response.GetArray().Length > MaxResponseBytes)
            {
                return;
            }

            var requestId = response.ReadLong();
            var wardZdoId = response.ReadZDOID();
            var unregisteredCount = response.ReadInt();
            var registeredActivityCount = response.ReadInt();
            if (requestId <= 0L || wardZdoId.IsNone() ||
                unregisteredCount < 0 || unregisteredCount > MaxSnapshotPlayers ||
                registeredActivityCount < 0 || registeredActivityCount > MaxSnapshotPlayers)
            {
                return;
            }

            var maximumLastSeenUtc = DateTime.UtcNow.AddMinutes(5);
            var entries = new List<WardRecentPlayerEntry>(unregisteredCount);
            for (var index = 0; index < unregisteredCount; index++)
            {
                if (!TryReadSnapshotEntry(response, maximumLastSeenUtc, out var entry))
                {
                    return;
                }

                entries.Add(entry);
            }

            var registeredActivity = new List<WardPlayerActivityEntry>(registeredActivityCount);
            for (var index = 0; index < registeredActivityCount; index++)
            {
                if (!TryReadActivityEntry(response, maximumLastSeenUtc, out var entry))
                {
                    return;
                }

                registeredActivity.Add(entry);
            }

            SnapshotReceived?.Invoke(new WardRecentPlayersSnapshot(
                requestId,
                wardZdoId,
                entries,
                registeredActivity));
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Rejected malformed recent-player response: {exception.Message}");
        }
    }

    private static bool TryReadSnapshotEntry(
        ZPackage response,
        DateTime maximumLastSeenUtc,
        out WardRecentPlayerEntry entry)
    {
        entry = default;
        var playerId = response.ReadLong();
        var name = response.ReadString();
        var guildName = response.ReadString();
        var accountId = response.ReadString();
        var online = response.ReadBool();
        var lastSeenTicks = response.ReadLong();
        if (playerId == 0L || name.Length > MaxPlayerNameLength ||
            guildName.Length > MaxGuildNameLength ||
            accountId.Length > MaxAccountIdLength ||
            !IsPlausibleLastSeen(lastSeenTicks, maximumLastSeenUtc))
        {
            return false;
        }

        entry = new WardRecentPlayerEntry(
            playerId,
            name,
            guildName,
            accountId,
            online,
            lastSeenTicks);
        return true;
    }

    private static bool TryReadActivityEntry(
        ZPackage response,
        DateTime maximumLastSeenUtc,
        out WardPlayerActivityEntry entry)
    {
        entry = default;
        var playerId = response.ReadLong();
        var online = response.ReadBool();
        var lastSeenTicks = response.ReadLong();
        if (playerId == 0L || lastSeenTicks <= 0L || !IsPlausibleLastSeen(lastSeenTicks, maximumLastSeenUtc))
        {
            return false;
        }

        entry = new WardPlayerActivityEntry(playerId, online, lastSeenTicks);
        return true;
    }

    private static bool TryReadWardRequest(ZPackage? request, out long requestId, out ZDOID wardZdoId)
    {
        requestId = 0L;
        wardZdoId = ZDOID.None;
        if (request == null)
        {
            return false;
        }

        try
        {
            requestId = request.ReadLong();
            wardZdoId = request.ReadZDOID();
            return requestId > 0L && !wardZdoId.IsNone();
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadAddRequest(ZPackage? request, out long requestId, out ZDOID wardZdoId, out long targetPlayerId)
    {
        targetPlayerId = 0L;
        if (!TryReadWardRequest(request, out requestId, out wardZdoId))
        {
            return false;
        }

        try
        {
            targetPlayerId = request!.ReadLong();
            return targetPlayerId != 0L;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetEligibleTarget(long playerId, DateTime nowUtc, out StoredPlayer player)
    {
        if (!EnsureServerStoreLoaded())
        {
            player = null!;
            return false;
        }
        if (playerId != 0L && PlayersById.TryGetValue(playerId, out player!))
        {
            return OnlinePlayerIds.Contains(playerId) || IsRecent(player, nowUtc);
        }

        player = null!;
        return false;
    }

    private static bool IsRecent(StoredPlayer player, DateTime nowUtc)
    {
        return IsPlausibleLastSeen(player.LastSeenUtcTicks, nowUtc) &&
               nowUtc.Ticks - player.LastSeenUtcTicks <= RecentLifetime.Ticks;
    }

    private static long NextRequestId()
    {
        unchecked
        {
            _nextRequestId++;
            if (_nextRequestId <= 0L)
            {
                _nextRequestId = 1L;
            }

            return _nextRequestId;
        }
    }

    private static bool EnsureServerStoreLoaded()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return false;
        }

        var worldUid = ZNet.instance.GetWorldUID();
        if (worldUid == 0L)
        {
            return false;
        }

        if (_serverStoreLoaded && _loadedWorldUid == worldUid)
        {
            return true;
        }

        if (_serverStoreLoaded)
        {
            FlushServerStore();
        }

        PlayersById.Clear();
        OnlinePlayerIds.Clear();
        _dirty = false;
        _loadedWorldUid = worldUid;
        _serverStoreLoaded = true;
        _nextPruneUtc = DateTime.UtcNow.Add(PruneInterval);
        LoadServerStore();
        ReconcileAuthenticatedIdentities();
        return true;
    }

    private static void LoadServerStore()
    {
        var path = GetFilePath(_loadedWorldUid);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length < 0L || info.Length > MaxYamlBytes)
            {
                Plugin.Log.LogWarning($"Recent-player file '{path}' exceeds the size limit; starting empty.");
                return;
            }

            var data = Deserializer.Deserialize<RecentPlayersYaml>(File.ReadAllText(path));
            if (data == null || data.FormatVersion != FormatVersion || data.WorldUid != _loadedWorldUid || data.Players == null)
            {
                Plugin.Log.LogWarning($"Recent-player file '{path}' has an unsupported format or world id; starting empty.");
                return;
            }

            if (data.Players.Count > MaxStoredPlayers)
            {
                Plugin.Log.LogWarning($"Recent-player file '{path}' has too many entries; starting empty.");
                return;
            }

            var loadedAtUtc = DateTime.UtcNow;
            var maximumLastSeenUtc = loadedAtUtc.AddMinutes(5);
            for (var index = 0; index < data.Players.Count; index++)
            {
                var player = data.Players[index];
                if (player == null || player.PlayerId == 0L ||
                    !IsPlausibleLastSeen(player.LastSeenUtcTicks, maximumLastSeenUtc) ||
                    (player.Name?.Length ?? 0) > MaxPlayerNameLength ||
                    (player.AccountId?.Length ?? 0) > MaxAccountIdLength)
                {
                    Plugin.Log.LogWarning($"Recent-player file '{path}' is invalid; starting empty.");
                    PlayersById.Clear();
                    return;
                }

                PlayersById[player.PlayerId] = new StoredPlayer
                {
                    PlayerId = player.PlayerId,
                    Name = player.Name ?? string.Empty,
                    AccountId = player.AccountId ?? string.Empty,
                    // Small forward skews can happen when the host clock is corrected
                    // between saves. Keep the record, but never retain a future time.
                    LastSeenUtcTicks = Math.Min(player.LastSeenUtcTicks, loadedAtUtc.Ticks)
                };
            }

            PruneAndBound(loadedAtUtc);
        }
        catch (Exception exception)
        {
            PlayersById.Clear();
            Plugin.Log.LogWarning($"Failed to read recent-player file '{path}'; starting empty: {exception.Message}");
        }
    }

    private static void SaveServerStore()
    {
        if (!_serverStoreLoaded || _loadedWorldUid == 0L)
        {
            return;
        }

        PruneAndBound(DateTime.UtcNow);
        var data = new RecentPlayersYaml
        {
            FormatVersion = FormatVersion,
            WorldUid = _loadedWorldUid,
            Players = new List<StoredPlayer>(PlayersById.Values)
        };
        data.Players.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));

        try
        {
            var yaml = Serializer.Serialize(data);
            if (System.Text.Encoding.UTF8.GetByteCount(yaml) > MaxYamlBytes)
            {
                Plugin.Log.LogWarning("Recent-player data exceeded the file size limit and was not saved.");
                _saveAfterUtc = DateTime.UtcNow.Add(SaveRetryDelay);
                return;
            }

            var path = GetFilePath(_loadedWorldUid);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, yaml);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            _dirty = false;
            _saveAfterUtc = DateTime.MaxValue;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to save recent-player data: {exception.Message}");
            _saveAfterUtc = DateTime.UtcNow.Add(SaveRetryDelay);
        }
    }

    private static void FlushServerStore()
    {
        if (!_serverStoreLoaded)
        {
            return;
        }

        RefreshAuthenticatedLastSeen(DateTime.UtcNow);

        if (_dirty)
        {
            SaveServerStore();
        }
    }

    private static void RefreshAuthenticatedLastSeen(DateTime nowUtc)
    {
        var changed = false;
        foreach (var identity in AuthenticatedIdentitiesBySender.Values)
        {
            if (identity.PlayerId == 0L ||
                !PlayersById.TryGetValue(identity.PlayerId, out var player) ||
                player.LastSeenUtcTicks == nowUtc.Ticks)
            {
                continue;
            }

            player.LastSeenUtcTicks = nowUtc.Ticks;
            changed = true;
        }

        if (changed)
        {
            MarkDirty(nowUtc);
        }
    }

    private static void PruneAndBound(DateTime nowUtc)
    {
        List<long>? remove = null;
        foreach (var entry in PlayersById)
        {
            if (!OnlinePlayerIds.Contains(entry.Key) && !IsRecent(entry.Value, nowUtc))
            {
                remove ??= new List<long>();
                remove.Add(entry.Key);
            }
        }

        if (remove != null)
        {
            for (var index = 0; index < remove.Count; index++)
            {
                PlayersById.Remove(remove[index]);
            }

            MarkDirty(nowUtc);
        }

        if (PlayersById.Count <= MaxStoredPlayers)
        {
            return;
        }

        var candidates = new List<StoredPlayer>(PlayersById.Values);
        candidates.Sort((left, right) =>
        {
            var online = OnlinePlayerIds.Contains(right.PlayerId).CompareTo(OnlinePlayerIds.Contains(left.PlayerId));
            return online != 0 ? online : right.LastSeenUtcTicks.CompareTo(left.LastSeenUtcTicks);
        });
        for (var index = MaxStoredPlayers; index < candidates.Count; index++)
        {
            if (!OnlinePlayerIds.Contains(candidates[index].PlayerId))
            {
                PlayersById.Remove(candidates[index].PlayerId);
            }
        }

        MarkDirty(nowUtc);
    }

    private static void MarkDirty(DateTime nowUtc)
    {
        _dirty = true;
        var candidate = nowUtc.Add(SaveDebounce);
        if (_saveAfterUtc == DateTime.MaxValue || candidate < _saveAfterUtc)
        {
            _saveAfterUtc = candidate;
        }
    }

    private static string GetFilePath(long worldUid)
    {
        return Path.Combine(Paths.ConfigPath, FileNamePrefix + worldUid + FileNameSuffix);
    }

    private static string GetDisplayName(StoredPlayer player)
    {
        var name = NormalizeText(player.Name, MaxPlayerNameLength);
        return string.IsNullOrWhiteSpace(name) ? player.PlayerId.ToString() : name;
    }

    private static string GetAuthoritativeGuildName(StoredPlayer player)
    {
        var accountId = WardOwnership.NormalizeAccountIdValue(player.AccountId);
        var playerName = NormalizeText(player.Name, MaxPlayerNameLength);
        if (!GuildsCompat.TryResolveCachedAuthoritativeGuildIdentity(
                player.PlayerId,
                accountId,
                playerName,
                out var guild) ||
            guild.Id == 0)
        {
            return string.Empty;
        }

        return NormalizeText(guild.Name, MaxGuildNameLength);
    }

    private static string GetDisplayAccountId(StoredPlayer player)
    {
        return NormalizeText(
            WardOwnership.NormalizeAccountIdValue(player.AccountId),
            MaxAccountIdLength);
    }

    private static string NormalizeText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > maxLength)
        {
            normalized = normalized.Substring(0, maxLength);
        }

        return normalized;
    }

    private static bool IsPlausibleLastSeen(long ticks, DateTime maximumUtc)
    {
        return ticks >= DateTime.MinValue.Ticks &&
               ticks <= DateTime.MaxValue.Ticks &&
               ticks <= maximumUtc.Ticks;
    }

    private static void ReconcileAuthenticatedIdentities()
    {
        if (!_serverStoreLoaded)
        {
            return;
        }

        var identities = new List<ServerSessionIdentity>(AuthenticatedIdentitiesBySender.Values);
        for (var index = 0; index < identities.Count; index++)
        {
            RememberAuthenticatedIdentity(identities[index]);
        }
    }

    private static void RefreshOnlineState(long playerId)
    {
        foreach (var identity in AuthenticatedIdentitiesBySender.Values)
        {
            if (identity.PlayerId == playerId)
            {
                OnlinePlayerIds.Add(playerId);
                return;
            }
        }

        OnlinePlayerIds.Remove(playerId);
    }

    private static bool TryConsumeRequestBudget(long sender)
    {
        if (sender == 0L)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!RequestBudgetsBySender.TryGetValue(sender, out var budget) || now - budget.WindowStartedUtc >= RequestWindow)
        {
            RequestBudgetsBySender[sender] = new RequestBudget(now, 1);
            return true;
        }

        if (budget.Count >= MaxRequestsPerWindow)
        {
            return false;
        }

        RequestBudgetsBySender[sender] = new RequestBudget(budget.WindowStartedUtc, budget.Count + 1);
        return true;
    }

    private readonly struct SnapshotCandidate
    {
        internal SnapshotCandidate(
            StoredPlayer player,
            string name,
            string accountId,
            bool isOnline,
            long lastSeenUtcTicks)
        {
            Player = player;
            Name = name;
            AccountId = accountId;
            IsOnline = isOnline;
            LastSeenUtcTicks = lastSeenUtcTicks;
        }

        internal StoredPlayer Player { get; }
        internal long PlayerId => Player.PlayerId;
        internal string Name { get; }
        internal string AccountId { get; }
        internal bool IsOnline { get; }
        internal long LastSeenUtcTicks { get; }
    }

    private readonly struct RequestBudget
    {
        internal RequestBudget(DateTime windowStartedUtc, int count)
        {
            WindowStartedUtc = windowStartedUtc;
            Count = count;
        }

        internal DateTime WindowStartedUtc { get; }
        internal int Count { get; }
    }

    private sealed class RecentPlayersYaml
    {
        public RecentPlayersYaml()
        {
        }

        [YamlMember(Alias = "format_version")]
        public int FormatVersion { get; set; }

        [YamlMember(Alias = "world_uid")]
        public long WorldUid { get; set; }

        [YamlMember(Alias = "players")]
        public List<StoredPlayer>? Players { get; set; }
    }

    private sealed class StoredPlayer
    {
        public StoredPlayer()
        {
        }

        [YamlMember(Alias = "player_id")]
        public long PlayerId { get; set; }

        [YamlMember(Alias = "name")]
        public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "account_id")]
        public string AccountId { get; set; } = string.Empty;

        [YamlMember(Alias = "last_seen_utc_ticks")]
        public long LastSeenUtcTicks { get; set; }
    }
}
