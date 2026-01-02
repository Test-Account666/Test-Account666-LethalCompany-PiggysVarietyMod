using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PiggysVarietyMod.Dependencies;
using PiggysVarietyMod.Utils;
using static TestAccountCore.AssetLoader;
using static TestAccountCore.Netcode;

namespace PiggysVarietyMod;

[BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("TestAccount666.TestAccountCore")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class PiggysVarietyMod : BaseUnityPlugin {
    public static PiggysVarietyMod Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger { get; private set; } = null!;
    internal static Harmony? Harmony { get; set; }

    public static readonly ItemInputs INPUT_ACTIONS_INSTANCE = new();

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

        var assembly = Assembly.GetExecutingAssembly();

        ExecuteNetcodePatcher(assembly);
        LoadAssetBundle("PiggyVarietyMod.AssetBundle", assembly);

        Patch();

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
    }

    private void LoadAssetBundle(string fileName, Assembly assembly) {
        LoadBundle(assembly, fileName);
        LoadItems(Config);
        LoadShopItems(Config);
        LoadHallwayHazards(Config);
    }

    internal static void Unpatch() {
        Logger.LogDebug("Unpatching...");

        Harmony?.UnpatchSelf();

        Logger.LogDebug("Finished unpatching!");
    }
}