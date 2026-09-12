using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseRainRendering
{
    private static AssetBundle? _bundle;
    private static Shader? _shader;
    private static readonly List<Material> OwnedMaterials = [];
    private static readonly int FallingVector = Shader.PropertyToID("_FallingVector");
    private static readonly int Size = Shader.PropertyToID("_Size");
    private static readonly int RainDensity = Shader.PropertyToID("_RainDensity");

    internal static Material CreateRuntimeMaterial(Material source)
    {
        if (!source)
        {
            throw new InvalidOperationException("Native rain material is missing.");
        }

        if (OwnedMaterials.Contains(source))
        {
            return source;
        }

        var material = new Material(source) { name = "Manimal Lighthouse live rain" };

        try
        {
            if (!Restore(material))
            {
                throw new InvalidOperationException("Unexpected native rain shader.");
            }

            foreach (var property in new[] { "_RainDensity", "_MinAmbient", "_Intensity", "_SideSpeed", "_AlphaMult" })
            {
                if (source.HasFloat(property) && material.HasFloat(property))
                {
                    material.SetFloat(property, source.GetFloat(property));
                }
            }

            material.SetVector(FallingVector, source.GetVector(FallingVector));
            material.SetVector(Size, source.GetVector(Size));
            OwnedMaterials.Add(material);

            return material;
        }

        catch { UnityEngine.Object.Destroy(material); throw; }
    }

    private static bool Restore(Material material)
    {
        if (!material || !material.shader || material.shader.name != "Custom/RainDrops")
        {
            return false;
        }

        if (!_shader)
        {
            using (var resource = typeof(LighthouseRainRendering).Assembly.GetManifestResourceStream("Manimal.Lighthouse.Rain.bundle"))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("Embedded Lighthouse rain shader is missing.");
                }

                using (var memory = new MemoryStream())
                {
                    resource.CopyTo(memory);
                    _bundle = AssetBundle.LoadFromMemory(memory.ToArray());
                }
            }

            if (!_bundle || !_bundle)
            {
                throw new InvalidOperationException("Unable to load Lighthouse rain shader bundle.");
            }

            _shader = _bundle!.LoadAsset<Shader>("shaders/rain");
        }

        if ( !_shader || !_shader!.isSupported)
        {
            throw new InvalidOperationException("Lighthouse rain shader is unsupported.");
        }

        return Apply(material, _shader);
    }

    private static bool Apply(Material material, Shader shader)
    {
        if (!material || !material.shader || material.shader.name != "Custom/RainDrops" ||
            !shader || !shader.isSupported || shader.name != "Manimal/Lighthouse/RainDrops")
        {
            return false;
        }

        var queue = material.renderQueue;
        var keywords = material.shaderKeywords;
        
        // The native shader may omit density; retain the replacement's default in that case.
        float? density = material.HasFloat(RainDensity) ? material.GetFloat(RainDensity) : null;

        material.shader = shader;
        material.renderQueue = queue;
        material.shaderKeywords = keywords;
        if (density.HasValue && material.HasFloat(RainDensity))
        {
            material.SetFloat(RainDensity, density.Value);
        }

        return true;
    }

    internal static void Clear()
    {
        foreach (var material in OwnedMaterials)
        {
            if (material)
            {
                UnityEngine.Object.Destroy(material);
            }
        }

        OwnedMaterials.Clear();
        _shader = null;

        if (_bundle)
        {
            _bundle!.Unload(true);
        }

        _bundle = null;
    }
}
