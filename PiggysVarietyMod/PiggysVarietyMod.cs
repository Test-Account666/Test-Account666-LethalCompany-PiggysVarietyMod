using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using PiggysVarietyMod.Dependencies;
using PiggysVarietyMod.Items;
using PiggysVarietyMod.Utils;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PiggysVarietyMod;

[BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class PiggysVarietyMod : BaseUnityPlugin {
    public static PiggysVarietyMod Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger { get; private set; } = null!;
    internal static Harmony? Harmony { get; set; }

    public static ItemInputs InputActionsInstance = new ItemInputs();

    private void Awake() {
        Logger = base.Logger;
        Instance = this;

        if (DependencyChecker.IsLobbyCompatibilityInstalled()) {
            Logger.LogInfo("Found LobbyCompatibility Mod, initializing support :)");
            LobbyCompatibilitySupport.Initialize();
        }

        string assetsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "piggyvarietymod");
        AssetBundle bundle = AssetBundle.LoadFromFile(assetsDir);
        string fullpath = "Assets/LethalCompany/Mods/plugins/PiggysVarietyMod/";

        InitRifle(bundle, fullpath);

        Patch();

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
    }

    private static void InitRifle(AssetBundle bundle, string fullpath) 
    {
        Item rifleItem = bundle.LoadAsset<Item>(fullpath + "Items/Rifle/Rifle.asset");
        Item rifleMagItem = bundle.LoadAsset<Item>(fullpath + "Items/Rifle/Magazine.asset");

        RifleScript SpawnedRifleScript = rifleItem.spawnPrefab.AddComponent<RifleScript>();
        SpawnedRifleScript.itemProperties = rifleItem;
        SpawnedRifleScript.rifleLocalAnimator = bundle.LoadAsset<RuntimeAnimatorController>(fullpath + "Players/Animations/PlayerAnimator.controller");
        SpawnedRifleScript.rifleRemoteAnimator = bundle.LoadAsset<RuntimeAnimatorController>(fullpath + "Players/Animations/OtherPlayerAnimator.controller");
        SpawnedRifleScript.SFX_FireM4 = bundle.LoadAsset<AudioClip>(fullpath + "Items/Rifle/Audio/FireM4.ogg");
        SpawnedRifleScript.SFX_ReloadM4 = bundle.LoadAsset<AudioClip>(fullpath + "Items/Rifle/Audio/ReloadM4.ogg");
        SpawnedRifleScript.SFX_TriggerM4 = bundle.LoadAsset<AudioClip>(fullpath + "Items/Rifle/Audio/TriggerM4.ogg");
        SpawnedRifleScript.SFX_SwitchFireModeM4 = bundle.LoadAsset<AudioClip>(fullpath + "Items/Rifle/Audio/SwitchFireModeM4.ogg");

        NetworkPrefabs.RegisterNetworkPrefab(rifleItem.spawnPrefab);
        NetworkPrefabs.RegisterNetworkPrefab(rifleMagItem.spawnPrefab);

        Utilities.FixMixerGroups(rifleItem.spawnPrefab);
        Utilities.FixMixerGroups(rifleMagItem.spawnPrefab);

        LethalLib.Modules.Items.RegisterScrap(rifleItem, 15, Levels.LevelTypes.All);
        LethalLib.Modules.Items.RegisterScrap(rifleMagItem, 20, Levels.LevelTypes.All);

        LethalLib.Modules.Items.RegisterShopItem(rifleItem, 666);
        LethalLib.Modules.Items.RegisterShopItem(rifleMagItem, 20);
    }

    internal static void Patch()
    {
        Harmony ??= new(MyPluginInfo.PLUGIN_GUID);

        Logger.LogDebug("Patching...");

        Harmony.PatchAll();

        Logger.LogDebug("Finished patching!");
    }


    internal static void Unpatch() {
        Logger.LogDebug("Unpatching...");

        Harmony?.UnpatchSelf();

        Logger.LogDebug("Finished unpatching!");
    }
}