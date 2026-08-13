using UnityEngine;

namespace STUWard;

internal static class WardGuiLayoutSettings
{
    internal static Vector2 GetPanelOffset() => new(0f, 0f);
    internal static Vector2 GetPanelSize() => new(1080f, 900f);
    internal static Vector2 GetTitlePosition() => new(0f, 400f);
    internal static Vector2 GetTitleSize() => new(300f, 56f);
    internal static Vector2 GetOwnerPosition() => new(-340f, 400f);
    internal static Vector2 GetGuildPosition() => new(-340f, 362f);
    internal static Vector2 GetOwnerGuildLabelSize() => new(360f, 34f);
    internal static Vector2 GetCloseButtonPosition() => new(440f, 400f);
    internal static Vector2 GetPageArrowButtonPosition() => new(315f, 400f);

    internal static float GetBehaviorToggleSize() => 30f;
    internal static Vector2 GetBehaviorControlsGridPosition() => new(-15f, 245f);
    internal static Vector2 GetBehaviorControlsGridSize() => new(960f, 118f);

    internal static Vector2 GetRegisteredPlayersRemoveButtonPosition() => new(395f, 0f);
    internal static Vector2 GetPlayerListHeaderLabelSize() => new(630f, 40f);
    internal static Vector2 GetPlayerSearchSize() => new(280f, 36f);
    internal static Vector2 GetRegisteredPlayersHeaderPosition() => new(-180f, 315f);
    internal static Vector2 GetRegisteredPlayersSearchPosition() => new(325f, 315f);
    internal static Vector2 GetPermittedListPosition() => new(-15f, 145f);
    internal static Vector2 GetPermittedListSize() => new(960f, 280f);

    internal static Vector2 GetRecentPlayersHeaderPosition() => new(-180f, -30f);
    internal static Vector2 GetRecentPlayersSearchPosition() => new(325f, -30f);
    internal static Vector2 GetRecentPlayersListPosition() => new(-15f, -230f);
    internal static Vector2 GetRecentPlayersListSize() => new(960f, 340f);

    internal static Vector2 GetRestrictionsHeaderPosition() => new(0f, 150f);
    internal static Vector2 GetRestrictionListPosition() => new(-15f, -133f);
    internal static Vector2 GetRestrictionListSize() => new(960f, 504f);
    internal static Vector2 GetRestrictionCellSize() => new(452f, 48f);
    internal static Vector2 GetRestrictionCellSpacing() => new(8f, 6f);
}
