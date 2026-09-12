using System;
using System.Collections.Generic;
using System.IO;
using Koenigz.PerfectCulling.EFT;
using Manimal.Lighthouse.Shared;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseSidecars
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);

    public static void Activate(ContentManifest manifest)
    {
        Files.Clear();

        foreach (var entry in manifest.Sidecars)
        {
            const string prefix = "StreamingAssets/";

            if (!entry.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Sidecar must preserve its StreamingAssets relative path.");
            }

            Files.Add(entry.Path[prefix.Length..], ManifestRules.Resolve(Plugin.Root, entry.Path));
        }
    }
    public static void Clear() => Files.Clear();
    private static string Resolve(Component owner, string relative)
    {
        if (!owner || !LighthouseSceneLoader.Owns(owner.gameObject.scene.name))
        {
            return "";
        }

        relative = relative.Replace('\\', '/');
        ManifestRules.ValidateRelativePath(relative);

        return !Files.TryGetValue(relative, out var path) ? throw new InvalidDataException("Replacement Lighthouse requested an undeclared sidecar: " + relative) : path;
    }
    internal static void PackedPath(PerfectCullingAdaptiveGrid grid, ref string __result)
    {
        var path = Resolve(grid, "Culling_Data/" + grid.GridHash + "_packed_cull.bytes");

        if (path.Length != 0)
        {
            __result = path;
        }
    }
    internal static void AudioPath(ref string dataPath, MonoBehaviour runner)
    {
        var path = Resolve(runner, dataPath);

        if (path.Length != 0)
        {
            dataPath = path;
        }
    }
    internal static void XrAbsolutePath(Component __instance, ref string __result)
    {
        if (!__instance || !LighthouseSceneLoader.Owns(__instance.gameObject.scene.name))
        {
            return;
        }

        var streaming = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var original = Path.GetFullPath(__result);

        if (!original.StartsWith(streaming, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Replacement Lighthouse acoustic path is outside its declared StreamingAssets source.");
        }

        __result = Resolve(__instance, original[streaming.Length..]);
    }
    internal static void XrAsyncPath(Component __instance, ref string __0)
    {
        var path = Resolve(__instance, __0);

        if (path.Length == 0)
        {
            return;
        }

        __0 = Path.GetRelativePath(Application.streamingAssetsPath, path).Replace('\\', '/');
    }
}
