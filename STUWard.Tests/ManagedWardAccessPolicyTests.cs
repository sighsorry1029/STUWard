namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class ManagedWardAccessPolicyTests
{
    [Theory]
    [InlineData(42L, 42L, false, 0, 0, false, true)]
    [InlineData(100L, 42L, true, 0, 0, false, true)]
    [InlineData(100L, 42L, false, 7, 7, false, true)]
    [InlineData(100L, 42L, false, 0, 0, true, true)]
    [InlineData(100L, 42L, false, 2, 3, false, false)]
    public void CanAccess_returns_expected_decision(
        long actorPlayerId,
        long ownerPlayerId,
        bool isAdminDebug,
        int playerGuildId,
        int wardGuildId,
        bool permitted,
        bool expectedAllowed)
    {
        var actor = new ManagedWardAccessActor(
            actorPlayerId,
            new WardGuildIdentity(playerGuildId, string.Empty),
            isAdminDebug);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId,
            new WardGuildIdentity(wardGuildId, string.Empty),
            permitted);

        Assert.Equal(expectedAllowed, ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Fact]
    public void HasMatchingGuild_requires_non_zero_matching_ids()
    {
        Assert.True(ManagedWardAccessPolicy.HasMatchingGuild(
            new WardGuildIdentity(12, "left"),
            new WardGuildIdentity(12, "right")));
        Assert.False(ManagedWardAccessPolicy.HasMatchingGuild(
            new WardGuildIdentity(0, "left"),
            new WardGuildIdentity(0, "right")));
        Assert.False(ManagedWardAccessPolicy.HasMatchingGuild(
            new WardGuildIdentity(12, "left"),
            new WardGuildIdentity(13, "right")));
    }

    [Fact]
    public void Dedicated_server_guild_member_access_is_allowed_by_matching_guild_projection()
    {
        var actor = new ManagedWardAccessActor(
            playerId: 200L,
            playerGuild: new WardGuildIdentity(77, "Guild"),
            isAdminDebug: false);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId: 100L,
            wardGuild: new WardGuildIdentity(77, "Guild"),
            permitted: false);

        Assert.True(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }

    [Theory]
    [InlineData(0, 77)]
    [InlineData(77, 0)]
    public void Dedicated_server_guild_member_access_requires_non_zero_guild_ids(int playerGuildId, int wardGuildId)
    {
        var actor = new ManagedWardAccessActor(
            playerId: 200L,
            playerGuild: new WardGuildIdentity(playerGuildId, "Guild"),
            isAdminDebug: false);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId: 100L,
            wardGuild: new WardGuildIdentity(wardGuildId, "Guild"),
            permitted: false);

        Assert.False(ManagedWardAccessPolicy.CanAccess(actor, subject));
    }
}
