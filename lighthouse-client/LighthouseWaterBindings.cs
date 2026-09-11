#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WaterSSR;

namespace Manimal.Lighthouse.Client
{
    internal static class LighthouseWaterBindings
    {
#pragma warning disable CS0649
        [Serializable] public sealed class TextureBinding { public string Field, Asset; }
        [Serializable] public sealed class Profile { public string Root, Json; public string[] Targets; public TextureBinding[] Textures; }
        [Serializable] public sealed class Config { public int Version; public string Scene; public Profile[] Profiles; }
#pragma warning restore CS0649
        private static Config _config;
        private static readonly List<WaterSettings> Settings = new List<WaterSettings>();
        private static readonly List<WaterForSSRv3> Groups = new List<WaterForSSRv3>();
        private static string _error;
        public static int GroupCount { get { return Groups.Count; } }

        public static void Load(AssetBundle bundle)
        {
            var text = bundle.LoadAsset<TextAsset>("water.json");
            if (!text) throw new InvalidOperationException("Lighthouse water settings are missing.");
            _config = JsonUtility.FromJson<Config>(text.text);
            if (_config.Version != 1 || _config.Profiles.Length != 2) throw new InvalidOperationException("Unexpected Lighthouse water schema.");
            foreach (var profile in _config.Profiles)
            {
                var settings = JsonUtility.FromJson<WaterSettings>(profile.Json);
                foreach (var binding in profile.Textures)
                {
                    var texture = bundle.LoadAsset<Texture2D>(binding.Asset);
                    if (!texture) throw new InvalidOperationException("Missing Lighthouse water texture: " + binding.Asset);
                    switch (binding.Field)
                    {
                        case "_rippleTexture": settings._rippleTexture = texture; break;
                        case "_normals": settings._normals = texture; break;
                        case "_normalsDetails": settings._normalsDetails = texture; break;
                        case "_foam": settings._foam = texture; break;
                        default: throw new InvalidOperationException("Unknown water texture binding: " + binding.Field);
                    }
                }
                settings.IsDirty = true;
                Settings.Add(settings);
            }
        }

        public static void Restore(Scene scene)
        {
            if (_config == null || scene.name != _config.Scene || Groups.Count != 0) return;
            try
            {
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < _config.Profiles.Length; i++)
                {
                    var profile = _config.Profiles[i];Transform owner = null;
                    foreach (var root in roots) if (root.name == profile.Root) { owner = root.transform; break; }
                    if (!owner) throw new InvalidOperationException("Missing water group: " + profile.Root);
                    var targets = new WaterObject[profile.Targets.Length];
                    for (int j = 0; j < targets.Length; j++)
                    {
                        var target = Resolve(roots, profile.Targets[j]);
                        var filter = target ? target.GetComponent<MeshFilter>() : null;
                        var renderer = target ? target.GetComponent<MeshRenderer>() : null;
                        if (!filter || !filter.sharedMesh || !renderer) throw new InvalidOperationException("Missing water mesh: " + profile.Targets[j]);
                        targets[j] = new WaterObject { Filter = filter, Renderer = renderer, GameObject = target.gameObject };
                    }
                    // Assign every field before OnEnable registers with the native renderer.
                    var go = new GameObject("Manimal water binding");go.SetActive(false);go.transform.SetParent(owner, false);
                    var group = go.AddComponent<WaterForSSRv3>();group._settings = Settings[i];group._targets = targets;
                    Groups.Add(group);go.SetActive(true);
                }
            }
            catch (Exception e) { _error = e.ToString(); throw; }
        }

        private static Transform Resolve(GameObject[] roots, string path)
        {
            Transform parent = null;
            foreach (var part in path.Split('/'))
            {
                var split = part.LastIndexOf('~');var name = part.Substring(0, split);var wanted = int.Parse(part.Substring(split + 1));
                var found = 0;Transform next = null;
                if (!parent)
                {
                    foreach (var root in roots) if (root.name == name && found++ == wanted) { next = root.transform; break; }
                }
                else
                {
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (child.name == name && found++ == wanted) { next = child; break; }
                    }
                }
                if (!next) return null;parent = next;
            }
            return parent;
        }

        public static int ValidateBindings()
        {
            if (_config == null) return 0;
            if (_error != null) throw new InvalidOperationException(_error);
            if (Groups.Count != 2) throw new InvalidOperationException("Lighthouse water groups were not restored.");
            var count = 0;
            foreach (var group in Groups)
            {
                if (!group || !group.isActiveAndEnabled) throw new InvalidOperationException("Inactive Lighthouse water group.");
                foreach (var target in group._targets)
                {
                    if (!target.Filter || !target.Filter.sharedMesh || !target.Renderer || !target.GameObject)
                        throw new InvalidOperationException("Incomplete Lighthouse water target.");
                    count++;
                }
            }
            if (count != 7) throw new InvalidOperationException("Expected seven Lighthouse water surfaces.");
            return count;
        }

        public static void ValidateRenderer()
        {
            if (ValidateBindings() == 0) return;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<WaterRendererv3>())
                if (renderer.isActiveAndEnabled && renderer.gameObject.scene.IsValid() && renderer._shader && renderer._shader.isSupported) return;
            throw new InvalidOperationException("The native SPT water renderer is unavailable.");
        }

        public static void Clear() { Groups.Clear();Settings.Clear();_config = null;_error = null; }
    }
}
