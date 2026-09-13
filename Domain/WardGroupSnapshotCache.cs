using System.Collections.Generic;

namespace STUWard;

internal readonly struct WardGroupSnapshotEntry
{
    internal WardGroupSnapshotEntry(long playerId, long sessionId, long characterUserId, uint characterId, WardGroupIdentity group)
    {
        PlayerId = playerId;
        SessionId = sessionId;
        CharacterUserId = characterUserId;
        CharacterId = characterId;
        Group = group;
    }

    internal long PlayerId { get; }
    internal long SessionId { get; }
    internal long CharacterUserId { get; }
    internal uint CharacterId { get; }
    internal WardGroupIdentity Group { get; }
    internal bool IsValid => PlayerId != 0 && SessionId != 0 && (CharacterUserId != 0 || CharacterId != 0) &&
        Group.IsValid && (Group.Provider == "clan" || Group.Provider == "guilds") &&
        Group.Id.Length <= 128 && Group.Name.Length <= 256;
}

// A full response is tied to a request and a character's current network session.
// Its lifetime starts at the request, so delayed/replayed responses cannot renew trust.
internal sealed class WardGroupSnapshotCache
{
    internal const int MaximumEntries = 1024;
    internal const long LifetimeMilliseconds = 5000;
    private Dictionary<long, WardGroupSnapshotEntry> _entries = new();
    private long _nextRequestId;
    private long _pendingRequestId;
    private long _requestedAt;
    private long _expiresAt;

    internal bool HasPendingRequest(long now) => _pendingRequestId != 0 && now - _requestedAt < 2000;

    internal long BeginRequest(long now)
    {
        _requestedAt = now;
        _pendingRequestId = ++_nextRequestId;
        return _pendingRequestId;
    }

    internal bool TryApply(long requestId, IReadOnlyList<WardGroupSnapshotEntry> entries, long now)
    {
        if (requestId <= 0 || requestId != _pendingRequestId || now < _requestedAt ||
            now - _requestedAt >= LifetimeMilliseconds || entries.Count > MaximumEntries) return false;

        var replacement = new Dictionary<long, WardGroupSnapshotEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (!entry.IsValid || replacement.ContainsKey(entry.PlayerId)) return false;
            replacement.Add(entry.PlayerId, entry);
        }

        _entries = replacement;
        _expiresAt = _requestedAt + LifetimeMilliseconds;
        _pendingRequestId = 0;
        return true;
    }

    internal WardGroupIdentity Get(long playerId, long sessionId, long characterUserId, uint characterId,
        string provider, long now)
    {
        return now < _expiresAt && _entries.TryGetValue(playerId, out var entry) &&
               entry.SessionId == sessionId && entry.CharacterUserId == characterUserId &&
               entry.CharacterId == characterId && entry.Group.Provider == provider
            ? entry.Group : default;
    }

    internal void Clear()
    {
        _entries.Clear();
        _pendingRequestId = 0;
        _expiresAt = 0;
        // Keep the sequence across world changes to reject old responses.
    }
}
