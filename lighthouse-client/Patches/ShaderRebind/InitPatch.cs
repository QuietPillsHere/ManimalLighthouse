using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace Manimal.Lighthouse.Client.Patches.ShaderRebind;

internal sealed class InitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(AreaLight), nameof(AreaLight.Init));
    }

    [PatchPrefix]
    private static void Prefix(AreaLight __instance)
    {
        LighthouseShaderRebind.BeforeAreaInit(__instance);
    }
}
