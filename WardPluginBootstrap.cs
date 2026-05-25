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
        WardGuiLayoutSettings.Bind();
        ManagedWardConfigFileService.Initialize();
        WardItemPrefabPolicy.Initialize();
        WardOwnership.Initialize();

        PrefabManager.OnVanillaPrefabsAvailable += RegisterStuWardPiece;
        RegisterStuWardPiece();
    }

    internal static void Update()
    {
        ManagedWardRuntimeLifecycle.Update();
    }

    internal static void Shutdown()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterStuWardPiece;
        WardPluginConfigBindings.UnbindAll();
        WardItemPrefabPolicy.Shutdown();
        ManagedWardConfigFileService.Shutdown();
        GuildsCompat.TryShutdownHooks();
        Localizer.Unload();
    }

    internal static void ApplyRecipeSettings()
    {
        StuWardPrefab.ApplyRecipeSettings();
    }

    private static void RegisterStuWardPiece()
    {
        StuWardPrefab.Register();
        ApplyRecipeSettings();
    }
}
