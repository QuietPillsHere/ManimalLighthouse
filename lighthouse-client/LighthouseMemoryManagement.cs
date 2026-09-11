using System;
using HarmonyLib;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseMemoryManagement
{
    internal static void Install(Harmony harmony)
    {
        var setter = AccessTools.PropertySetter(typeof(InGameMemoryManagement), nameof(InGameMemoryManagement.GCEnabled));
        if (setter == null) throw new MissingMethodException("InGameMemoryManagement.GCEnabled setter");
        harmony.Patch(setter, prefix: new HarmonyMethod(typeof(LighthouseMemoryManagement), nameof(BeforeSet)));
    }

    // EFT normally disables collection at raid startup. The full backport and
    // installed mods can exhaust the managed heap while garbage cannot be reclaimed.
    // Intercept the game's policy setter, with no per-frame scene-name allocations.
    internal static bool KeepEnabled(bool requested, bool replacementActive) => requested || replacementActive;

    private static void BeforeSet(ref bool __0)
    {
        var enabled = KeepEnabled(__0, LighthouseSceneLoader.HasReplacement);
        if (enabled != __0)
            Plugin.Log.LogInfo("Lighthouse memory: kept garbage collection enabled (managed heap " +
                (GC.GetTotalMemory(false) / (1024 * 1024)) + " MiB).");
        __0 = enabled;
    }
}
