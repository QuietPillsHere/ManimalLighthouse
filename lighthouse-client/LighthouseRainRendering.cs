#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Manimal.Lighthouse.Client
{
    internal static class LighthouseRainRendering
    {
        private static AssetBundle? _bundle;
        private static Shader? _shader;
        private static readonly List<Material> OwnedMaterials = new List<Material>();

        internal static Material CreateRuntimeMaterial(Material source)
        {
            if (!source) throw new InvalidOperationException("Native rain material is missing.");
            if (OwnedMaterials.Contains(source)) return source;
            // EFT's CopyToPreventMaterialChangeInEditor returns the original
            // asset in the player. Clone explicitly to protect other maps.
            var material = new Material(source) { name = "Manimal Lighthouse live rain" };
            try
            {
                if (!Restore(material)) throw new InvalidOperationException("Unexpected native rain shader.");
                // Unity's material copy does not reliably retain uniforms that
                // are absent from the shader Properties block. Transfer the
                // controller's current state explicitly before binding it.
                foreach (var property in new[] { "_RainDensity", "_MinAmbient", "_Intensity", "_SideSpeed", "_AlphaMult" })
                    material.SetFloat(property, source.GetFloat(property));
                material.SetVector("_FallingVector", source.GetVector("_FallingVector"));
                material.SetVector("_Size", source.GetVector("_Size"));
                OwnedMaterials.Add(material);
                return material;
            }
            catch { UnityEngine.Object.Destroy(material); throw; }
        }

        internal static bool Restore(Material material)
        {
            if (!material || !material.shader || material.shader.name != "Custom/RainDrops") return false;
            if (!_shader)
            {
                using (var resource = typeof(LighthouseRainRendering).Assembly.GetManifestResourceStream("Manimal.Lighthouse.Rain.bundle"))
                {
                    if (resource == null) throw new InvalidOperationException("Embedded Lighthouse rain shader is missing.");
                    using (var memory = new MemoryStream())
                    {
                        resource.CopyTo(memory);
                        _bundle = AssetBundle.LoadFromMemory(memory.ToArray());
                    }
                }
                if (_bundle == null || !_bundle) throw new InvalidOperationException("Unable to load Lighthouse rain shader bundle.");
                _shader = _bundle!.LoadAsset<Shader>("shaders/rain");
            }
            if (_shader == null || !_shader || !_shader.isSupported) throw new InvalidOperationException("Lighthouse rain shader is unsupported.");
            return Apply(material, _shader);
        }

        internal static bool Apply(Material material, Shader shader)
        {
            if (!material || !material.shader || material.shader.name != "Custom/RainDrops" ||
                !shader || !shader.isSupported || shader.name != "Manimal/Lighthouse/RainDrops") return false;
            var queue = material.renderQueue;
            var keywords = material.shaderKeywords;
            // _RainDensity is a runtime uniform, absent from the shader's
            // serialized Properties block; Unity resets it on reassignment.
            var density = material.GetFloat("_RainDensity");
            // Native vertex/fragment bytecode and inputs remain intact. The
            // embedded donor only changes RGB destination blend from 1-alpha
            // to one, so low ambient rain cannot paint dark streaks over sky.
            material.shader = shader;
            material.renderQueue = queue;
            material.shaderKeywords = keywords;
            material.SetFloat("_RainDensity", density);
            return true;
        }

        internal static void Clear()
        {
            foreach (var material in OwnedMaterials) if (material) UnityEngine.Object.Destroy(material);
            OwnedMaterials.Clear();
            _shader = null;
            if (_bundle) _bundle!.Unload(true);
            _bundle = null;
        }
    }
}
