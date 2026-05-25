namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class ManagedWardAccessPolicyTests
{
    [Theory]
    [InlineData(42L, 42L, false, 0, 0, false, true, "owner", false, false)]
    [InlineData(100L, 42L, true, 0, 0, false, true, "admin_debug", false, false)]
    [InlineData(100L, 42L, false, 7, 7, false, true, "guild", false, true)]
    [InlineData(100L, 42L, false, 0, 0, true, true, "permitted", true, false)]
    [InlineData(100L, 42L, false, 2, 3, false, false, "denied", false, false)]
    public void Evaluate_returns_expected_decision(
        long actorPlayerId,
        long ownerPlayerId,
        bool isAdminDebug,
        int playerGuildId,
        int wardGuildId,
        bool permitted,
        bool expectedAllowed,
        string expectedReason,
        bool expectedPermitted,
        bool expectedSameGuild)
    {
        var actor = new ManagedWardAccessActor(
            actorPlayerId,
            new WardGuildIdentity(playerGuildId, string.Empty),
            isAdminDebug);
        var subject = new ManagedWardAccessSubject(
            ownerPlayerId,
            new WardGuildIdentity(wardGuildId, string.Empty),
            permitted,
            string.Empty,
            "test-zdo");

        var result = ManagedWardAccessPolicy.Evaluate(actor, subject);

        Assert.Equal(expectedAllowed, result.Allowed);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedPermitted, result.Permitted);
        Assert.Equal(expectedSameGuild, result.SameGuild);
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
            permitted: false,
            wardSteamAccountId: string.Empty,
            wardZdoLabel: "dedicated-server-ward");

        var result = ManagedWardAccessPolicy.Evaluate(actor, subject);

        Assert.True(result.Allowed);
        Assert.Equal("guild", result.Reason);
        Assert.True(result.SameGuild);
        Assert.False(result.Permitted);
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
            permitted: false,
            wardSteamAccountId: string.Empty,
            wardZdoLabel: "dedicated-server-ward");

        var result = ManagedWardAccessPolicy.Evaluate(actor, subject);

        Assert.False(result.Allowed);
        Assert.Equal("denied", result.Reason);
        Assert.False(result.SameGuild);
    }
}
