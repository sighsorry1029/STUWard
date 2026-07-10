using UnityEngine;

namespace STUWard;

internal static class WardGuiLayoutSettings
{
    private const float SettingsSliderWidth = 520f;
    private const float WarningToggleLabelGap = 12f;
    private const float WarningToggleLabelWidth = 120f;
    private const float WarningToggleY = 57f;

    internal static Vector2 GetPanelOffset() => new(0f, 0f);
    internal static Vector2 GetPanelSize() => new(1080f, 900f);
    internal static Vector2 GetTitlePosition() => new(0f, 382f);
    internal static Vector2 GetOwnerPosition() => new(-100f, 400f);
    internal static Vector2 GetGuildPosition() => new(-100f, 360f);
    internal static Vector2 GetCloseButtonPosition() => new(440f, 400f);
    internal static Vector2 GetPageArrowButtonPosition() => new(315f, 382f);
    internal static Vector2 GetRadiusLabelPosition() => new(-360f, 300f);
    internal static Vector2 GetRadiusSliderPosition() => new(20f, 300f);
    internal static Vector2 GetRadiusValuePosition() => new(360f, 300f);
    internal static Vector2 GetAreaMarkerSpeedLabelPosition() => new(-360f, 240f);
    internal static Vector2 GetAreaMarkerSpeedSliderPosition() => new(20f, 240f);
    internal static Vector2 GetAreaMarkerSpeedValuePosition() => new(360f, 240f);
    internal static Vector2 GetAreaMarkerAlphaLabelPosition() => new(-360f, 180f);
    internal static Vector2 GetAreaMarkerAlphaSliderPosition() => new(20f, 180f);
    internal static Vector2 GetAreaMarkerAlphaValuePosition() => new(360f, 180f);
    internal static Vector2 GetAutoCloseDelayLabelPosition() => new(-360f, 120f);
    internal static Vector2 GetAutoCloseDelaySliderPosition() => new(20f, 120f);
    internal static Vector2 GetAutoCloseDelayValuePosition() => new(360f, 120f);
    internal static Vector2 GetWarningEffectsLabelPosition() => new(-360f, 60f);
    internal static Vector2 GetWarningSoundLabelPosition(float toggleSize) => GetWarningToggleLabelPosition(GetSettingsSliderLeftEdge(), toggleSize);
    internal static Vector2 GetWarningSoundTogglePosition(float toggleSize) => GetWarningTogglePosition(GetSettingsSliderLeftEdge(), toggleSize);
    internal static Vector2 GetWarningFlashLabelPosition(float toggleSize) => GetWarningToggleLabelPosition(GetSettingsSliderCenterX(), toggleSize);
    internal static Vector2 GetWarningFlashTogglePosition(float toggleSize) => GetWarningTogglePosition(GetSettingsSliderCenterX(), toggleSize);
    internal static Vector2 GetRegisteredPlayersRemoveButtonPosition() => new(395f, 0f);
    internal static Vector2 GetRegisteredPlayersHeaderPosition() => new(0f, 10f);
    internal static Vector2 GetPermittedListPosition() => new(-15f, -190f);
    internal static Vector2 GetPermittedListSize() => new(960f, 360f);
    internal static Vector2 GetRestrictionsHeaderPosition() => new(0f, 285f);
    internal static Vector2 GetRestrictionListPosition() => new(-15f, -75f);
    internal static Vector2 GetRestrictionListSize() => new(960f, 620f);

    private static float GetSettingsSliderLeftEdge()
    {
        return GetRadiusSliderPosition().x - SettingsSliderWidth * 0.5f;
    }

    private static float GetSettingsSliderCenterX()
    {
        return GetRadiusSliderPosition().x;
    }

    private static Vector2 GetWarningTogglePosition(float leftEdge, float toggleSize)
    {
        return new Vector2(leftEdge + toggleSize * 0.5f, WarningToggleY);
    }

    private static Vector2 GetWarningToggleLabelPosition(float leftEdge, float toggleSize)
    {
        return new Vector2(leftEdge + toggleSize + WarningToggleLabelGap + WarningToggleLabelWidth * 0.5f, WarningToggleY);
    }
}
