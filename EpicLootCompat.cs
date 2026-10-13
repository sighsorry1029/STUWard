using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace STUWard;

internal static class EpicLootCompat
{
    internal const string PluginGuid = "randyknapp.mods.epicloot";
    private static MethodInfo _getUi = null!;
    private static MethodInfo _getTable = null!;
    private static MethodInfo _cancel = null!;

    internal static void TryPatch(Harmony harmony)
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin) || plugin.Instance == null) return;
        Apply(harmony, plugin.Instance.GetType().Assembly);
        Plugin.Log.LogInfo("EpicLoot enchanting table ward protection enabled.");
    }

    // Resolve the complete optional contract before installing any hooks.
    internal static void Apply(Harmony harmony, Assembly assembly)
    {
        var hooks = ResolveHooks(assembly);
        var attempted = new List<(MethodInfo Target, MethodInfo Prefix)>();
        try
        {
            foreach (var hook in hooks)
            {
                attempted.Add(hook);
                harmony.Patch(hook.Target, prefix: new HarmonyMethod(hook.Prefix) { priority = Priority.First });
            }
        }
        catch
        {
            foreach (var hook in attempted)
            {
                try { harmony.Unpatch(hook.Target, hook.Prefix); }
                catch (Exception exception) { Plugin.Log.LogWarning($"EpicLoot rollback failed for {hook.Target.Name}: {exception.Message}"); }
            }
            throw;
        }
    }

    internal static List<(MethodInfo Target, MethodInfo Prefix)> ResolveHooks(Assembly assembly)
    {
        var table = assembly.GetType("EpicLoot_UnityLib.EnchantingTable", throwOnError: true)!;
        var ui = assembly.GetType("EpicLoot_UnityLib.EnchantingTableUI", throwOnError: true)!;
        var panel = assembly.GetType("EpicLoot_UnityLib.EnchantingTableUIPanelBase", throwOnError: true)!;
        if (!typeof(Component).IsAssignableFrom(table) || !typeof(Component).IsAssignableFrom(ui))
            throw new InvalidOperationException("Unsupported EpicLoot table/UI types.");
        var getUi = RequireMethod(ui, "get_instance", true, ui);
        var getTable = RequireMethod(ui, "get_SourceTable", false, table);
        var cancel = RequireMethod(panel, "Cancel", false, typeof(void));
        if (!getUi.IsPublic || !getTable.IsPublic || !cancel.IsPublic || !cancel.IsVirtual)
            throw new InvalidOperationException("Unsupported EpicLoot UI accessors.");
        var hooks = new List<(MethodInfo Target, MethodInfo Prefix)>();
        void Add(MethodInfo target, string prefix) => hooks.Add((target,
            typeof(EpicLootCompat).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)!));
        Add(RequireMethod(ui, "Show", true, typeof(void), table), nameof(ShowPrefix));
        foreach (var name in new[] { "SacrificeUI", "ConvertUI", "EnchantUI", "AugmentUI", "DisenchantUI", "RuneUI", "UpgradeTableUI" })
        {
            var type = assembly.GetType("EpicLoot_UnityLib." + name, throwOnError: true)!;
            if (!panel.IsAssignableFrom(type)) throw new InvalidOperationException("Unsupported EpicLoot panel: " + name);
            Add(RequireMethod(type, "DoMainAction", false, typeof(void)), nameof(ActionPrefix));
        }
        Add(RequireMethod(table, "RPC_TableUpgradeRequest", false, typeof(void),
            typeof(long), typeof(ZDOID), typeof(int), typeof(int)), nameof(UpgradePrefix));

        _getUi = getUi;
        _getTable = getTable;
        _cancel = cancel;
        return hooks;
    }

    private static MethodInfo RequireMethod(Type type, string name, bool isStatic, Type result, params Type[] parameters)
    {
        var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                    (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method == null || method.ReturnType != result || method.IsAbstract || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static bool ShowPrefix(Component __0, bool __runOriginal)
    {
        if (!__runOriginal) return true;
        return CanUseTable(__0);
    }

    private static bool ActionPrefix(object __instance, bool __runOriginal)
    {
        if (!__runOriginal) return true;
        var ui = _getUi.Invoke(null, null);
        var table = ui == null ? null : _getTable.Invoke(ui, null) as Component;
        if (CanUseTable(table)) return true;
        // Keep SourceTable intact: the enclosing panel Update continues after this call.
        // Paid augment choices and completed upgrade responses are deliberately not patched.
        _cancel.Invoke(__instance, null);
        return false;
    }

    private static bool CanUseTable(Component? table)
    {
        if (table == null || Player.m_localPlayer == null) return false;
        return WardAccess.TryBlockVoid(WardRestrictionOptions.CraftingStations, table, Player.m_localPlayer);
    }

    private static bool UpgradePrefix(Component __instance, long __0, ZDOID __1, int __2, int __3, bool __runOriginal)
    {
        if (!__runOriginal) return true;
        var view = __instance.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner()) return false;
        // EpicLoot's normal request always identifies this table. Do not authorize
        // one receiver and then let its RPC mutate an unrelated table.
        var allowed = __0 != 0L && __1.Equals(view.GetZDO().m_uid);
        if (allowed)
        {
            var point = __instance.transform.position;
            var candidates = WardAccess.GetCandidateManagedWards(point, 0f, requireEnabled: true);
            foreach (var area in candidates)
            {
                if (area == null || !area.IsInside(point, 0f) ||
                    !WardSettings.HasRestriction(WardSettings.GetConfiguration(area), WardRestrictionOptions.CraftingStations)) continue;
                // An unresolved ID must not become WardAccess's local-player fallback.
                allowed = WardOwnership.TryResolveAuthoritativePlayerIdFromSender(__0, out var playerId) &&
                    !WardAccess.EvaluateRestrictionAccessAgainstCandidates(
                        WardRestrictionOptions.CraftingStations, point, 0f, playerId, candidates, flash: false).IsDenied;
                break;
            }
        }
        if (allowed) return true;
        if (__0 != 0L) view.InvokeRPC(__0, "el.TableUpgradeResponse", __1, __2, __3, false);
        return false;
    }
}
