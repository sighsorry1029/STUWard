using System;

namespace STUWard;

internal static class WardLocalization
{
    internal const string PieceNameToken = "$stuw_piece_name";
    internal const string PieceDescriptionToken = "$stuw_piece_desc";
    internal const string UiTitleToken = "$stuw_ui_title";
    internal const string UiOwnerToken = "$stuw_ui_owner";
    internal const string UiGuildToken = "$stuw_ui_guild";
    internal const string UiCloseToken = "$stuw_ui_close";
    internal const string UiRadiusToken = "$stuw_ui_radius";
    internal const string UiRangeSpeedToken = "$stuw_ui_range_speed";
    internal const string UiRangeBrightnessToken = "$stuw_ui_range_brightness";
    internal const string UiDoorCloseDelayToken = "$stuw_ui_door_close_delay";
    internal const string UiWarningEffectsToken = "$stuw_ui_warning_effects";
    internal const string UiWarningSoundToken = "$stuw_ui_warning_sound";
    internal const string UiWarningFlashToken = "$stuw_ui_warning_flash";
    internal const string UiRegisteredPlayersToken = "$stuw_ui_registered_players";
    internal const string UiNoRegisteredPlayersToken = "$stuw_ui_no_registered_players";
    internal const string UiRemoveToken = "$stuw_ui_remove";
    internal const string UiOffToken = "$stuw_ui_off";
    internal const string UiRadiusValueToken = "$stuw_ui_radius_value";
    internal const string UiDelayValueToken = "$stuw_ui_delay_value";
    internal const string UiRegisteredPlayerFormatToken = "$stuw_ui_registered_player_format";
    internal const string HoverSettingsToken = "$stuw_hover_settings";
    internal const string MessageBlockedItemToken = "$stuw_msg_blocked_item";
    internal const string MessageBuildingDamageProtectedToken = "$stuw_msg_building_damage_protected";
    internal const string MessageOverlapToken = "$stuw_msg_overlap";
    internal const string MessageLimitWithMaxToken = "$stuw_msg_limit_with_max";
    internal const string ShortcutUnboundToken = "$stuw_shortcut_unbound";

    internal const string PieceNameFallback = "Ward";
    internal const string PieceDescriptionFallback = "Configurable ward with extended protection.";
    internal const string UiTitleFallback = "Ward Settings";
    internal const string UiOwnerFallback = "Owner: {0}";
    internal const string UiGuildFallback = "Guild: {0}";
    internal const string UiCloseFallback = "Close";
    internal const string UiRadiusFallback = "Ward radius";
    internal const string UiRangeSpeedFallback = "Range speed";
    internal const string UiRangeBrightnessFallback = "Range brightness";
    internal const string UiDoorCloseDelayFallback = "Door close delay";
    internal const string UiWarningEffectsFallback = "Warning effects";
    internal const string UiWarningSoundFallback = "Sound";
    internal const string UiWarningFlashFallback = "Flash";
    internal const string UiRegisteredPlayersFallback = "Registered players";
    internal const string UiNoRegisteredPlayersFallback = "No registered players.";
    internal const string UiRemoveFallback = "Remove";
    internal const string UiOffFallback = "Off";
    internal const string UiRadiusValueFallback = "{0} m";
    internal const string UiDelayValueFallback = "{0} s";
    internal const string UiRegisteredPlayerFormatFallback = "{0} / {1} / {2}";
    internal const string HoverSettingsFallback = "[<color=yellow><b>{0}</b></color>] Ward settings";
    internal const string MessageBlockedItemFallback = "A ward prevents using this item here.";
    internal const string MessageBuildingDamageProtectedFallback = "An active Ward prevents damaging protected structures.";
    internal const string MessageOverlapFallback = "Another Ward is too close.";
    internal const string MessageLimitWithMaxFallback = "Ward limit reached (max {0})";
    internal const string ShortcutUnboundFallback = "Unbound";

    internal static string Localize(string token, string fallback)
    {
        var localization = Localization.instance;
        if (localization == null)
        {
            return fallback;
        }

        var localized = localization.Localize(token);
        var unresolvedBracketToken = token.StartsWith("$", StringComparison.Ordinal)
            ? $"[{token[1..]}]"
            : $"[{token}]";

        return string.IsNullOrWhiteSpace(localized) || localized == token || localized == unresolvedBracketToken ? fallback : localized;
    }

    internal static string LocalizeFormat(string token, string fallback, params object[] args)
    {
        var format = Localize(token, fallback);
        return args == null || args.Length == 0 ? format : string.Format(format, args);
    }
}
