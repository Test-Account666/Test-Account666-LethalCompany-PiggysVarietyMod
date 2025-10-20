using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PiggysVarietyMod.Dependencies;

namespace PiggysVarietyMod;

[BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class PiggysVarietyMod : BaseUnityPlugin {
    public static PiggysVarietyMod Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger { get; private set; } = null!;
    internal static Harmony? Harmony { get; set; }

    internal static void Patch() {
        Harmony ??= new(MyPluginInfo.PLUGIN_GUID);

        Logger.LogDebug("Patching...");

        Harmony.PatchAll();

        Logger.LogDebug("Finished patching!");
    }

    private void Awake() {
        Logger = base.Logger;
        Instance = this;

        if (DependencyChecker.IsLobbyCompatibilityInstalled()) {
            Logger.LogInfo("Found LobbyCompatibility Mod, initializing support :)");
            LobbyCompatibilitySupport.Initialize();
        }

        Patch();

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
    }

    internal static void Unpatch() {
        Logger.LogDebug("Unpatching...");

        Harmony?.UnpatchSelf();

        Logger.LogDebug("Finished unpatching!");
    }
}