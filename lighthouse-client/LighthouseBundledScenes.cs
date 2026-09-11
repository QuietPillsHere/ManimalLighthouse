using System;
using System.IO;
using System.Threading.Tasks;
using EFT;
using EFT.AssetsManager;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseBundledScenes
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(AssetsManagerExtension), nameof(AssetsManagerExtension.LoadScene)),
            prefix: new HarmonyMethod(typeof(LighthouseBundledScenes), nameof(BeforeLoad)));
    }

    private static bool BeforeLoad(ResourceKey resourceKey, LoadSceneMode loadSceneMode, bool allowSceneActivation,
        Action<float> progressCallback, ref LoadSceneOperation __result)
    {
        if (resourceKey == null || !LighthouseSceneLoader.Owns(Path.GetFileNameWithoutExtension(resourceKey.path))) return true;
        var operation = new BundledSceneOperation();
        __result = operation;
        _ = operation.Begin(resourceKey.path, loadSceneMode, allowSceneActivation, progressCallback);
        return false;
    }

    private sealed class BundledSceneOperation : LoadSceneOperation
    {
        internal async Task Begin(string path, LoadSceneMode mode, bool activate, Action<float> progress)
        {
            Info = "Manimal Lighthouse bundle scene: " + path;
            try
            {
                AsyncOperation = SceneManager.LoadSceneAsync(path, mode);
                if (AsyncOperation == null) throw new InvalidDataException("Bundled scene could not load: " + path);
                AsyncOperation.allowSceneActivation = activate;
                while (!AsyncOperation.isDone)
                {
                    progress?.Invoke(AsyncOperation.progress);
                    if (!activate && AsyncOperation.progress >= 0.9f) break;
                    await Task.Yield();
                }
                progress?.Invoke(1f);
                Succeed = true;
            }
            catch (Exception error)
            {
                Error = error.Message;
                Plugin.Log.LogError(Info + ": " + error);
            }
        }
    }
}
