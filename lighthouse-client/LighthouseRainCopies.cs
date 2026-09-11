#nullable enable
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client
{
    internal static class LighthouseRainCopies
    {
        internal static int DisableSavedCopies(Scene scene, MeshRenderer live)
        {
            if (!live || live.gameObject.scene != scene || !live.transform.parent) return 0;
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer == live || !renderer.enabled || !renderer.name.StartsWith("RainFall(", StringComparison.Ordinal)) continue;
                    // The audited donor contains eight saved siblings and one
                    // saved root. Never touch a different controller's children.
                    if (renderer.transform.parent && renderer.transform.parent != live.transform.parent) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter && filter.sharedMesh && filter.sharedMesh.name != "RainFallDrops GetMesh") continue;
                    var material = renderer.sharedMaterial;
                    if (material && (!material.shader || material.shader.name != "Custom/RainDrops")) continue;
                    renderer.enabled = false;
                    count++;
                }
            return count;
        }
    }
}
