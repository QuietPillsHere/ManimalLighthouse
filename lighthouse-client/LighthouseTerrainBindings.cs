#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JBooth.MicroSplat;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
internal static class LighthouseTerrainBindings
{
#pragma warning disable CS0649
    [Serializable]
    public sealed class Profile
    {
        public string Season, Quality, Material;
        public string[] Keywords;
        public Color[] Values;
    }
    [Serializable]
    public sealed class Settings
    {
        public int Version;
        public Profile[] Profiles;
        public string[] Terrains;
    }
#pragma warning restore CS0649
    private sealed class ResourcesForProfile
    {
        public Material Material;
        public MicroSplatKeywords Keywords;
        public MicroSplatPropData Properties;
    }
    private static readonly Dictionary<string, ResourcesForProfile> Profiles = new();
    private static readonly Dictionary<Terrain, string> Bound = new();
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal);
    private static Settings _settings;
    private static AssetBundle _bundle;
    private static Shader _basemap;
    private static readonly int NormalSao = Shader.PropertyToID("_NormalSAO");
    private static readonly int Control0 = Shader.PropertyToID("_Control0");
    private static readonly int Diffuse = Shader.PropertyToID("_Diffuse");

    public static void Load(AssetBundle bundle)
    {
        var json = bundle.LoadAsset<TextAsset>("rendering.json");

        if (!json)
        {
            throw new InvalidOperationException("Lighthouse rendering binding table is missing.");
        }

        _settings = JsonUtility.FromJson<Settings>(json.text);

        if (_settings.Version != 1 || _settings.Profiles.Length != 18 || _settings.Terrains.Length != 6)
        {
            throw new InvalidOperationException("Unexpected Lighthouse terrain rendering schema.");
        }

        _bundle = bundle;
        _basemap = bundle.LoadAsset<Shader>("shaders/terrain_basemap");

        if (!_basemap || !_basemap.isSupported)
        {
            throw new InvalidOperationException("Lighthouse terrain basemap shader is unsupported.");
        }

        foreach (var name in _settings.Terrains)
        {
            Names.Add(name);
        }
    }

    public static bool Restore(Terrain terrain)
    {
        if (!_bundle || !terrain || !Names.Contains(terrain.name))
        {
            return false;
        }

        var key = MicroSplatObject.currentSeason + "/" + MicroSplatObject.currentQuality;

        if (Bound.TryGetValue(terrain, out var previous) && previous == key)
        {
            return false;
        }

        if (!terrain.terrainData || terrain.terrainData.alphamapTextureCount == 0)
        {
            throw new InvalidOperationException("Lighthouse terrain has no control textures: " + terrain.name);
        }

        if (!Profiles.TryGetValue(key, out var resources))
        {
            Profile selected = null;

            foreach (var profile in _settings.Profiles)
            {
                if (profile.Season + "/" + profile.Quality != key) { continue; }

                selected = profile;
                break;
            }

            if (selected == null)
            {
                throw new InvalidOperationException("Missing terrain season/quality profile: " + key);
            }

            var material = _bundle.LoadAsset<Material>(selected.Material);

            if (!material || !material.shader || !material.shader.isSupported || material.passCount == 0)
            {
                throw new InvalidOperationException("Unsupported native terrain material: " + selected.Material);
            }

            if (!material.GetTexture(Diffuse) || !material.GetTexture(NormalSao))
            {
                throw new InvalidOperationException("Missing terrain texture arrays: " + selected.Material);
            }

            resources = new ResourcesForProfile
            {
                Material = material,
                Keywords = ScriptableObject.CreateInstance<MicroSplatKeywords>(),
                Properties = ScriptableObject.CreateInstance<MicroSplatPropData>()
            };
            resources.Keywords.keywords = [.. selected.Keywords];
            resources.Properties.values = selected.Values;
            Profiles.Add(key, resources);
        }

        var component = terrain.GetComponent<MicroSplatTerrain>();
        var active = terrain.gameObject.activeSelf;

        // AddComponent on an active object invokes Awake before fields can
        // be assigned. The native component expects all three sets to exist.
        if (!component && active)
        {
            terrain.gameObject.SetActive(false);
        }

        try
        {
            if (!component)
            {
                component = terrain.gameObject.AddComponent<MicroSplatTerrain>();
            }

            component.templateMaterialHigh = new MicroSplatObject.MaterialsSet();
            component.templateMaterialNormal = new MicroSplatObject.MaterialsSet();
            component.templateMaterialLow = new MicroSplatObject.MaterialsSet();
            component.terrain = terrain;
            component.templateMaterial = resources.Material;
            component.keywordSO = resources.Keywords;
            component.propData = resources.Properties;
            component.baseMapShader = _basemap;
            component.patchBoundsMultiplier = Vector3.one;
            component.Sync();

            if (!terrain.materialTemplate || !terrain.materialTemplate.GetTexture(Control0))
            {
                throw new InvalidOperationException("Terrain material synchronization failed: " + terrain.name);
            }

            Bound[terrain] = key;
        }
        finally
        {
            if (active && !terrain.gameObject.activeSelf)
            {
                terrain.gameObject.SetActive(true);
            }
        }

        return true;
    }

    public static void Validate()
    {
        if (!_bundle)
        {
            return;
        }

        var restored = new HashSet<string>(StringComparer.Ordinal);

        foreach (var terrain in Bound.Keys)
        {
            if (terrain)
            {
                restored.Add(terrain.name);
            }
        }

        if (restored.Count != Names.Count)
        {
            throw new InvalidOperationException("Restored " + restored.Count + " of " + Names.Count + " Lighthouse terrain tiles.");
        }
    }

    public static void Refresh()
    {
        var terrains = new List<Terrain>(Bound.Keys);

        foreach (var terrain in terrains)
        {
            if (terrain)
            {
                Restore(terrain);
            }
        }
    }

    public static void Clear()
    {
        foreach (var resources in Profiles.Values)
        {
            if (resources.Properties.propTex)
            {
                UnityEngine.Object.Destroy(resources.Properties.propTex);
            }

            UnityEngine.Object.Destroy(resources.Properties);
            UnityEngine.Object.Destroy(resources.Keywords);
        }

        Profiles.Clear();
        Bound.Clear();
        Names.Clear();
        _bundle = null;
        _basemap = null;
        _settings = null;
    }
}