using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseSceneCaches
{
    private static readonly FieldInfo[] Fields = typeof(LocationScene).GetFields(BindingFlags.Public | BindingFlags.Instance);

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(LocationScene), nameof(LocationScene.Awake)),
        prefix: new HarmonyMethod(typeof(LighthouseSceneCaches), nameof(BeforeAwake)));

    private static void BeforeAwake(LocationScene __instance)
    {
        var name = __instance.gameObject.scene.name;
        if (LighthouseSceneLoader.IsNativeDonor(name))
        {
            if (LighthouseSceneLoader.IsNativeAmbienceDonor(name))
            {
                // The donor contributes ambience only. Retain its ambient
                // lookup entries, excluding the old map's rooms and portals.
                foreach (var field in Fields)
                {
                    if (!field.FieldType.IsArray || field.FieldType.GetArrayRank() != 1) continue;
                    var element = field.FieldType.GetElementType()!;
                    if (!typeof(UnityEngine.Object).IsAssignableFrom(element) && !element.IsInterface) continue;
                    var kept = new System.Collections.Generic.List<object>();
                    if (field.GetValue(__instance) is Array values)
                        foreach (var value in values)
                            if (LighthouseAmbience.IsAmbientObject(value)) kept.Add(value);
                    var filtered = Array.CreateInstance(element, kept.Count);
                    for (var i = 0; i < kept.Count; i++) filtered.SetValue(kept[i], i);
                    field.SetValue(__instance, filtered);
                }
                return;
            }
            // Geographic airdrop points come from the replacement Scripts scene.
            __instance.AirdropPoints = Array.Empty<EFT.Airdrop.AirdropPoint>();
            return;
        }
        if (!LighthouseSceneLoader.Owns(name)) return;
        var removed = 0;
        foreach (var field in Fields)
        {
            if (!field.FieldType.IsArray || field.FieldType.GetArrayRank() != 1) continue;
            var element = field.FieldType.GetElementType()!;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(element) && !element.IsInterface) continue;
            var values = field.GetValue(__instance) as Array;
            if (values == null) { field.SetValue(__instance, Array.CreateInstance(element, 0)); continue; }
            var count = 0;
            foreach (var value in values) if (Exists(value)) count++;
            if (count == values.Length) continue;
            var compact = Array.CreateInstance(element, count);var index = 0;
            foreach (var value in values) if (Exists(value)) compact.SetValue(value, index++);
            removed += values.Length - count;
            field.SetValue(__instance, compact);
        }
        // These arrays are lookup caches, not indexed gameplay data. Omitted
        // test components must not enter the native registry as null entries.
        if (removed != 0) Plugin.Log.LogInfo("Lighthouse: removed " + removed + " unavailable component references from " + name + " lookup caches.");
    }

    private static bool Exists(object? value) => value != null && (!(value is UnityEngine.Object unity) || unity);
}
