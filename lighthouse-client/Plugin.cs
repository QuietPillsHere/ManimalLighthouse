using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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

    private void Awake()
    {
        Log = Logger;
        Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        AllowProbe = Config.Bind("Development", "AllowProbeContent", false, "Allow explicitly marked loader-test content. Leave disabled for normal raids.");
        AllowTest = Config.Bind("Development", "AllowTestContent", false, "Load the explicitly marked full-map test build, with native SPT environment managers and documented incomplete features.");
        CaptureRain = Config.Bind("Diagnostics", "CaptureRain", false, "Capture rain material/depth diagnostics. Expensive scene scans and GPU readback; enable only for an explicitly requested capture.");

        new Patches.SceneLoader.LoadPresetAsyncReversePatch().Enable();
        new Patches.SceneLoader.LoadPresetAsyncPatch().Enable();
        new Patches.Sidecars.GetPackedFilePathForGridPatch().Enable();
        new Patches.Sidecars.ConstructorPatch().Enable();
        new Patches.Sidecars.MetaXRAcousticMap.get_AbsoluteFilePathPatch().Enable();
        new Patches.Sidecars.MetaXRAcousticMap.LoadMapAsyncPatch().Enable();
        new Patches.Sidecars.MetaXRAcousticGeometry.get_AbsoluteFilePathPatch().Enable();
        new Patches.Sidecars.MetaXRAcousticGeometry.LoadGeometryAsyncPatch().Enable();
        new Patches.ShaderRebind.InitPatch().Enable();
        new Patches.ShaderRebind.OnGameStartedPatch().Enable();
        new Patches.KeyCleanup.ExitPatch().Enable();
        new Patches.TunnelDoor.OnEnablePatch().Enable();
        new Patches.RainDiagnostics.InitPatch().Enable();
        new Patches.PadlockAudio.PlaySoundPatch().Enable();
        new Patches.BundledScenes.LoadScenePatch().Enable();
        new Patches.SceneCaches.AwakePatch().Enable();
        new Patches.MemoryManagement.set_GCEnabledPatch().Enable();
        new Patches.Btr.LoadMapPathsConfigurationPatch().Enable();
        new Patches.Btr.LoadBTRVehiclePatch().Enable();
        new Patches.Btr.InitBTRServerPatch().Enable();
        new Patches.Btr.InitBTROnClientPatch().Enable();
        new Patches.FrameStats.OnGameStartedPatch().Enable();
        LighthouseAmbience.Install();

        new Patches.Ambience.InitPatch().Enable();

        SceneManager.sceneLoaded += LighthouseAmbience.SceneLoaded;
        SceneManager.sceneLoaded += LighthouseShaderRebind.SceneLoaded;
        SceneManager.sceneLoaded += LighthouseTestEnvironment.SceneLoaded;
        SceneManager.sceneUnloaded += LighthouseSceneLoader.SceneUnloaded;

        Log.LogInfo("Lighthouse loader registered. Matching client/server content is required; development test content requires explicit enablement.");

        if (LighthouseBindingVerification.Requested())
        {
            LighthouseBindingVerification.RunAndQuit();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= LighthouseSceneLoader.SceneUnloaded;
        SceneManager.sceneLoaded -= LighthouseAmbience.SceneLoaded;
        SceneManager.sceneLoaded -= LighthouseShaderRebind.SceneLoaded;
        SceneManager.sceneLoaded -= LighthouseTestEnvironment.SceneLoaded;

        LighthouseSceneLoader.ReleaseIfUnused();
    }
}
