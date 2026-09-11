using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseShaderRebind
{
    private static readonly Dictionary<string, Shader> NativeShaders = new(StringComparer.Ordinal);
    private static readonly Dictionary<Material, string> Materials = new();
    private static readonly HashSet<string> ReportedMissing = new(StringComparer.Ordinal);
    private static string? _terrainError;

    internal static void RegisterRenderingBundle(AssetBundle bundle)
    {
        LighthouseTerrainBindings.Load(bundle);
        var count = 0;
        foreach (var path in bundle.GetAllAssetNames())
        {
            if (!path.StartsWith("shaders/donor_", StringComparison.Ordinal)) continue;
            var shader = bundle.LoadAsset<Shader>(path);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Unsupported Lighthouse shader donor: " + path);
            if (!NativeShaders.TryGetValue(shader.name, out var native) || !native || !native.isSupported)
                NativeShaders[shader.name] = shader;
            count++;
        }
        if (count != 9) throw new InvalidOperationException("Incomplete Lighthouse compiled shader donors.");
        Plugin.Log.LogInfo("Lighthouse rendering update: 9 compiled shader donors and 18 terrain season/quality profiles loaded.");
    }

    internal static void ValidateTerrain()
    {
        if (_terrainError != null) throw new InvalidOperationException(_terrainError);
        LighthouseTerrainBindings.Validate();
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(AreaLight), nameof(AreaLight.Init)),
            prefix: new HarmonyMethod(typeof(LighthouseShaderRebind), nameof(BeforeAreaInit)));
        harmony.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)),
            postfix: new HarmonyMethod(typeof(LighthouseShaderRebind), nameof(RebindAll)));
    }

    internal static void CaptureNativeShaders()
    {
        Clear();
        // Run before opening replacement bundles: same-name ripped copies must
        // never become the donor selected by Shader.Find.
        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            if (shader && shader.isSupported && !NativeShaders.ContainsKey(shader.name)) NativeShaders.Add(shader.name, shader);
        foreach (var entry in ShadersFinder.SHADERS)
        {
            var shader = entry.Value ? entry.Value : Shader.Find(entry.Key);
            if (shader && shader.isSupported) NativeShaders[entry.Key] = shader;
        }
        foreach (var name in new[] { "Hidden/AreaLight", "Hidden/Shadowmap", "Hidden/BlurShadowmap" })
        {
            var shader = Shader.Find(name);
            if (shader && shader.isSupported) NativeShaders[name] = shader;
        }
    }

    private static Shader? Resolve(string name)
    {
        if (NativeShaders.TryGetValue(name, out var captured) && captured) return captured;
        // The game's initialized registry can supply late native shaders.
        if (ShadersFinder.SHADERS.TryGetValue(name, out var registered) && registered && registered.isSupported) return registered;
        return null;
    }

    internal static void Rebind(Material material)
    {
        if (!material) return;
        if (LighthouseMaterialRepairs.Repair(material, Resolve))
        {
            Materials.Remove(material);
            Plugin.Log.LogInfo("Restored Lighthouse material shader: " + material.name + " -> " + material.shader.name);
        }
        if (!Materials.TryGetValue(material, out var name))
        {
            if (!material.shader) return;
            name = material.shader.name;
            Materials.Add(material, name);
        }
        var native = Resolve(name);
        if (native)
        {
            // Keep textures, colors, render queue and authored keywords.
            if (material.shader != native)
            {
                var queue = material.renderQueue;
                var keywords = material.shaderKeywords;
                material.shader = native;
                material.renderQueue = queue;
                material.shaderKeywords = keywords;
            }
        }
        else if (ReportedMissing.Add(name + "|" + material.name))
            Plugin.Log.LogWarning("Lighthouse shader has no native donor: " + name + " (material: " + material.name + ")");
    }

    internal static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!LighthouseSceneLoader.Owns(scene.name)) return;
        try { LighthouseWaterBindings.Restore(scene); }
        catch (Exception error) { Plugin.Log.LogError(error); }
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                try
                {
                    if (LighthouseTerrainBindings.Restore(terrain))
                        Plugin.Log.LogInfo("Restored Lighthouse MicroSplat terrain: " + terrain.name + " (" + terrain.terrainData.alphamapTextureCount + " control maps).");
                    var grass = LighthouseGrassBindings.Restore(terrain);
                    if (grass != 0) Plugin.Log.LogInfo("Restored Lighthouse grass: " + terrain.name + " (main and optic managers).");
                }
                catch (Exception error) { _terrainError = error.ToString(); Plugin.Log.LogError(error); }
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials) if (material) Rebind(material);
            foreach (var light in root.GetComponentsInChildren<AreaLight>(true))
                if (light.SourceMaterial) Rebind(light.SourceMaterial);
        }
    }

    private static void BeforeAreaInit(AreaLight __instance)
    {
        if (!LighthouseSceneLoader.Owns(__instance.gameObject.scene.name)) return;
        __instance.m_ProxyShader = Resolve("Hidden/AreaLight");
        __instance.m_ShadowmapShader = Resolve("Hidden/Shadowmap");
        __instance.m_BlurShadowmapShader = Resolve("Hidden/BlurShadowmap");
        // Retail uses a different area-light renderer. Target Init requires
        // these components and receives its native shaders before creating caches.
        var go = __instance.gameObject;
        if (!go.GetComponent<MeshFilter>()) go.AddComponent<MeshFilter>();
        if (!go.GetComponent<MeshRenderer>()) go.AddComponent<MeshRenderer>();
        if (__instance.SourceMaterial) Rebind(__instance.SourceMaterial);
        if (!__instance.m_ProxyShader && ReportedMissing.Add("Hidden/AreaLight"))
            Plugin.Log.LogWarning("Lighthouse area lights are missing the native Hidden/AreaLight shader.");
    }

    internal static void RebindAll()
    {
        LighthouseTerrainBindings.Refresh();
        if (LighthouseGrassBindings.ManagerCount != 0) Plugin.Log.LogInfo(LighthouseGrassBindings.Diagnostics());
        // Only materials captured from owned replacement scenes. No global
        // renderer sweep and no permanent done-set that survives raid teardown.
        var pending = new List<Material>(Materials.Keys);
        foreach (var material in pending) if (material) Rebind(material);
    }

    internal static void Clear()
    {
        Materials.Clear(); NativeShaders.Clear(); ReportedMissing.Clear();
        _terrainError = null;
    }
}
