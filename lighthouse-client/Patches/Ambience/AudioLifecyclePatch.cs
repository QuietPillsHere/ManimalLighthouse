using System.Reflection;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Lighthouse.Client.Patches.Ambience;

internal sealed class AudioLifecyclePatch : ModulePatch
{
    private readonly MethodInfo _targetMethod;

    internal AudioLifecyclePatch(MethodInfo targetMethod)
    {
        _targetMethod = targetMethod;
    }

    protected override MethodBase GetTargetMethod()
    {
        return _targetMethod;
    }

    [PatchPrefix]
    private static bool Prefix(MonoBehaviour __instance)
    {
        return LighthouseAmbience.BeforeLifecycle(__instance);
    }
}
