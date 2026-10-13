using STUWard;
using UnityEngine;

// Source-linked API tests. The ward evaluator is a deterministic fixture;
// these validate policy gating and both coordinates, not live ward discovery/trust.
var inside = new Vector3(5, 0, 5);
var outside = new Vector3(50, 0, 50);
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}
bool Allowed(long id, Vector3 actor, Vector3 target, string prefab = "FLG_TamingOrb") =>
    WardAccessApi.IsItemUseAllowed(prefab, id, actor, target);
Check(Allowed(2, outside, outside), "untrusted outside/outside");
Check(!Allowed(2, outside, inside), "outside-to-inside is blocked");
Check(!Allowed(2, inside, outside), "inside-to-outside is blocked");
Check(!Allowed(2, inside, inside), "inside/inside is blocked");
Check(Allowed(1, outside, inside), "trusted target allowed");
Check(Allowed(1, inside, outside), "trusted actor allowed");
Check(Allowed(2, inside, inside, "UnlistedItem"), "unlisted item preserves policy");
WardAccess.Enabled = false;
Check(Allowed(2, inside, inside), "disabled wards allow use");
WardAccess.Enabled = true;
Check(!Allowed(0, outside, outside), "zero ID cannot use local player fallback");
Check(!Allowed(1, outside, new Vector3(float.NaN, 0, 0)), "invalid target rejected");
Check(!Allowed(1, new Vector3(0, float.PositiveInfinity, 0), outside), "invalid actor rejected");
Plugin.IsReady = false;
Check(!Allowed(1, outside, outside), "installed but not ready fails closed");
Plugin.IsReady = true;
ZNet.instance = null;
Check(!Allowed(1, outside, outside), "missing network fails closed");
Console.WriteLine($"PASS: {checks} source-linked item-access API checks; no Unity or real ward evaluation.");

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
public class ZNet { public static ZNet instance = new(); }
public class Transform { public Vector3 position; }
public class Container { public Transform transform = new(); }
public class PrivateArea { public bool IsInside(Vector3 p, float r) => false; }
namespace STUWard
{
    internal static class Plugin { internal static bool IsReady = true; }
    internal static class WardItemPrefabPolicy { internal static bool IsBlockedItem(string p) => p == "FLG_TamingOrb"; }
    internal enum WardRestrictionOptions { Containers }
    internal static class WardAccess
    {
        internal static bool Enabled = true;
        internal enum AccessDecision { NoWard, Allowed, Denied }
        internal struct AccessResult { internal AccessDecision Decision; internal bool IsDenied => Decision == AccessDecision.Denied; }
        internal static AccessResult EvaluateAccess(Vector3 p, float r, long id, bool flash) => new()
        {
            Decision = Enabled && p.x < 10 && id != 1 ? AccessDecision.Denied : AccessDecision.Allowed
        };
        internal static List<PrivateArea> GetCandidateManagedWards(Vector3 p, float r, bool requireEnabled) => new();
        internal static bool IsManagedWard(PrivateArea a, bool requireEnabled) => false;
        internal static bool HasEnabledManagedWards() => false;
        internal static AccessResult EvaluateRestrictionAccessAgainstCandidates(WardRestrictionOptions o, Vector3 p, float r, long id, List<PrivateArea> a, bool flash) => default;
    }
}
