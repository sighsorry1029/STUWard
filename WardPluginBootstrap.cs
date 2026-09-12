using System;
using LocalizationManager;

namespace STUWard;

internal static class WardPluginBootstrap
{
    internal static void InitializeCore()
    {
        WardUiResources.PrepareAssetCatalog();
        Localizer.Load();
    }
    internal static void InitializeFeatures() => WardItemPrefabPolicy.Initialize();

    internal static void Shutdown()
    {
        // Each resource must be released even when an earlier service failed to
        // initialize (including a failed type initializer).
        Cleanup(() => WardGuiController.Instance?.Shutdown());
        Cleanup(DoorRpcUseDoorPatch.Reset);
        Cleanup(WardPluginConfigBindings.UnbindAll);
        Cleanup(() => WardItemPrefabPolicy.Shutdown());
        Cleanup(WardRecentPlayers.Shutdown);
        Cleanup(ManagedWardConfigFileService.Shutdown);
        Cleanup(GuildsCompat.TryShutdownHooks);
        Cleanup(WardGroupCompat.Shutdown);
        Cleanup(StuWardPrefab.Shutdown);
        Cleanup(Localizer.Unload);
    }

    private static void Cleanup(Action release)
    {
        try { release(); }
        catch (Exception exception) { Plugin.Log.LogWarning($"STUWard cleanup failed: {exception}"); }
    }
}
