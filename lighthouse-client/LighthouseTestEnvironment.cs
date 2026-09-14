using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseTestEnvironment
{
    private static readonly HashSet<string> OldGeography = new(StringComparer.Ordinal)
    {
        "EFT.EnvironmentEffect.DryPlane", "EFT.EnvironmentEffect.IndoorTrigger",
        "EFT.EnvironmentEffect.TriggerGroup", "EFT.Interactive.ObstacleCollider",
        "EFT.Airdrop.AirdropPoint", "WeatherObstacle"
    };

    internal static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!LighthouseSceneLoader.IsNativeDonor(scene.name))
        {
            return;
        }

        if (LighthouseSceneLoader.IsNativeAmbienceDonor(scene.name))
        {
            return;
        }

        var removed = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || !OldGeography.Contains(component.GetType().FullName))
                {
                    continue;
                }

                component.enabled = false;
                component.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(component);
                removed++;
            }
        }

        // the replacement Scripts scene carries the retail level borders and water volumes; the donor's
        // old copies still fence off areas the rework opened (the sniper peak camp)
        var fences = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Lighthouse_LevelBorders" || root.name == "Lighthouse_Watercolliders")
            {
                root.SetActive(false);
                fences++;
            }
        }

        Plugin.Log.LogInfo("Lighthouse test: native camera/weather loaded; removed " + removed + " old geographic components and " + fences + " old border/water roots. Retail volumes load from the replacement Scripts scene.");
    }
}
