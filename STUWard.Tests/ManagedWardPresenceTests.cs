using UnityEngine;
using Xunit;
using ProtectionMode = STUWard.Plugin.HostileCreatureStructureProtectionMode;

namespace STUWard.Tests;

// Exercises the production scheduler and presence decisions, not Unity's
// geometry, spatial index or authorization implementations. Engine stand-ins
// are shared with the handshake suite, so both use the same serial collection.
[Collection("Engine state")]
public sealed class ManagedWardPresenceTests : IDisposable
{
    private readonly PrivateArea ward = new();

    public ManagedWardPresenceTests()
    {
        Player.Players.Clear();
        WardAccess.Areas.Clear();
        WardAccess.QueryRadii.Clear();
        ManagedWardRuntimeContexts.Entries.Clear();
        ZNet.instance = new ZNet();
        Time.time = 0f;
        Plugin.HostileCreatureStructureProtection = new() { Value = ProtectionMode.UnattendedOnly };
        ManagedWardPresenceService.ResetRuntimeState();
        ward.AllowedPlayers.Add(1);
        WardAccess.Areas.Add(ward);
    }

    private static Player AddPlayer(long id = 1, float position = 0f)
    {
        var player = new Player { Id = id };
        player.transform.position = new Vector3(position);
        Player.Players.Add(player);
        return player;
    }

    private static bool IsBlocked(float point = 0f) =>
        ManagedWardPresenceService.ShouldBlockHostileCreatureDamageToBuilding(new Vector3(point));

    private float LastSeen => ManagedWardRuntimeContexts.GetOrCreate(ward).PresenceLastTrustedNearbyTime;

    [Theory]
    [InlineData(1L, 0f, false)]
    [InlineData(1L, 17.9f, false)]
    [InlineData(1L, 18.1f, true)]
    [InlineData(2L, 0f, true)]
    [InlineData(0L, 0f, true)]
    public void Damage_check_uses_trust_and_the_eight_meter_presence_buffer(long id, float position, bool blocked)
    {
        Player.Players.Add(null!);
        AddPlayer(id, position);
        Assert.Equal(blocked, IsBlocked());
        Assert.Equal(blocked ? float.NegativeInfinity : 0f, LastSeen);
    }

    [Fact]
    public void Immediate_check_updates_only_covering_wards_and_overlap_requires_all_to_be_attended()
    {
        var other = new PrivateArea { Center = 30f };
        other.AllowedPlayers.Add(1);
        WardAccess.Areas.Add(other);
        AddPlayer();
        AddPlayer(position: 30f);
        Assert.False(IsBlocked());
        Assert.False(ManagedWardRuntimeContexts.TryGet(other, out _));

        other.Center = 0f;
        other.AllowedPlayers.Clear();
        Assert.True(IsBlocked());
        other.AllowedPlayers.Add(1);
        Assert.False(IsBlocked());
    }

    [Fact]
    public void Sweep_has_one_second_cadence_and_updates_indexed_wards_outside_initial_coverage()
    {
        var other = new PrivateArea { Center = 30f };
        other.AllowedPlayers.Add(1);
        WardAccess.Areas.Add(other);
        AddPlayer();
        AddPlayer(position: 30f);
        Assert.False(IsBlocked());
        WardAccess.QueryRadii.Clear();
        Time.time = 0.99f;
        ManagedWardPresenceService.Update();
        Assert.Empty(WardAccess.QueryRadii);
        Time.time = 1f;
        ManagedWardPresenceService.Update();
        Assert.Equal(new[] { 8f, 8f }, WardAccess.QueryRadii);
        Assert.Equal(1f, LastSeen);
        Assert.Equal(1f, ManagedWardRuntimeContexts.GetOrCreate(other).PresenceLastTrustedNearbyTime);
    }

    [Theory]
    [InlineData(1L, 17.9f, true)]
    [InlineData(1L, 18.1f, false)]
    [InlineData(2L, 0f, false)]
    [InlineData(0L, 0f, false)]
    public void Periodic_sweep_applies_the_same_presence_criteria(long id, float position, bool attended)
    {
        Assert.True(IsBlocked()); // Starts activity with no nearby players.
        Player.Players.Add(null!);
        AddPlayer(id, position);
        Time.time = 1f;
        ManagedWardPresenceService.Update();
        Assert.Equal(attended ? 1f : float.NegativeInfinity, LastSeen);
    }

    [Fact]
    public void Last_trusted_presence_has_ten_second_grace_without_refreshing_absent_players()
    {
        AddPlayer();
        Assert.False(IsBlocked());
        Player.Players.Clear();
        Time.time = 10f;
        Assert.False(IsBlocked());
        Time.time = 10.01f;
        Assert.True(IsBlocked());
        Assert.Equal(0f, LastSeen);
    }

    [Fact]
    public void Fifteen_second_activity_expiry_keeps_history_and_next_damage_restarts_refresh()
    {
        AddPlayer();
        Assert.False(IsBlocked());
        Time.time = 15f;
        ManagedWardPresenceService.Update();
        Assert.Equal(15f, LastSeen);
        Time.time = 16f;
        ManagedWardPresenceService.Update();
        Assert.Equal(15f, LastSeen);
        Assert.False(IsBlocked());
        Assert.Equal(16f, LastSeen);
    }

    [Fact]
    public void Client_update_does_not_sweep_server_presence()
    {
        AddPlayer();
        Assert.False(IsBlocked());
        ZNet.instance!.Server = false;
        Time.time = 1f;
        ManagedWardPresenceService.Update();
        Assert.Equal(0f, LastSeen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mode_or_enabled_ward_change_clears_active_presence(bool disableWard)
    {
        AddPlayer();
        Assert.False(IsBlocked());
        if (disableWard) ward.Enabled = false;
        else Plugin.HostileCreatureStructureProtection!.Value = ProtectionMode.Off;
        ManagedWardPresenceService.Update();
        Assert.Equal(float.NegativeInfinity, LastSeen);
        Assert.False(IsBlocked());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Invalidation_and_session_reset_clear_history_and_stop_sweeps(bool resetSession)
    {
        AddPlayer();
        Assert.False(IsBlocked());
        if (resetSession) ManagedWardPresenceService.ResetRuntimeState();
        else ManagedWardPresenceService.Invalidate();
        Assert.Equal(float.NegativeInfinity, LastSeen);
        Time.time = 1f;
        ManagedWardPresenceService.Update();
        Assert.Equal(float.NegativeInfinity, LastSeen);
    }

    [Fact]
    public void Always_mode_uses_exact_enabled_coverage_without_presence()
    {
        Plugin.HostileCreatureStructureProtection!.Value = ProtectionMode.Always;
        AddPlayer();
        Assert.True(IsBlocked());
        Assert.False(IsBlocked(10.1f));
        Assert.Equal(float.NegativeInfinity, LastSeen);
        ward.Enabled = false;
        Assert.False(IsBlocked());
    }

    public void Dispose()
    {
        ManagedWardPresenceService.ResetRuntimeState();
        Player.Players.Clear();
        WardAccess.Areas.Clear();
        WardAccess.QueryRadii.Clear();
        ManagedWardRuntimeContexts.Entries.Clear();
        ZNet.instance = null;
        Plugin.HostileCreatureStructureProtection = null;
        Time.time = 0f;
    }
}
