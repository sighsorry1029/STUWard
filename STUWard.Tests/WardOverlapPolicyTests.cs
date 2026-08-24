namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class WardOverlapPolicyTests
{
    [Fact]
    public void IsForeignOverlap_blocks_overlapping_foreign_area()
    {
        var query = Query(owner: 10L, Group("guilds", "1"));
        var area = Area(id: 1, x: 10f, owner: 20L, Group("guilds", "2"));

        Assert.True(WardOverlapPolicy.IsForeignOverlap(query, area));
    }

    [Fact]
    public void IsForeignOverlap_allows_edge_touching_area()
    {
        var query = Query(owner: 10L, Group("guilds", "1"));
        var area = Area(id: 1, x: 16f, owner: 20L, Group("guilds", "2"));

        Assert.False(WardOverlapPolicy.IsForeignOverlap(query, area));
    }

    [Fact]
    public void IsForeignOverlap_ignores_same_owner_or_exact_group()
    {
        Assert.False(WardOverlapPolicy.IsForeignOverlap(
            Query(owner: 10L, Group("guilds", "7")),
            Area(id: 1, x: 10f, owner: 10L, Group("guilds", "8"))));
        Assert.False(WardOverlapPolicy.IsForeignOverlap(
            Query(owner: 10L, Group("clan", "group-id")),
            Area(id: 1, x: 10f, owner: 20L, Group("clan", "group-id"))));
    }

    [Fact]
    public void IsForeignOverlap_does_not_mix_providers_with_the_same_raw_id()
    {
        var query = Query(owner: 10L, Group("guilds", "7"));
        var area = Area(id: 1, x: 10f, owner: 20L, Group("clan", "7"));

        Assert.True(WardOverlapPolicy.IsForeignOverlap(query, area));
    }

    [Fact]
    public void IsForeignOverlap_ignores_requested_area()
    {
        var query = new WardOverlapQuery(0f, 0f, 8f, 10L, Group("guilds", "1"), ignoredAreaId: 1);
        var area = Area(id: 1, x: 1f, owner: 20L, Group("guilds", "2"));

        Assert.False(WardOverlapPolicy.IsForeignOverlap(query, area));
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_clamps_to_nearest_foreign_area()
    {
        var query = new WardOverlapQuery(0f, 0f, 20f, 10L, Group("guilds", "1"));
        var areas = new[]
        {
            new WardOverlapArea(1, 30f, 0f, 8f, 20L, Group("guilds", "2")),
            new WardOverlapArea(2, 50f, 0f, 8f, 30L, Group("guilds", "3"))
        };

        Assert.Equal(22f, WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas));
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_ignores_trusted_areas_and_clamps_to_fallback()
    {
        var query = new WardOverlapQuery(0f, 0f, 20f, 10L, Group("clan", "one"));
        var areas = new[]
        {
            new WardOverlapArea(1, 5f, 0f, 8f, 10L, Group("clan", "two")),
            new WardOverlapArea(2, 5f, 0f, 8f, 20L, Group("clan", "one"))
        };

        Assert.Equal(64f, WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas));
    }

    [Fact]
    public void GetMaxNonOverlappingRadius_never_returns_negative_radius()
    {
        var query = new WardOverlapQuery(0f, 0f, 20f, 10L, Group("guilds", "1"));
        var areas = new[] { new WardOverlapArea(1, 2f, 0f, 8f, 20L, Group("guilds", "2")) };

        Assert.Equal(0f, WardOverlapPolicy.GetMaxNonOverlappingRadius(64f, query, areas));
    }

    [Fact]
    public void TryGetPlacementRadius_uses_the_largest_radius_left_by_an_existing_foreign_ward()
    {
        var query = new WardOverlapQuery(0f, 0f, 32f, 10L, Group("guilds", "1"));
        var areas = new[] { new WardOverlapArea(1, 50f, 0f, 32f, 20L, Group("guilds", "2")) };

        var canPlace = WardOverlapPolicy.TryGetPlacementRadius(8f, 32f, query, areas, out var radius);

        Assert.True(canPlace);
        Assert.Equal(18f, radius);
    }

    [Fact]
    public void TryGetPlacementRadius_rejects_when_less_than_the_minimum_radius_remains()
    {
        var query = new WardOverlapQuery(0f, 0f, 32f, 10L, Group("guilds", "1"));
        var areas = new[] { new WardOverlapArea(1, 39f, 0f, 32f, 20L, Group("guilds", "2")) };

        var canPlace = WardOverlapPolicy.TryGetPlacementRadius(8f, 32f, query, areas, out var radius);

        Assert.False(canPlace);
        Assert.Equal(7f, radius);
    }

    private static WardOverlapQuery Query(long owner, WardGroupIdentity group)
    {
        return new WardOverlapQuery(0f, 0f, 8f, owner, group);
    }

    private static WardOverlapArea Area(int id, float x, long owner, WardGroupIdentity group)
    {
        return new WardOverlapArea(id, x, 0f, 8f, owner, group);
    }

    private static WardGroupIdentity Group(string provider, string id)
    {
        return new WardGroupIdentity(provider, id, string.Empty);
    }
}
