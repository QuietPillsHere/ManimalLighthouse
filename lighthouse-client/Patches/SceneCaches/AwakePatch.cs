using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace Manimal.Lighthouse.Client.Patches.SceneCaches;

internal sealed class AwakePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationScene), nameof(LocationScene.Awake));
    }

    [PatchPrefix]
    private static void Prefix(LocationScene __instance)
    {
        LighthouseSceneCaches.BeforeAwake(__instance);
    }
}
