namespace STUWard;

internal readonly struct ManagedWardRef
{
    private ManagedWardRef(PrivateArea? area, ZNetView? nview, ZDO? zdo, StuWardArea? component)
    {
        Area = area;
        NView = nview;
        Zdo = zdo;
        Component = component;
    }

    internal PrivateArea? Area { get; }
    internal ZNetView? NView { get; }
    internal ZDO? Zdo { get; }
    internal StuWardArea? Component { get; }

    internal bool HasArea => Area != null;
    internal bool HasManagedComponent => Component != null;
    internal bool IsManagedZdo => WardOwnership.IsManagedWardZdo(Zdo);
    internal bool IsManaged => HasManagedComponent || IsManagedZdo;
    internal bool IsPlacementGhost => Area != null && Player.IsPlacementGhost(Area.gameObject);
    internal bool HasValidNetworkIdentity => NView != null && NView.IsValid() && Zdo != null;
    internal bool IsOwner => NView != null && NView.IsValid() && NView.IsOwner();
    internal long CreatorPlayerId => Zdo?.GetLong(ZDOVars.s_creator, 0L) ?? 0L;

    internal static ManagedWardRef FromArea(PrivateArea? area)
    {
        return FromArea(area, knownZdo: null);
    }

    internal static ManagedWardRef FromArea(PrivateArea? area, ZDO? knownZdo)
    {
        var nview = WardPrivateAreaSafeAccess.GetNView(area);
        var zdo = knownZdo != null && knownZdo.IsValid()
            ? knownZdo
            : WardPrivateAreaSafeAccess.GetZdo(nview);
        return new ManagedWardRef(
            area,
            nview,
            zdo,
            area != null ? area.GetComponent<StuWardArea>() : null);
    }

    internal ManagedWardRef EnsureManagedComponent(out bool added)
    {
        added = false;
        if (Area == null || HasManagedComponent || !IsManagedZdo || IsPlacementGhost)
        {
            return this;
        }

        Area.gameObject.AddComponent<StuWardArea>();
        added = true;
        return FromArea(Area, Zdo);
    }
}
