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
    internal const string UiAutoCloseToken = "$stuw_ui_auto_close";
    internal const string UiWarningSoundToken = "$stuw_ui_warning_sound";
    internal const string UiWarningFlashToken = "$stuw_ui_warning_flash";
    internal const string UiAreaMarkerRotationToken = "$stuw_ui_area_marker_rotation";
    internal const string UiRegisteredPlayersToken = "$stuw_ui_registered_players";
    internal const string UiUnregisteredPlayersToken = "$stuw_ui_unregistered_players";
    internal const string UiRestrictionsToken = "$stuw_ui_restrictions";
    internal const string UiRestrictionForcedToken = "$stuw_ui_restriction_forced";
    internal const string UiRestrictionDoorsToken = "$stuw_ui_restriction_doors";
    internal const string UiRestrictionPortalsToken = "$stuw_ui_restriction_portals";
    internal const string UiRestrictionPickupToken = "$stuw_ui_restriction_pickup";
    internal const string UiRestrictionPlacedConsumablesToken = "$stuw_ui_restriction_placed_consumables";
    internal const string UiRestrictionItemStandsToken = "$stuw_ui_restriction_item_stands";
    internal const string UiRestrictionArmorStandsToken = "$stuw_ui_restriction_armor_stands";
    internal const string UiRestrictionContainersToken = "$stuw_ui_restriction_containers";
    internal const string UiRestrictionCraftingStationsToken = "$stuw_ui_restriction_crafting_stations";
    internal const string UiRestrictionTameablesAndSaddlesToken = "$stuw_ui_restriction_tameables_and_saddles";
    internal const string UiNoRegisteredPlayersToken = "$stuw_ui_no_registered_players";
    internal const string UiNoUnregisteredPlayersToken = "$stuw_ui_no_unregistered_players";
    internal const string UiRecentPlayersLoadingToken = "$stuw_ui_recent_players_loading";
    internal const string UiRecentPlayersErrorToken = "$stuw_ui_recent_players_error";
    internal const string UiSearchPlayersToken = "$stuw_ui_search_players";
    internal const string UiNoMatchingPlayersToken = "$stuw_ui_no_matching_players";
    internal const string UiRemoveToken = "$stuw_ui_remove";
    internal const string UiAddToken = "$stuw_ui_add";
    internal const string UiOnlineToken = "$stuw_ui_online";
    internal const string UiLastSeenUnavailableToken = "$stuw_ui_last_seen_unavailable";
    internal const string UiLastSeenJustNowToken = "$stuw_ui_last_seen_just_now";
    internal const string UiLastSeenMinutesAgoToken = "$stuw_ui_last_seen_minutes_ago";
    internal const string UiLastSeenHoursAgoToken = "$stuw_ui_last_seen_hours_ago";
    internal const string UiLastSeenDaysAgoToken = "$stuw_ui_last_seen_days_ago";
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
    internal const string UiAutoCloseFallback = "Door auto-close";
    internal const string UiWarningSoundFallback = "Ward alert sound";
    internal const string UiWarningFlashFallback = "Ward alert visual effect";
    internal const string UiAreaMarkerRotationFallback = "Ward range rotation";
    internal const string UiRegisteredPlayersFallback = "Registered players";
    internal const string UiUnregisteredPlayersFallback = "Unregistered players";
    internal const string UiRestrictionsFallback = "Restrictions";
    internal const string UiRestrictionForcedFallback = "Forced";
    internal const string UiRestrictionDoorsFallback = "Doors";
    internal const string UiRestrictionPortalsFallback = "Portals";
    internal const string UiRestrictionPickupFallback = "Pickup";
    internal const string UiRestrictionPlacedConsumablesFallback = "Consumables";
    internal const string UiRestrictionItemStandsFallback = "Item stands";
    internal const string UiRestrictionArmorStandsFallback = "Armor stands";
    internal const string UiRestrictionContainersFallback = "Containers";
    internal const string UiRestrictionCraftingStationsFallback = "Crafting stations";
    internal const string UiRestrictionTameablesAndSaddlesFallback = "Tames";
    internal const string UiNoRegisteredPlayersFallback = "No registered players.";
    internal const string UiNoUnregisteredPlayersFallback = "No unregistered players seen in the last 28 days.";
    internal const string UiRecentPlayersLoadingFallback = "Loading recent players...";
    internal const string UiRecentPlayersErrorFallback = "Could not load recent players.";
    internal const string UiSearchPlayersFallback = "Search players...";
    internal const string UiNoMatchingPlayersFallback = "No matching players.";
    internal const string UiRemoveFallback = "Remove";
    internal const string UiAddFallback = "Add";
    internal const string UiOnlineFallback = "Online";
    internal const string UiLastSeenUnavailableFallback = "Last seen unavailable";
    internal const string UiLastSeenJustNowFallback = "Seen just now";
    internal const string UiLastSeenMinutesAgoFallback = "Seen {0}m ago";
    internal const string UiLastSeenHoursAgoFallback = "Seen {0}h ago";
    internal const string UiLastSeenDaysAgoFallback = "Seen {0}d ago";
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
