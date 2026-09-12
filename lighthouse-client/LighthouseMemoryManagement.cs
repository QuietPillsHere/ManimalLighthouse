using System;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseMemoryManagement
{
    private static bool KeepEnabled(bool requested, bool replacementActive) => requested || replacementActive;

    internal static void BeforeSet(ref bool __0)
    {
        var enabled = KeepEnabled(__0, LighthouseSceneLoader.HasReplacement);

        if (enabled != __0)
        {
            Plugin.Log.LogInfo("Lighthouse memory: kept garbage collection enabled (managed heap " +
            GC.GetTotalMemory(false) / (1024 * 1024) + " MiB).");
        }

        __0 = enabled;
    }
}
