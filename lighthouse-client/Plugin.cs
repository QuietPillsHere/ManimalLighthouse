using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using Manimal.Lighthouse.Shared;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

[BepInPlugin(ModIdentity.Guid, ModIdentity.ClientName, ModIdentity.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log = null!;
    internal static string Root = "";
    internal static ConfigEntry<bool> AllowProbe = null!;
    internal static ConfigEntry<bool> AllowTest = null!;
    internal static ConfigEntry<bool> CaptureRain = null!;
    private Harmony? _harmony;
    private void Awake()
    {
        Log = Logger; Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        AllowProbe = Config.Bind("Development", "AllowProbeContent", false, "Allow explicitly marked loader-test content. Leave disabled for normal raids.");
        AllowTest = Config.Bind("Development", "AllowTestContent", false, "Load the explicitly marked full-map test build, with native SPT environment managers and documented incomplete features.");
        CaptureRain = Config.Bind("Diagnostics", "CaptureRain", false, "Capture rain material/depth diagnostics. Expensive scene scans and GPU readback; enable only for an explicitly requested capture.");
        var original = AccessTools.Method(typeof(LoadScenesFromPresetOperation), nameof(LoadScenesFromPresetOperation.LoadPresetAsync));
        _harmony = new Harmony(ModIdentity.Guid);
        _harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(LighthouseSceneLoader), nameof(LighthouseSceneLoader.LoadOriginal))).Patch();
        _harmony.Patch(original, prefix: new HarmonyMethod(typeof(LighthouseSceneLoader), nameof(LighthouseSceneLoader.Prefix)));
        LighthouseSidecars.Install(_harmony);
        LighthouseShaderRebind.Install(_harmony);
        LighthouseKeyCleanup.Install(_harmony);
        LighthouseTunnelDoor.Install(_harmony);
        LighthouseRainDiagnostics.Install(_harmony);
        LighthousePadlockAudio.Install(_harmony);
        LighthouseBundledScenes.Install(_harmony);
        LighthouseSceneCaches.Install(_harmony);
        LighthouseMemoryManagement.Install(_harmony);
        LighthouseBtr.Install(_harmony);
        LighthouseFrameStats.Install(_harmony);
        LighthouseAmbience.Install(_harmony);
        SceneManager.sceneLoaded += LighthouseAmbience.SceneLoaded;
        SceneManager.sceneLoaded += LighthouseShaderRebind.SceneLoaded;
        SceneManager.sceneLoaded += LighthouseTestEnvironment.SceneLoaded;
        SceneManager.sceneUnloaded += LighthouseSceneLoader.SceneUnloaded;
        Log.LogInfo("Lighthouse loader registered. Matching client/server content is required; development test content requires explicit enablement.");
        if(LighthouseBindingVerification.Requested())LighthouseBindingVerification.RunAndQuit();
    }
    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= LighthouseSceneLoader.SceneUnloaded;
        SceneManager.sceneLoaded -= LighthouseAmbience.SceneLoaded;
        SceneManager.sceneLoaded -= LighthouseShaderRebind.SceneLoaded;
        SceneManager.sceneLoaded -= LighthouseTestEnvironment.SceneLoaded;
        _harmony?.UnpatchSelf();
        LighthouseSceneLoader.ReleaseIfUnused();
    }
}



