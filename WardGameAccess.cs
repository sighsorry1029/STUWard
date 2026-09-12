using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace STUWard;

// Explicit access to the original 1.0.7 metadata. No publicized compilation
// references or repeated reflection searches on the frame/update paths.
internal static class WardGameAccess
{
    private static readonly AccessTools.FieldRef<PrivateArea, ZNetView> WardView = AccessTools.FieldRefAccess<PrivateArea, ZNetView>("m_nview");
    private static readonly AccessTools.FieldRef<PrivateArea, Piece> WardPiece = AccessTools.FieldRefAccess<PrivateArea, Piece>("m_piece");
    private static readonly AccessTools.FieldRef<Player, Character> HoverCreature = AccessTools.FieldRefAccess<Player, Character>("m_hoveringCreature");
    private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
    private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> PlacementStatus = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
    private static readonly AccessTools.FieldRef<Player, int> PickupMask = AccessTools.FieldRefAccess<Player, int>("m_autoPickupMask");
    private static readonly AccessTools.FieldRef<Player, int> RemoveMask = AccessTools.FieldRefAccess<Player, int>("m_removeRayMask");
    private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
    private static readonly AccessTools.FieldRef<Minimap, bool[]> VisibleIcons = AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");
    private static readonly AccessTools.FieldRef<Minimap, bool> PinUpdate = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");
    private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> WorldObjects = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
    private static readonly AccessTools.FieldRef<CircleProjector, List<GameObject>> Segments = AccessTools.FieldRefAccess<CircleProjector, List<GameObject>>("m_segments");
    private static readonly AccessTools.FieldRef<Terminal, List<string>> Commands = AccessTools.FieldRefAccess<Terminal, List<string>>("m_commandList");
    private static readonly AccessTools.FieldRef<ZNet, Splatform.Platform> SteamPlatform = AccessTools.FieldRefAccess<ZNet, Splatform.Platform>("m_steamPlatform");
    private static readonly AccessTools.FieldRef<bool> AutoPickupEnabled = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField(typeof(Player), "m_enableAutoPickup"));
    private static readonly AccessTools.FieldRef<List<PrivateArea>> AllAreas = AccessTools.StaticFieldRefAccess<List<PrivateArea>>(AccessTools.DeclaredField(typeof(PrivateArea), "m_allAreas"));

    private static readonly Func<PrivateArea, bool> IsEnabledCall = Method<Func<PrivateArea, bool>>(typeof(PrivateArea), "IsEnabled");
    private static readonly Func<PrivateArea, Vector3, float, bool> IsInsideCall = Method<Func<PrivateArea, Vector3, float, bool>>(typeof(PrivateArea), "IsInside", typeof(Vector3), typeof(float));
    private static readonly Action<PrivateArea, bool> FlashCall = Method<Action<PrivateArea, bool>>(typeof(PrivateArea), "FlashShield", typeof(bool));
    private static readonly Action<PrivateArea, bool> EnabledCall = Method<Action<PrivateArea, bool>>(typeof(PrivateArea), "SetEnabled", typeof(bool));
    private static readonly Action<PrivateArea> StatusCall = Method<Action<PrivateArea>>(typeof(PrivateArea), "UpdateStatus");
    private static readonly Action<Player> PiecesCall = Method<Action<Player>>(typeof(Player), "UpdateAvailablePiecesList");
    private static readonly Action<Player, bool> GhostValidCall = Method<Action<Player, bool>>(typeof(Player), "SetPlacementGhostValid", typeof(bool));
    private delegate void FindHover(Player player, out GameObject go, out Character creature);
    private static readonly FindHover HoverCall = Method<FindHover>(typeof(Player), "FindHoverObject", typeof(GameObject).MakeByRefType(), typeof(Character).MakeByRefType());
    private static readonly Func<ZRoutedRpc, long> ServerPeerCall = Method<Func<ZRoutedRpc, long>>(typeof(ZRoutedRpc), "GetServerPeerID");
    private static readonly Func<ZRoutedRpc, long, ZNetPeer> RoutedPeerCall = Method<Func<ZRoutedRpc, long, ZNetPeer>>(typeof(ZRoutedRpc), "GetPeer", typeof(long));
    private static readonly Func<ZNet, ZRpc, ZNetPeer> PeerCall = Method<Func<ZNet, ZRpc, ZNetPeer>>(typeof(ZNet), "GetPeer", typeof(ZRpc));
    private static readonly Func<Door, bool> DoorCall = Method<Func<Door, bool>>(typeof(Door), "CanInteract");

    private static T Method<T>(Type type, string name, params Type[] arguments) where T : Delegate =>
        AccessTools.MethodDelegate<T>(AccessTools.DeclaredMethod(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name));

    internal static ZNetView GetWardView(this PrivateArea area) => WardView(area);
    internal static Piece GetWardPiece(this PrivateArea area) => WardPiece(area);
    internal static Character GetHoverCreature(this Player player) => HoverCreature(player);
    internal static GameObject GetPlacementGhost(this Player player) => PlacementGhost(player);
    internal static ref Player.PlacementStatus GetPlacementStatusRef(this Player player) => ref PlacementStatus(player);
    internal static int GetPickupMask(this Player player) => PickupMask(player);
    internal static int GetRemoveRayMask(this Player player) => RemoveMask(player);
    internal static List<Minimap.PinData> GetPins(this Minimap map) => Pins(map);
    internal static ref bool[] GetVisibleIcons(this Minimap map) => ref VisibleIcons(map);
    internal static ref bool GetPinUpdateRequired(this Minimap map) => ref PinUpdate(map);
    internal static Dictionary<ZDOID, ZDO> GetWorldObjects(this ZDOMan manager) => WorldObjects(manager);
    internal static List<GameObject> GetSegments(this CircleProjector projector) => Segments(projector);
    internal static List<string> GetCommandList(this Terminal terminal) => Commands(terminal);
    internal static Splatform.Platform GetSteamPlatform(this ZNet net) => SteamPlatform(net);
    internal static bool IsAutoPickupEnabled => AutoPickupEnabled();
    internal static List<PrivateArea> GetAllAreas() => AllAreas();
    internal static bool IsEnabled(this PrivateArea area) => IsEnabledCall(area);
    internal static bool IsInside(this PrivateArea area, Vector3 point, float radius) => IsInsideCall(area, point, radius);
    internal static void FlashShield(this PrivateArea area, bool connected) => FlashCall(area, connected);
    internal static void SetEnabled(this PrivateArea area, bool enabled) => EnabledCall(area, enabled);
    internal static void UpdateStatus(this PrivateArea area) => StatusCall(area);
    internal static void UpdateAvailablePiecesList(this Player player) => PiecesCall(player);
    internal static void SetPlacementGhostValid(this Player player, bool valid) => GhostValidCall(player, valid);
    internal static void FindHoverObject(this Player player, out GameObject go, out Character creature) => HoverCall(player, out go, out creature);
    internal static long GetServerPeerID(this ZRoutedRpc rpc) => ServerPeerCall(rpc);
    internal static ZNetPeer GetPeer(this ZRoutedRpc rpc, long uid) => RoutedPeerCall(rpc, uid);
    internal static ZNetPeer GetPeer(this ZNet net, ZRpc rpc) => PeerCall(net, rpc);
    internal static bool CanInteract(this Door door) => DoorCall(door);
}
