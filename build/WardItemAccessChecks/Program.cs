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
var itemChecks = checks;
ZNet.instance = new();
Container NewContainer(bool grave, long owner)
{
    var container = new Container();
    container.transform.position = inside;
    var view = new ZNetView();
    view.Data.Set(ZDOVars.s_owner, owner);
    container.Components[typeof(Container)] = container;
    container.Components[typeof(ZNetView)] = view;
    if (grave)
    {
        var tombstone = new TombStone { Components = container.Components };
        container.Components[typeof(TombStone)] = tombstone;
    }
    return container;
}
void ExpectContainer(Container container, long playerId, bool handled, bool allowed, string name)
{
    var actualHandled = WardAccessApi.TryCheckContainerAccess(container, playerId, out var actualAllowed);
    Check(actualHandled == handled && actualAllowed == allowed, name);
}
var ownGrave = NewContainer(true, 2);
var chest = NewContainer(false, 2);
ExpectContainer(ownGrave, 2, true, true, "untrusted character can recover own grave");
ExpectContainer(ownGrave, 3, true, false, "other character cannot recover grave");
ExpectContainer(chest, 2, true, false, "ordinary chest with matching owner data stays denied");
ExpectContainer(ownGrave, 1, true, true, "trusted access to another grave is preserved");
ExpectContainer(chest, 1, true, true, "trusted access to ordinary chest is preserved");
ExpectContainer(ownGrave, 0, false, false, "zero character ID cannot gain grave exception");
ExpectContainer(null, 2, false, false, "missing container is unhandled");
var view = ownGrave.GetComponent<ZNetView>();
view.NetworkOwner = 3;
ExpectContainer(ownGrave, 2, true, true, "network owner migration does not revoke character access");
ExpectContainer(ownGrave, 3, true, false, "network owner does not become grave owner");
view.Data.Set(ZDOVars.s_owner, 3);
ExpectContainer(ownGrave, 2, true, false, "changed grave ownership is not cached");
ExpectContainer(ownGrave, 3, true, true, "current stored grave owner receives exception");
view.Data.Set(ZDOVars.s_owner, 0);
ExpectContainer(ownGrave, 2, true, false, "unset grave owner fails closed");
view.Data.Set(ZDOVars.s_owner, 2);
view.Valid = false;
ExpectContainer(ownGrave, 2, true, false, "invalid network view fails closed");
view.Valid = true;
ownGrave.Components.Remove(typeof(ZNetView));
ExpectContainer(ownGrave, 2, true, false, "missing network view fails closed");
ownGrave.Components[typeof(ZNetView)] = view;
var secondContainer = new Container { Components = ownGrave.Components };
secondContainer.transform.position = inside;
ExpectContainer(secondContainer, 2, true, false, "second container on grave object is not the grave inventory");
ownGrave.Components.Remove(typeof(TombStone));
ExpectContainer(ownGrave, 2, true, false, "removed grave component cannot retain exception");
ownGrave = NewContainer(true, 2);
ownGrave.transform.position = outside;
ExpectContainer(ownGrave, 2, false, false, "outside managed coverage preserves original handling");
WardAccess.Enabled = false;
ownGrave.transform.position = inside;
ExpectContainer(ownGrave, 2, false, false, "disabled wards preserve original handling");
WardAccess.Enabled = true;
WardAccess.ContainerRestriction = false;
ExpectContainer(chest, 2, true, true, "disabled container restriction preserves ordinary access");
ExpectContainer(ownGrave, 3, true, true, "disabled container restriction preserves other-grave access");
Console.WriteLine($"PASS: {itemChecks} item-access and {checks - itemChecks} tombstone/container API checks. Source-linked API with component/ZDO/ward fixtures; no Unity or RPC execution.");

namespace UnityEngine
{
    public class Component
    {
        internal Dictionary<Type, Component> Components = new();
        public T GetComponent<T>() where T : Component => Components.TryGetValue(typeof(T), out var value) ? (T)value : null;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
public class ZNet { public static ZNet instance = new(); }
public class Transform { public Vector3 position; }
public class Container : Component { public Transform transform = new(); }
public class TombStone : Component { }
public class ZNetView : Component
{
    public ZDO Data = new();
    public bool Valid = true;
    public long NetworkOwner;
    public bool IsValid() => Valid;
    public ZDO GetZDO() => Data;
}
public class ZDO
{
    private readonly Dictionary<int, long> _values = new();
    public long GetLong(int key, long fallback) => _values.TryGetValue(key, out var value) ? value : fallback;
    public void Set(int key, long value) => _values[key] = value;
}
public static class ZDOVars { public const int s_owner = 42; }
public class PrivateArea { public bool IsInside(Vector3 p, float r) => false; }
namespace STUWard
{
    internal static class Plugin { internal static bool IsReady = true; }
    internal static class WardItemPrefabPolicy { internal static bool IsBlockedItem(string p) => p == "FLG_TamingOrb"; }
    internal enum WardRestrictionOptions { Containers }
    internal static class WardAccess
    {
        internal static bool Enabled = true;
        internal static bool ContainerRestriction = true;
        internal enum AccessDecision { NoWard, Allowed, Denied }
        internal struct AccessResult { internal AccessDecision Decision; internal bool IsDenied => Decision == AccessDecision.Denied; }
        internal static AccessResult EvaluateAccess(Vector3 p, float r, long id, bool flash) => new()
        {
            Decision = Enabled && p.x < 10 && id != 1 ? AccessDecision.Denied : AccessDecision.Allowed
        };
        internal static List<PrivateArea> GetCandidateManagedWards(Vector3 p, float r, bool requireEnabled) =>
            Enabled && p.x < 10 ? new() { new PrivateArea() } : new();
        internal static bool IsManagedWard(PrivateArea a, bool requireEnabled) => false;
        internal static bool HasEnabledManagedWards() => Enabled;
        internal static AccessResult EvaluateRestrictionAccessAgainstCandidates(WardRestrictionOptions o, Vector3 p, float r, long id, List<PrivateArea> a, bool flash) =>
            a.Count == 0 ? default : ContainerRestriction ? EvaluateAccess(p, r, id, flash) : new() { Decision = AccessDecision.Allowed };
    }
}
