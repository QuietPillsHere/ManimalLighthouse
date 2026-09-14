using System;
using System.Reflection;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

// waypoints swaps the whole scene navmesh for its lighthouse-navmesh.bundle at bot init.
// that bake is the old layout, so it fights the rework's converted navmesh. skip only the
// swap on our raids; its door-link and path patches read the live scene and stay on.
internal static class LighthouseWaypointsCompatibility
{
    private const string WaypointPatchTypeName = "DrakiaXYZ.Waypoints.Patches.WaypointPatch";
    private static ModulePatch? _patch;
    private static bool _initialized;

    internal static void EnableIfAvailable()
    {
        if (_initialized) return;
        _initialized = true;
        var patchType = FindLoadedType(WaypointPatchTypeName);
        if (patchType is null) return;
        var inject = patchType.GetMethod("InjectNavmesh", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(GameWorld)], null);
        if (inject is null)
        {
            Plugin.Log.LogWarning("Lighthouse: Waypoints navmesh injection API was not recognized; its custom navmesh will still replace the reworked map's.");
            return;
        }
        _patch = new InjectNavmeshPatch(inject);
        _patch.Enable();
        Plugin.Log.LogInfo("Lighthouse: Waypoints custom navmesh is skipped on the reworked Lighthouse; the scene's own navmesh is used.");
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null) return type;
            }
            catch (ReflectionTypeLoadException)
            {
                // a partially loadable unrelated assembly shouldn't kill an optional lookup
            }
        }
        return null;
    }

    private static bool IsOwnedLighthouseRaid(GameWorld? gameWorld)
    {
        if (!gameWorld || !string.Equals(gameWorld!.LocationId, "Lighthouse", StringComparison.OrdinalIgnoreCase)) return false;
        if (!LighthouseSceneLoader.HasReplacement) return false;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            if (LighthouseSceneLoader.Owns(SceneManager.GetSceneAt(i).name)) return true;
        }
        return false;
    }

    private sealed class InjectNavmeshPatch(MethodBase target) : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => target;

        [PatchPrefix]
        private static bool Prefix(GameWorld gameWorld)
        {
            if (!IsOwnedLighthouseRaid(gameWorld)) return true;
            Plugin.Log.LogInfo("Lighthouse: skipped Waypoints navmesh replacement for the reworked map.");
            return false;
        }
    }
}
