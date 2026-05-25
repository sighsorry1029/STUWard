namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class WardOverlapPolicyTests
{
    [Fact]
    public void WouldOverlapForeignWard_blocks_overlapping_foreign_area()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 8f, ownerPlayerId: 10L, guildId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 10f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 2)
        };

        Assert.True(WardOverlapPolicy.WouldOverlapForeignWard(query, areas));
    }

    [Fact]
    public void WouldOverlapForeignWard_allows_edge_touching_area()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 8f, ownerPlayerId: 10L, guildId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 16f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 2)
        };

        Assert.False(WardOverlapPolicy.WouldOverlapForeignWard(query, areas));
    }

    [Theory]
    [InlineData(10L, 20L, 7, 8, true)]
    [InlineData(10L, 10L, 7, 8, false)]
    [InlineData(10L, 20L, 7, 7, false)]
    public void WouldOverlapForeignWard_ignores_trusted_owner_or_guild(
        long queryOwner,
        long areaOwner,
        int queryGuild,
        int areaGuild,
        bool expectedBlock)
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 8f, ownerPlayerId: queryOwner, guildId: queryGuild);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 10f, z: 0f, radius: 8f, ownerPlayerId: areaOwner, guildId: areaGuild)
        };

        Assert.Equal(expectedBlock, WardOverlapPolicy.WouldOverlapForeignWard(query, areas));
    }

    [Fact]
    public void WouldOverlapForeignWard_ignores_requested_area()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 8f, ownerPlayerId: 10L, guildId: 1, ignoredAreaId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 1f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 2)
        };

        Assert.False(WardOverlapPolicy.WouldOverlapForeignWard(query, areas));
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_clamps_to_nearest_foreign_area()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 20f, ownerPlayerId: 10L, guildId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 30f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 2),
            new WardOverlapArea(id: 2, x: 50f, z: 0f, radius: 8f, ownerPlayerId: 30L, guildId: 3)
        };

        var maxRadius = WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas);

        Assert.Equal(22f, maxRadius);
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_ignores_trusted_areas_and_clamps_to_fallback()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 20f, ownerPlayerId: 10L, guildId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 5f, z: 0f, radius: 8f, ownerPlayerId: 10L, guildId: 2),
            new WardOverlapArea(id: 2, x: 5f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 1)
        };

        var maxRadius = WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas);

        Assert.Equal(64f, maxRadius);
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_never_returns_negative_radius()
    {
        var query = new WardOverlapQuery(x: 0f, z: 0f, radius: 20f, ownerPlayerId: 10L, guildId: 1);
        var areas = new[]
        {
            new WardOverlapArea(id: 1, x: 2f, z: 0f, radius: 8f, ownerPlayerId: 20L, guildId: 2)
        };

        var maxRadius = WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas);

        Assert.Equal(0f, maxRadius);
    }
}
