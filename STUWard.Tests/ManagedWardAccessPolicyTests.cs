namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class ManagedWardAccessPolicyTests
{
    [Theory]
    [InlineData(42L, 42L, false, "", "", false)]
    [InlineData(100L, 42L, true, "", "", false)]
    [InlineData(100L, 42L, false, "7", "7", false)]
    [InlineData(100L, 42L, false, "", "", true)]
    public void CanAccess_grants_full_trust_for_every_supported_trust_source(
        long actorPlayerId,
        long ownerPlayerId,
        bool isAdminDebug,
        string playerGroupId,
        string wardGroupId,
        bool permitted)
    {
        var actor = new ManagedWardAccessActor(
            actorPlayerId,
            Group("guilds", playerGroupId),
            isAdminDebug);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId,
            Group("guilds", wardGroupId),
            permitted);

        Assert.True(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Theory]
    [InlineData("2", "3")]
    [InlineData("", "")]
    public void CanAccess_denies_players_without_a_trust_source(string playerGroupId, string wardGroupId)
    {
        var actor = new ManagedWardAccessActor(
            playerId: 100L,
            playerGroup: Group("guilds", playerGroupId),
            isAdminDebug: false);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId: 42L,
            wardGroup: Group("guilds", wardGroupId),
            permitted: false);

        Assert.False(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Fact]
    public void HasMatchingGroup_requires_exact_provider_and_id()
    {
        Assert.True(ManagedWardAccessPolicy.HasMatchingGroup(
            Group("clan", "0123456789abcdef"),
            Group("clan", "0123456789abcdef")));
        Assert.False(ManagedWardAccessPolicy.HasMatchingGroup(default, default));
        Assert.False(ManagedWardAccessPolicy.HasMatchingGroup(
            Group("guilds", "12"),
            Group("clan", "12")));
        Assert.False(ManagedWardAccessPolicy.HasMatchingGroup(
            Group("clan", "ABC"),
            Group("clan", "abc")));
    }

    [Fact]
    public void Dedicated_server_clan_member_access_is_allowed_by_matching_clan_projection()
    {
        var actor = new ManagedWardAccessActor(
            playerId: 200L,
            playerGroup: Group("clan", "clan-id"),
            isAdminDebug: false);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId: 100L,
            wardGroup: Group("clan", "clan-id"),
            permitted: false);

        Assert.True(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Fact]
    public void CanAccess_denies_groups_from_different_providers_even_when_raw_ids_match()
    {
        var actor = new ManagedWardAccessActor(200L, Group("guilds", "77"), false);
        var subject = new ManagedWardAccessSubject(100L, Group("clan", "77"), false);

        Assert.False(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Fact]
    public void WardGroupIdentity_default_value_has_a_safe_hash_code()
    {
        _ = default(WardGroupIdentity).GetHashCode();
    }

    [Fact]
    public void WardGroupIdentity_name_is_display_metadata_not_identity()
    {
        Assert.Equal(
            new WardGroupIdentity("clan", "exact-id", "Before"),
            new WardGroupIdentity("clan", "exact-id", "After"));
    }

    private static WardGroupIdentity Group(string provider, string id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? default
            : new WardGroupIdentity(provider, id, string.Empty);
    }
}
