using System.Reflection;
using Xunit;

namespace STUWard.Tests
{
    // Runs the production handshake against transport/engine stand-ins. The
    // native IsAdmin policy is an external decision, not reimplemented here.
    public sealed class AdminDebugHandshakeTests : IDisposable
    {
        private const string Request = "STUWard_RequestAdminDebugState";
        private const string Snapshot = "STUWard_ReceiveAdminDebugSnapshot";
        private const string Projection = "STUWard_ReceiveAdminDebugProjection";
        private readonly ZNet net = new();
        private readonly ZRoutedRpc rpc = new();

        public AdminDebugHandshakeTests()
        {
            ZNet.instance = net;
            ZRoutedRpc.instance = rpc;
            Player.m_localPlayer = null;
            Player.m_debugMode = false;
            WardOwnership.Bindings.Clear();
            Plugin.Log.Messages.Clear();
            WardAdminDebugAccess.ResetRuntimeState();
            WardAdminDebugAccess.RegisterRpcs();
        }

        private ZNetPeer Peer(long sender = 10, long player = 20, string host = "Steam_123")
        {
            var peer = new ZNetPeer { m_uid = sender, m_socket = new TestSocket { Host = host } };
            net.Peers[sender] = peer;
            if (player != 0) WardOwnership.Bindings[sender] = player;
            return peer;
        }

        [Fact]
        public void Native_override_grants_unlisted_admin_and_all_managed_ward_trust()
        {
            Peer();
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            var actor = new ManagedWardAccessActor(20, default, WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.True(ManagedWardAccessPolicy.CanAccess(actor, new ManagedWardAccessSubject(30, default, false)));
            Assert.Contains(rpc.Sent, m => m.Name == Snapshot && m.Target == 10);
        }

        [Fact]
        public void Native_explicit_deny_is_not_overridden_by_a_listed_account()
        {
            Peer();
            net.Admins.Add("123");
            net.AdminPolicy = _ => false;
            rpc.Deliver(Request, 10, true);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.Contains(Plugin.Log.Messages, m => m.Contains("not-server-admin"));
        }

        [Theory]
        [InlineData("Steam_123")]
        [InlineData("Xbox_123")]
        [InlineData("123")]
        public void Administrator_policy_receives_the_unmodified_authenticated_socket_identity(string host)
        {
            Peer(host: host);
            net.AdminPolicy = actual => { Assert.Equal(host, actual); return true; };
            rpc.Deliver(Request, 10, true);
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.Equal(2, net.AdminChecks);
        }

        [Fact]
        public void Unauthenticated_sender_cannot_request_a_grant_or_allocate_diagnostics()
        {
            net.AdminPolicy = _ => throw new Exception("Should not check an unknown sender");
            rpc.Deliver(Request, 99, true);
            Assert.Empty(rpc.Sent);
            Assert.Empty(Plugin.Log.Messages);
        }

        [Fact]
        public void Identity_not_ready_can_retry_then_gain_approval()
        {
            Peer(player: 0);
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            Assert.Equal(0, net.AdminChecks);
            Assert.Contains(Plugin.Log.Messages, m => m.Contains("identity-not-ready"));
            WardOwnership.Bindings[10] = 20;
            rpc.Deliver(Request, 10, true);
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
        }

        [Fact]
        public void Native_error_and_unready_connection_fail_closed()
        {
            var peer = Peer();
            net.AdminPolicy = _ => throw new InvalidOperationException("private detail");
            rpc.Deliver(Request, 10, true);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.Contains(Plugin.Log.Messages, m => m.Contains("admin-check-error:InvalidOperationException"));
            Assert.DoesNotContain(Plugin.Log.Messages, m => m.Contains("private detail"));
            peer.Ready = false;
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
        }

        [Fact]
        public void Changed_native_admin_policy_revokes_grant_during_access_revalidation()
        {
            Peer();
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            net.AdminPolicy = _ => false;
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.Contains(rpc.Sent, m => m.Name == Projection && (long)m.Values[0] == 20 && !(bool)m.Values[1]);
        }

        [Fact]
        public void Disabling_debug_removes_server_trust_even_if_native_admin_remains_true()
        {
            Peer();
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            rpc.Deliver(Request, 10, false);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.Contains(Plugin.Log.Messages, m => m.Contains("debug-off"));
        }

        [Fact]
        public void Old_grant_does_not_follow_a_replaced_peer_or_character()
        {
            Peer();
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            Peer(); // same sender number, different connection object
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            rpc.Deliver(Request, 10, true);
            WardOwnership.Bindings[10] = 21;
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(21));
        }

        [Fact]
        public void Disconnect_and_session_reset_clear_approval_and_diagnostics()
        {
            Peer();
            net.AdminPolicy = _ => true;
            rpc.Deliver(Request, 10, true);
            var logs = Plugin.Log.Messages.Count;
            WardAdminDebugAccess.ForgetServerPeer(10);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            rpc.Deliver(Request, 10, true);
            Assert.True(Plugin.Log.Messages.Count > logs);
            WardAdminDebugAccess.ResetRuntimeState();
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
        }

        [Fact]
        public void Server_diagnostic_does_not_repeat_on_identical_heartbeats()
        {
            Peer();
            net.AdminPolicy = _ => false;
            for (var i = 0; i < 5; i++) rpc.Deliver(Request, 10, true);
            Assert.Single(Plugin.Log.Messages);
        }

        private Player Client()
        {
            net.Server = false;
            var player = new Player { Id = 20 };
            Player.m_localPlayer = player;
            return player;
        }

        private static ZPackage AdminSnapshot(params long[] players)
        {
            var package = new ZPackage();
            package.Write(players.Length);
            foreach (var id in players) package.Write(id);
            return package;
        }

        private static void ElapseRetry()
        {
            typeof(WardAdminDebugAccess).GetField("_lastLocalDebugAdminSyncUtc", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, DateTime.UtcNow.AddSeconds(-4));
        }

        [Fact]
        public void Client_requires_both_real_debug_flag_and_server_approval()
        {
            var player = Client();
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.False((bool)rpc.Sent.Last().Values[0]);
            Player.m_debugMode = true;
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            rpc.Deliver(Snapshot, 99, AdminSnapshot(20)); // unrelated peer
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot(20));
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Player.m_debugMode = false;
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
        }

        [Fact]
        public void Missing_server_does_not_consume_a_state_change()
        {
            var player = Client();
            Player.m_debugMode = true;
            rpc.ServerId = 0;
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Empty(rpc.Sent);
            rpc.ServerId = 100;
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Single(rpc.Sent);
            Assert.Equal(100, rpc.Sent[0].Target);
        }

        [Fact]
        public void Missing_response_diagnostic_is_throttled_while_requests_keep_retrying()
        {
            var player = Client();
            Player.m_debugMode = true;
            WardAdminDebugAccess.UpdateLocalState(player);
            typeof(WardAdminDebugAccess).GetField("_nextNoResponseWarningUtc", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, DateTime.UtcNow.AddSeconds(-1));
            ElapseRetry();
            WardAdminDebugAccess.UpdateLocalState(player);
            ElapseRetry();
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Equal(3, rpc.Sent.Count);
            Assert.Single(Plugin.Log.Messages, m => m.Contains("No server approval response"));
        }

        [Fact]
        public void Lost_disable_request_retries_and_late_enabled_snapshot_does_not_settle_it()
        {
            var player = Client();
            Player.m_debugMode = true;
            WardAdminDebugAccess.UpdateLocalState(player);
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot(20));
            Player.m_debugMode = false;
            WardAdminDebugAccess.UpdateLocalState(player);
            var sent = rpc.Sent.Count;
            ElapseRetry();
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Equal(sent + 1, rpc.Sent.Count);
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot(20));
            ElapseRetry();
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Equal(sent + 2, rpc.Sent.Count);
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot());
            ElapseRetry();
            WardAdminDebugAccess.UpdateLocalState(player);
            Assert.Equal(sent + 2, rpc.Sent.Count);
        }

        [Fact]
        public void New_rpc_instance_is_bound_and_session_reset_drops_client_grants()
        {
            Client();
            var replacement = new ZRoutedRpc();
            ZRoutedRpc.instance = replacement;
            WardAdminDebugAccess.RegisterRpcs();
            replacement.Deliver(Projection, replacement.ServerId, 21L, true);
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(21));
            WardAdminDebugAccess.ResetRuntimeState();
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(21));
        }

        [Fact]
        public void Empty_snapshot_between_character_instances_clears_previous_local_approval()
        {
            var player = Client();
            Player.m_debugMode = true;
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot(20));
            Assert.True(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
            Player.m_localPlayer = null;
            rpc.Deliver(Snapshot, rpc.ServerId, AdminSnapshot());
            Player.m_localPlayer = player;
            Assert.False(WardAdminDebugAccess.IsPlayerAdminDebugController(20));
        }

        public void Dispose()
        {
            WardAdminDebugAccess.ResetRuntimeState();
            Player.m_localPlayer = null;
            Player.m_debugMode = false;
            ZNet.instance = null;
            ZRoutedRpc.instance = null;
        }
    }
}

namespace STUWard
{
    internal sealed class Player
    {
        public static Player? m_localPlayer;
        public static bool m_debugMode;
        public long Id;
        public long GetPlayerID() => Id;
    }
    internal sealed class PrivateArea { }
    internal static class WardAccess { public static bool IsManagedWard(PrivateArea area, bool enabled) => true; }
    internal sealed class TestSocket { public string Host = ""; public string GetHostName() => Host; }
    internal sealed class ZNetPeer
    {
        public long m_uid;
        public TestSocket? m_socket;
        public bool Ready = true;
        public bool IsReady() => Ready;
    }
    internal sealed class ZNet
    {
        public static ZNet? instance;
        public bool Server = true;
        public readonly Dictionary<long, ZNetPeer> Peers = new();
        public readonly List<string> Admins = new();
        public Func<string, bool> AdminPolicy = _ => false;
        public int AdminChecks;
        public bool IsServer() => Server;
        public ZNetPeer? GetPeer(long sender) => Peers.GetValueOrDefault(sender);
        public bool IsAdmin(string host) { AdminChecks++; return AdminPolicy(host); }
        public List<string> GetAdminList() => Admins;
    }
    internal sealed class ZRoutedRpc
    {
        public static ZRoutedRpc? instance;
        public const long Everybody = 0;
        public long ServerId = 100;
        private readonly Dictionary<string, Delegate> handlers = new();
        public readonly List<(long Target, string Name, object[] Values)> Sent = new();
        public void Register<T>(string name, Action<long, T> action) => handlers[name] = action;
        public void Register<T, U>(string name, Action<long, T, U> action) => handlers[name] = action;
        public long GetServerPeerID() => ServerId;
        public void InvokeRoutedRPC(long target, string name, params object[] values) => Sent.Add((target, name, values));
        public void Deliver(string name, long sender, params object[] values) => handlers[name].DynamicInvoke(new object[] { sender }.Concat(values).ToArray());
    }
    internal sealed class ZPackage
    {
        private readonly Queue<object> values = new();
        public void Write(int value) => values.Enqueue(value);
        public void Write(long value) => values.Enqueue(value);
        public int ReadInt() => (int)values.Dequeue();
        public long ReadLong() => (long)values.Dequeue();
    }
    internal static class WardOwnership
    {
        public static readonly Dictionary<long, long> Bindings = new();
        public static bool TryResolveAuthoritativePlayerIdFromSender(long sender, out long player) => Bindings.TryGetValue(sender, out player) && player != 0;
        public static bool IsAuthoritativeServerSender(long sender) => sender != 0 && sender == ZRoutedRpc.instance?.ServerId;
        public static string NormalizeAccountIdValue(string? account) => GuildIdentityPolicy.NormalizeAccountId(account);
    }
    internal static class Plugin { public static readonly TestLog Log = new(); }
    internal sealed class TestLog
    {
        public readonly List<string> Messages = new();
        public void LogInfo(string value) => Messages.Add(value);
        public void LogWarning(string value) => Messages.Add(value);
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
