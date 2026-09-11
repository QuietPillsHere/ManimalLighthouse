using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EFT;
using Manimal.Lighthouse.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseSceneLoader
{
    private static readonly List<AssetBundle> Bundles = new List<AssetBundle>();
    private static readonly HashSet<string> SceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NativeDonorScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool _loading;
    private static ScenesPreset? _ownedPreset;
    internal static bool HasReplacement => _ownedPreset != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task LoadOriginal(LoadScenesFromPresetOperation instance, ScenesPreset preset) => throw new NotSupportedException("Harmony reverse patch is not installed.");

    public static bool Prefix(LoadScenesFromPresetOperation __instance, ScenesPreset preset, ref Task __result)
    {
        if (!preset || !string.Equals(preset.ServerName, "Lighthouse", StringComparison.OrdinalIgnoreCase)) return true;
        // A plugin without content is inert, matching the server's native mode.
        if (!File.Exists(Path.Combine(Plugin.Root, ManifestRules.FileName))) return true;
        __result = LoadReplacement(__instance, preset);
        return false;
    }
    private static async Task LoadReplacement(LoadScenesFromPresetOperation operation, ScenesPreset native)
    {
        if (_loading) { operation.SetFailed("Lighthouse replacement is already loading."); return; }
        ReleaseIfUnused();
        _loading = true;
        var succeeded = false;
        try
        {
            operation._cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(Plugin.Root, ManifestRules.FileName);
            var manifest = JsonConvert.DeserializeObject<ContentManifest>(File.ReadAllText(path))!;
            ManifestRules.Validate(manifest);
            if (!manifest.Ready && manifest.Mode != "test") throw new InvalidDataException("Lighthouse content conversion has not passed its release gates.");
            if (manifest.Mode == "test" && !Plugin.AllowTest.Value) throw new InvalidDataException("Full-map test content is disabled for this client.");
            if (manifest.Mode == "probe" && !Plugin.AllowProbe.Value) throw new InvalidDataException("Loader probe content is disabled for this client.");
            var request = RequestHandler.GetJsonAsync("/manimal/lighthouse/capability");
            var timeout = Task.Delay(TimeSpan.FromSeconds(15), operation._cancellationToken);
            if (await Task.WhenAny(request, timeout) != request) { operation._cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException("Lighthouse server compatibility check timed out."); }
            var response = JsonConvert.DeserializeObject<ServerCapability>(await request)!;
            ManifestRules.CheckCapability(manifest, ManifestRules.Hash(path), response);
            await Task.Run(() => {
                foreach (var bundle in manifest.Bundles) { operation._cancellationToken.ThrowIfCancellationRequested(); ManifestRules.VerifyFile(Plugin.Root, bundle.Path, bundle.Sha256); }
                foreach (var sidecar in manifest.Sidecars) { operation._cancellationToken.ThrowIfCancellationRequested(); ManifestRules.VerifyFile(Plugin.Root, sidecar.Path, sidecar.Sha256); }
            }, operation._cancellationToken);
            operation._cancellationToken.ThrowIfCancellationRequested();
            if (Bundles.Count != 0) throw new InvalidOperationException("Previous Lighthouse scenes are still loaded.");
            LighthouseShaderRebind.CaptureNativeShaders();
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Bundles)
            {
                var bundle = AssetBundle.LoadFromFile(ManifestRules.Resolve(Plugin.Root, entry.Path));
                if (!bundle) throw new InvalidDataException("Unity could not load " + entry.Path);
                Bundles.Add(bundle);
                if (entry.Path == "bundles/manimal_lighthouse_rendering.bundle") LighthouseShaderRebind.RegisterRenderingBundle(bundle);
                if (entry.Path == "bundles/manimal_lighthouse_grass.bundle") LighthouseGrassBindings.Load(bundle, LighthouseShaderRebind.Rebind);
                if (entry.Path == "bundles/manimal_lighthouse_water.bundle") LighthouseWaterBindings.Load(bundle);
                foreach (var scene in bundle.GetAllScenePaths()) if (!available.Add(scene)) throw new InvalidDataException("Duplicate bundled scene path: " + scene);
            }
            if (native._scenesResourceKeys.Count != manifest.Scenes.Count) throw new InvalidDataException("Native Lighthouse preset scene count changed.");
            var replacementKeys = new List<SceneResourceKey>();
            for (var i = 0; i < manifest.Scenes.Count; i++)
            {
                var mapping = manifest.Scenes[i]; var original = native._scenesResourceKeys[i];
                if (original.path != mapping.OriginalPath || original.onlyOffline != mapping.OnlyOffline) throw new InvalidDataException("Lighthouse preset order/flags do not match the audited build.");
                if (!available.Contains(mapping.ReplacementPath)) throw new InvalidDataException("Bundled replacement scene missing: " + mapping.ReplacementPath);
                if (manifest.Mode == "test" && (i == 0 || Path.GetFileNameWithoutExtension(original.path) == "Lighthouse_Sound"))
                {
                    // The target scene supplies a compatible camera, sky and
                    // weather system. The retail Scripts test scene retains
                    // the new map's geographic volumes and BTR paths. Sound
                    // supplies only the native ambient hierarchy; its old room,
                    // portal and radio roots are disabled by LighthouseAmbience.
                    replacementKeys.Add(original);
                    NativeDonorScenes.Add(Path.GetFileNameWithoutExtension(original.path));
                }
                replacementKeys.Add(new SceneResourceKey { path = mapping.ReplacementPath, rcid = mapping.ReplacementPath, onlyOffline = mapping.OnlyOffline });
                SceneNames.Add(Path.GetFileNameWithoutExtension(mapping.ReplacementPath));
            }
            _ownedPreset = UnityEngine.Object.Instantiate(native);
            InGameMemoryManagement.GCEnabled = true;
            _ownedPreset._scenesResourceKeys = replacementKeys;
            operation._totalScenesToLoad = _ownedPreset.GetTotalSceneCount();
            LighthouseSidecars.Activate(manifest);
            Plugin.Log.LogInfo("Loading coherent Lighthouse content " + manifest.ContentId + " (29 replacement scenes).");
            await LoadOriginal(operation, _ownedPreset);
            if (operation.Failed) throw new InvalidOperationException(operation.Error);
            var sceneError = operation.GetLoadError();
            if (!string.IsNullOrEmpty(sceneError)) throw new InvalidOperationException(sceneError);
            LighthouseAmbience.ValidateLoaded();
            LighthouseShaderRebind.RebindAll();
            LighthouseShaderRebind.ValidateTerrain();
            LighthouseGrassBindings.Validate();
            LighthouseWaterBindings.ValidateRenderer();
            if (LighthouseWaterBindings.GroupCount != 0) Plugin.Log.LogInfo("Lighthouse water: 2 groups, 7 surfaces registered with the native SPT renderer.");
            if (LighthouseGrassBindings.ManagerCount != 0) Plugin.Log.LogInfo(LighthouseGrassBindings.Diagnostics());
            operation._cancellationToken.ThrowIfCancellationRequested();
            succeeded = true;
        }
        catch (OperationCanceledException) { operation.SetCancelled(); }
        catch (Exception e) { Plugin.Log.LogError(e); operation.SetFailed("Manimal Lighthouse: " + e.Message); }
        finally
        {
            try { if (!succeeded) await UnloadPartialReplacement(); }
            catch (Exception e) { Plugin.Log.LogError("Lighthouse cleanup failed: " + e); }
            finally { _loading = false; ReleaseIfUnused(); }
        }
    }
    private static async Task UnloadPartialReplacement()
    {
        var scenes = new List<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++) { var scene = SceneManager.GetSceneAt(i); if (SceneNames.Contains(scene.name) || NativeDonorScenes.Contains(scene.name)) scenes.Add(scene); }
        foreach (var scene in scenes)
        {
            var unloading = SceneManager.UnloadSceneAsync(scene);
            if (unloading == null || unloading.isDone) continue;
            var completion = new TaskCompletionSource<bool>();
            unloading.completed += _ => completion.TrySetResult(true);
            await completion.Task;
        }
    }
    public static void SceneUnloaded(Scene scene) { if (SceneNames.Contains(scene.name) || NativeDonorScenes.Contains(scene.name)) ReleaseIfUnused(); }
    internal static bool Owns(string sceneName) => SceneNames.Contains(sceneName);
    internal static bool IsNativeDonor(string sceneName) => NativeDonorScenes.Contains(sceneName);
    internal static bool IsNativeAmbienceDonor(string sceneName) => IsNativeDonor(sceneName) && sceneName == "Lighthouse_Sound";
    public static void ReleaseIfUnused()
    {
        if (_loading) return;
        for (var i = 0; i < SceneManager.sceneCount; i++) if (SceneNames.Contains(SceneManager.GetSceneAt(i).name) || NativeDonorScenes.Contains(SceneManager.GetSceneAt(i).name)) return;
        LighthouseTerrainBindings.Clear();
        LighthouseGrassBindings.Clear();
        LighthouseWaterBindings.Clear();
        LighthouseRainRendering.Clear();
        foreach (var bundle in Bundles) if (bundle) bundle.Unload(true);
        Bundles.Clear(); SceneNames.Clear(); NativeDonorScenes.Clear();
        LighthouseSidecars.Clear();
        LighthouseShaderRebind.Clear();
        if (_ownedPreset) UnityEngine.Object.Destroy(_ownedPreset);
        _ownedPreset = null;
    }
}
