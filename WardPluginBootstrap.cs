using Jotunn.Managers;
using LocalizationManager;

namespace STUWard;

internal static class WardPluginBootstrap
{
    internal static void InitializeCore()
    {
        Localizer.Load();
    }

    internal static void InitializeFeatures()
    {
        ManagedWardConfigFileService.Initialize();
        WardItemPrefabPolicy.Initialize();
        WardOwnership.Initialize();

        PrefabManager.OnVanillaPrefabsAvailable += RegisterStuWardPiece;
        RegisterStuWardPiece();
    }

    internal static void Shutdown()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterStuWardPiece;
        DoorRpcUseDoorPatch.Reset();
        WardPluginConfigBindings.UnbindAll();
        WardItemPrefabPolicy.Shutdown();
        ManagedWardConfigFileService.Shutdown();
        GuildsCompat.TryShutdownHooks();
        Localizer.Unload();
    }

    private static void RegisterStuWardPiece()
    {
        StuWardPrefab.Register();
        StuWardPrefab.ApplyRecipeSettings();
    }
}
