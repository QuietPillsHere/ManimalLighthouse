#nullable disable
using System;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseMaterialRepairs
{
    public static bool Repair(Material material, Func<string, Shader> resolve)
    {
        if (!material || material.shader && material.shader.name != "Hidden/InternalErrorShader")
        {
            return false;
        }

        var name = material.name;

        foreach (var suffix in new[] { " (Instance)", " (Clone)" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
            }
        }

        if (name != "Black")
        {
            return false;
        }
        
        var shader = resolve("Legacy Shaders/Diffuse");

        if (!shader || !shader.isSupported)
        {
            return false;
        }

        material.shader = shader;
        material.renderQueue = -1;

        return true;
    }
}