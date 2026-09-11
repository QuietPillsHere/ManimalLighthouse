#nullable disable
using System;
using UnityEngine;

namespace Manimal.Lighthouse.Client
{
    internal static class LighthouseMaterialRepairs
    {
        public static bool Repair(Material material, Func<string, Shader> resolve)
        {
            if (!material || (material.shader && material.shader.name != "Hidden/InternalErrorShader")) return false;
            var name = material.name;
            foreach (var suffix in new[] { " (Instance)", " (Clone)" })
                if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);
            if (name != "Black") return false;
            // Retail resources.assets:158 -> :2752, and SPT :26 -> :1998:
            // both use Legacy Shaders/Diffuse. The export replaced that shader
            // with unity_builtin_extra:7, which resolves in the editor but not
            // in the target player. This is slot 1 behind the mini_TAC_2_TTP
            // vent slats (both units, LOD0/1), also shared by event wires.
            var shader = resolve("Legacy Shaders/Diffuse");
            if (!shader || !shader.isSupported) return false;
            material.shader = shader;
            material.renderQueue = -1;
            return true;
        }
    }
}
