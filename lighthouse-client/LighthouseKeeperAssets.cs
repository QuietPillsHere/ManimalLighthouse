using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

// the keeper timelines and gate sounds already ship with SPT; an embedded container bundle
// references those installed objects by file, the way the map bundle borrows native textures
internal static class LighthouseKeeperAssets
{
    private static readonly Dictionary<string, UnityEngine.Object> Cache = new(StringComparer.Ordinal);
    private static AssetBundle? _bundle;

    internal static UnityEngine.Object? Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached) && cached)
        {
            return cached;
        }

        if (!_bundle)
        {
            using (var stream = typeof(LighthouseKeeperAssets).Assembly.GetManifestResourceStream("Manimal.Lighthouse.Keeper.bundle")
                ?? throw new InvalidOperationException("Embedded keeper bundle is missing."))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                _bundle = AssetBundle.LoadFromMemory(memory.ToArray());
            }

            if (!_bundle)
            {
                Plugin.Log.LogError("Lighthouse: embedded keeper bundle failed to load.");
                return null;
            }
        }

        var asset = _bundle!.LoadAsset(name);

        if (!asset)
        {
            Plugin.Log.LogError("Lighthouse: keeper bundle could not resolve " + name + " from the installed SPT data.");
            return null;
        }

        Cache[name] = asset;
        return asset;
    }
}
