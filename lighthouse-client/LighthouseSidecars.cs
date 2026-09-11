using System;
using System.Collections.Generic;
using System.IO;
using Audio.SpatialSystem;
using HarmonyLib;
using Koenigz.PerfectCulling.EFT;
using Manimal.Lighthouse.Shared;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseSidecars
{
    private static readonly Dictionary<string,string> Files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    public static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PackedCullingGridData), nameof(PackedCullingGridData.GetPackedFilePathForGrid)),
            postfix: new HarmonyMethod(typeof(LighthouseSidecars), nameof(PackedPath)));
        harmony.Patch(AccessTools.Constructor(typeof(SpatialAudioDataLoader), new[] { typeof(string),typeof(MonoBehaviour) }),
            prefix: new HarmonyMethod(typeof(LighthouseSidecars), nameof(AudioPath)));
        InstallXr(harmony,typeof(MetaXRAcousticMap),"LoadMapAsync");
        InstallXr(harmony,typeof(MetaXRAcousticGeometry),"LoadGeometryAsync");
    }
    private static void InstallXr(Harmony harmony,Type type,string asyncMethod)
    {
        harmony.Patch(AccessTools.PropertyGetter(type,"AbsoluteFilePath"),
            postfix:new HarmonyMethod(typeof(LighthouseSidecars),nameof(XrAbsolutePath)));
        harmony.Patch(AccessTools.Method(type,asyncMethod,new[] {typeof(string)}),
            prefix:new HarmonyMethod(typeof(LighthouseSidecars),nameof(XrAsyncPath)));
    }
    public static void Activate(ContentManifest manifest)
    {
        Files.Clear();
        foreach (var entry in manifest.Sidecars)
        {
            const string prefix="StreamingAssets/";
            if (!entry.Path.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidDataException("Sidecar must preserve its StreamingAssets relative path.");
            Files.Add(entry.Path.Substring(prefix.Length),ManifestRules.Resolve(Plugin.Root,entry.Path));
        }
    }
    public static void Clear() => Files.Clear();
    private static string Resolve(Component owner,string relative)
    {
        if (!owner || !LighthouseSceneLoader.Owns(owner.gameObject.scene.name)) return "";
        relative=relative.Replace('\\','/');
        ManifestRules.ValidateRelativePath(relative);
        if (!Files.TryGetValue(relative,out var path)) throw new InvalidDataException("Replacement Lighthouse requested an undeclared sidecar: "+relative);
        return path;
    }
    private static void PackedPath(PerfectCullingAdaptiveGrid grid,ref string __result)
    {
        var path=Resolve(grid,"Culling_Data/"+grid.GridHash+"_packed_cull.bytes");
        if (path.Length!=0) __result=path;
    }
    private static void AudioPath(ref string dataPath,MonoBehaviour runner)
    {
        var path=Resolve(runner,dataPath);
        if (path.Length!=0) dataPath=path;
    }
    private static void XrAbsolutePath(Component __instance,ref string __result)
    {
        if(!__instance || !LighthouseSceneLoader.Owns(__instance.gameObject.scene.name))return;
        var streaming=Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var original=Path.GetFullPath(__result);
        if(!original.StartsWith(streaming,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Replacement Lighthouse acoustic path is outside its declared StreamingAssets source.");
        __result=Resolve(__instance,original.Substring(streaming.Length));
    }
    private static void XrAsyncPath(Component __instance,ref string __0)
    {
        var path=Resolve(__instance,__0);
        if(path.Length==0)return;
        // Target Meta XR extracts the subpath after StreamingAssets, then
        // concatenates it with the game's StreamingAssets directory. Translate
        // that subpath back to the verified mod file for its asynchronous read.
        __0=Path.GetRelativePath(Application.streamingAssetsPath,path).Replace('\\','/');
    }
}
