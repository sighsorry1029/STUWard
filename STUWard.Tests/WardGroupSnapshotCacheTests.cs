using Xunit;

namespace STUWard;

public sealed class WardGroupSnapshotCacheTests
{
    private static WardGroupSnapshotEntry Member(long player = 20, string provider = "clan", string id = "primary") =>
        new(player, 30, 40, 50, new WardGroupIdentity(provider, id, "Group"));

    private static WardGroupIdentity Get(WardGroupSnapshotCache cache, long now = 200, long session = 30,
        long characterUser = 40, uint character = 50, string provider = "clan") =>
        cache.Get(20, session, characterUser, character, provider, now);

    [Theory]
    [InlineData("clan")]
    [InlineData("guilds")]
    public void Authenticated_projection_grants_unregistered_member_and_full_replacement_revokes(string provider)
    {
        var cache = new WardGroupSnapshotCache();
        Assert.True(cache.TryApply(cache.BeginRequest(100), new[] { Member(provider: provider) }, 200));
        var subject = new ManagedWardAccessSubject(1, Member(provider: provider).Group, false);
        Assert.True(ManagedWardAccessPolicy.CanAccess(new(20, Get(cache, provider: provider), false), subject));
        Assert.True(cache.TryApply(cache.BeginRequest(300), Array.Empty<WardGroupSnapshotEntry>(), 400));
        Assert.False(ManagedWardAccessPolicy.CanAccess(new(20, Get(cache, 400, provider: provider), false), subject));
        Assert.True(ManagedWardAccessPolicy.CanAccess(new(20, Get(cache, 400, provider: provider), false),
            new ManagedWardAccessSubject(1, subject.WardGroup, true)));
    }

    [Fact]
    public void Reconnect_character_and_provider_must_all_match()
    {
        var cache = new WardGroupSnapshotCache();
        Assert.True(cache.TryApply(cache.BeginRequest(100), new[] { Member() }, 200));
        Assert.True(Get(cache).IsValid);
        Assert.False(Get(cache, session: 31).IsValid);
        Assert.False(Get(cache, characterUser: 41).IsValid);
        Assert.False(Get(cache, character: 51).IsValid);
        Assert.False(Get(cache, provider: "guilds").IsValid);
    }

    [Fact]
    public void Delayed_and_duplicate_responses_cannot_extend_permission_lifetime()
    {
        var cache = new WardGroupSnapshotCache();
        var request = cache.BeginRequest(100);
        Assert.True(cache.TryApply(request, new[] { Member() }, 4999));
        Assert.True(Get(cache, 5099).IsValid);
        Assert.False(cache.TryApply(request, new[] { Member() }, 5099));
        Assert.False(Get(cache, 5100).IsValid);
        request = cache.BeginRequest(5200);
        Assert.False(cache.TryApply(request, new[] { Member() }, 10200));
        Assert.False(Get(cache, 10200).IsValid);
    }

    [Fact]
    public void Superseded_and_previous_world_responses_cannot_restore_membership()
    {
        var cache = new WardGroupSnapshotCache();
        var old = cache.BeginRequest(100);
        var current = cache.BeginRequest(2200);
        Assert.False(cache.TryApply(old, new[] { Member() }, 2300));
        Assert.True(cache.TryApply(current, Array.Empty<WardGroupSnapshotEntry>(), 2300));
        cache.Clear();
        Assert.NotEqual(current, cache.BeginRequest(2400));
        Assert.False(cache.TryApply(current, new[] { Member() }, 2500));
        Assert.False(Get(cache, 2500).IsValid);
    }

    [Fact]
    public void Invalid_or_duplicate_rows_do_not_partly_authorize_a_response()
    {
        var cache = new WardGroupSnapshotCache();
        var request = cache.BeginRequest(100);
        Assert.False(cache.TryApply(request, new[] { Member(), Member() }, 200));
        Assert.False(Get(cache).IsValid);
        Assert.False(cache.TryApply(request, new[] { Member(), Member(21, "unknown") }, 200));
        Assert.False(cache.TryApply(request, new[] { Member(), Member(21, id: "") }, 200));
        Assert.False(cache.TryApply(request, Enumerable.Range(0, WardGroupSnapshotCache.MaximumEntries + 1)
            .Select(i => Member(i + 1)).ToArray(), 200));
        Assert.False(Get(cache).IsValid);
    }
}
