using System;
using System.Reflection;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseSceneCaches
{
    private static readonly FieldInfo[] Fields = typeof(LocationScene).GetFields(BindingFlags.Public | BindingFlags.Instance);

    internal static void BeforeAwake(LocationScene __instance)
    {
        var name = __instance.gameObject.scene.name;

        if (LighthouseSceneLoader.IsNativeDonor(name))
        {
            if (LighthouseSceneLoader.IsNativeAmbienceDonor(name))
            {
                foreach (var field in Fields)
                {
                    if (!field.FieldType.IsArray || field.FieldType.GetArrayRank() != 1)
                    {
                        continue;
                    }

                    var element = field.FieldType.GetElementType()!;

                    if (!typeof(UnityEngine.Object).IsAssignableFrom(element) && !element.IsInterface)
                    {
                        continue;
                    }

                    var kept = new System.Collections.Generic.List<object>();

                    if (field.GetValue(__instance) is Array values)
                    {
                        foreach (var value in values)
                        {
                            if (LighthouseAmbience.IsAmbientObject(value))
                            {
                                kept.Add(value);
                            }
                        }
                    }

                    var filtered = Array.CreateInstance(element, kept.Count);

                    for (var i = 0; i < kept.Count; i++)
                    {
                        filtered.SetValue(kept[i], i);
                    }

                    field.SetValue(__instance, filtered);
                }

                return;
            }

            __instance.AirdropPoints = [];

            return;
        }

        if (!LighthouseSceneLoader.Owns(name))
        {
            return;
        }

        var removed = 0;

        foreach (var field in Fields)
        {
            if (!field.FieldType.IsArray || field.FieldType.GetArrayRank() != 1)
            {
                continue;
            }

            var element = field.FieldType.GetElementType()!;

            if (!typeof(UnityEngine.Object).IsAssignableFrom(element) && !element.IsInterface)
            {
                continue;
            }

            if (field.GetValue(__instance) is not Array values)
            {
                field.SetValue(__instance, Array.CreateInstance(element, 0));
                continue;
            }

            var count = 0;

            foreach (var value in values)
            {
                if (Exists(value))
                {
                    count++;
                }
            }

            if (count == values.Length)
            {
                continue;
            }

            var compact = Array.CreateInstance(element, count);
            var index = 0;

            foreach (var value in values)
            {
                if (Exists(value))
                {
                    compact.SetValue(value, index++);
                }
            }

            removed += values.Length - count;
            field.SetValue(__instance, compact);
        }

        if (removed != 0)
        {
            Plugin.Log.LogInfo("Lighthouse: removed " + removed + " unavailable component references from " + name + " lookup caches.");
        }
    }

    private static bool Exists(object? value) => value != null && (value is not UnityEngine.Object unity || unity);
}
