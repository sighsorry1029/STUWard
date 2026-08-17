namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class GuildIdentityPolicyTests
{
    [Theory]
    [InlineData("76561198000000000", "76561198000000000")]
    [InlineData("Steam_76561198000000000", "76561198000000000")]
    [InlineData(" PlayFab_ABC123 ", "PlayFab_ABC123")]
    [InlineData("", "")]
    public void NormalizeAccountId_only_collapses_the_Steam_prefix(
        string accountId,
        string expected)
    {
        Assert.Equal(expected, GuildIdentityPolicy.NormalizeAccountId(accountId));
    }

    [Fact]
    public void NormalizeAccountId_keeps_cross_platform_namespaces_distinct()
    {
        Assert.NotEqual(
            GuildIdentityPolicy.NormalizeAccountId("PlayFab_123"),
            GuildIdentityPolicy.NormalizeAccountId("Steam_123"));
    }

    [Theory]
    [InlineData("76561198000000000", "Steam_76561198000000000")]
    [InlineData("Steam_76561198000000000", "Steam_76561198000000000")]
    [InlineData(" Steam_76561198000000000 ", "Steam_76561198000000000")]
    [InlineData("PlayFab_ABC123", "PlayFab_ABC123")]
    public void GetGuildsAccountId_uses_the_current_platform_identity(
        string accountId,
        string expected)
    {
        Assert.Equal(expected, GuildIdentityPolicy.GetGuildsAccountId(accountId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetGuildsAccountId_rejects_empty_account_ids(string accountId)
    {
        Assert.Equal(string.Empty, GuildIdentityPolicy.GetGuildsAccountId(accountId));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(12, 12, true)]
    [InlineData(0, 12, true)]
    [InlineData(13, 12, true)]
    [InlineData(12, 0, false)]
    public void CanApplyAuthoritativeGuild_only_retries_an_unresolved_server_no_guild_result(
        int reportedGuildId,
        int authoritativeGuildId,
        bool expected)
    {
        Assert.Equal(
            expected,
            GuildIdentityPolicy.CanApplyAuthoritativeGuild(reportedGuildId, authoritativeGuildId));
    }
}
