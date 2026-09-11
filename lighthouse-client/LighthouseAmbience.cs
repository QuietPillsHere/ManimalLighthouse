using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using Comfort.Common;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseAmbience
{
    private const string AmbientRoot = "AmbientAudioSystem";
    private const string ReplacementSound = "Lighthouse_Sound_ML";
    private static readonly string[] Lifecycle = { "Awake", "OnEnable", "Start", "OnDisable", "OnDestroy" };

    internal static void Install(Harmony harmony)
    {
        var patched = new HashSet<MethodInfo>();
        // The converted ambient hierarchy is missing its manager and several
        // player classes. Stop its lifecycle before orphan helpers dereference
        // those players. Native ambient instances and other maps pass through.
        foreach (var type in typeof(AmbientAudioSystem).Assembly.GetTypes())
        {
            var ns = type.Namespace ?? "";
            if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.ContainsGenericParameters) continue;
            if (ns == "Audio.AutoPanner" || ns.StartsWith("Audio.AmbientSubsystem", StringComparison.Ordinal))
                GuardLifecycle(harmony, type, patched);
        }
        // Loading the donor must never register a second spatial/radio system
        // or acoustic map, even if their Awake precedes LocationScene.Awake.
        foreach (var name in new[] {
            "Audio.SpatialSystem.SpatialAudioSystem", "Audio.SpatialSystem.SpatialAudioPortal",
            "Audio.SpatialSystem.AudioTriggerArea", "Audio.SpatialSystem.MultiWindowPortal",
            "SpatialAudioRoom", "Audio.SpatialSystem.SpatialAudioCrossSceneGroup", "GuidComponent",
            "CommonAssets.Scripts.Audio.RadioSystem.RadioBroadcastController",
            "Audio.RadioSystem.ClientBroadcastPlayer", "MetaXRAcousticMap"
        })
        {
            var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
            GuardLifecycle(harmony, type, patched);
        }
        harmony.Patch(AccessTools.Method(typeof(AmbientAudioSystem), nameof(AmbientAudioSystem.Init)),
            postfix: new HarmonyMethod(typeof(LighthouseAmbience), nameof(Initialized)));
    }

    private static void GuardLifecycle(Harmony harmony, Type type, HashSet<MethodInfo> patched)
    {
        foreach (var name in Lifecycle)
        {
            MethodInfo? method = null;
            for (var declaring = type; declaring != null && method == null; declaring = declaring.BaseType)
                method = declaring.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
            // Mono shares generic method bodies. Patching a closed singleton
            // Awake can cast unrelated singleton instances to the wrong type.
            // SpatialAudioSystem only registers there; undo that registration
            // at sceneLoaded instead, before the raid initializes audio.
            if (method == null || method.IsAbstract || method.IsStatic || method.DeclaringType!.IsGenericType || !patched.Add(method)) continue;
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(LighthouseAmbience), nameof(BeforeLifecycle)));
        }
    }

    private static bool BeforeLifecycle(MonoBehaviour __instance)
    {
        var scene = __instance.gameObject.scene.name;
        var donor = LighthouseSceneLoader.IsNativeAmbienceDonor(scene);
        var replacement = scene == ReplacementSound && LighthouseSceneLoader.Owns(scene);
        if (!donor && !replacement) return true;
        var root = __instance.transform.root.gameObject;
        if (donor ? root.name == AmbientRoot : root.name != AmbientRoot) return true;
        if (root.activeSelf) root.SetActive(false);
        // Also skip teardown for components whose Awake was suppressed: the
        // native spatial teardown changes static state shared with the real map.
        return false;
    }

    internal static bool IsAmbientObject(object? value)
    {
        if (value is Component component && component) return component.transform.root.name == AmbientRoot;
        return value is GameObject go && go && go.transform.root.name == AmbientRoot;
    }

    internal static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var donor = LighthouseSceneLoader.IsNativeAmbienceDonor(scene.name);
        var replacement = scene.name == ReplacementSound && LighthouseSceneLoader.Owns(scene.name);
        if (!donor && !replacement) return;
        var systems = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (donor && root.name == AmbientRoot)
            {
                systems += root.GetComponentsInChildren<AmbientAudioSystem>(true).Length;
                continue;
            }
            // Keep the donor's filtered LocationScene registration until unload.
            if (donor && root.GetComponent<LocationScene>()) continue;
            if (donor || root.name == AmbientRoot) root.SetActive(false);
        }
        if (donor)
        {
            RestoreSpatialRegistration();
            if (systems != 1) throw new InvalidDataException("Expected one native Lighthouse ambient system, found " + systems);
            Plugin.Log.LogInfo("Lighthouse ambience: native sound hierarchy loaded with original clips; donor rooms, radio and acoustic map disabled. Replacement spatial audio retained.");
        }
    }

    private static void Initialized(AmbientAudioSystem __instance)
    {
        if (!LighthouseSceneLoader.IsNativeAmbienceDonor(__instance.gameObject.scene.name)) return;
        if (!__instance.Initialized)
        {
            Plugin.Log.LogError("Lighthouse ambience: native initialization failed; see Player.log for its exception.");
            return;
        }
        var groups = __instance.GetComponentsInChildren<AmbientSoundPlayerGroup>(true).Length;
        var players = __instance.GetComponentsInChildren<BaseAmbientSoundPlayer>(true).Length;
        Plugin.Log.LogInfo("Lighthouse ambience: initialized " + groups + " native groups and " + players + " players for " + __instance.CurrentSeasonStatus + ".");
    }

    internal static void ValidateLoaded()
    {
        if (!LighthouseSceneLoader.IsNativeAmbienceDonor("Lighthouse_Sound")) return;
        var count = 0;
        foreach (var system in Resources.FindObjectsOfTypeAll<AmbientAudioSystem>())
        {
            if (!system || !LighthouseSceneLoader.IsNativeAmbienceDonor(system.gameObject.scene.name)) continue;
            count++;
            if (!system.gameObject.activeInHierarchy || !system._ambientData || !system.EffectsData)
                throw new InvalidDataException("Native Lighthouse ambient system or audio data is unavailable.");
            if (system.GetComponentsInChildren<AmbientSoundPlayerGroup>(true).Length != 12 ||
                system.GetComponentsInChildren<BaseAmbientSoundPlayer>(true).Length != 78)
                throw new InvalidDataException("Native Lighthouse ambient players do not match the audited donor.");
        }
        if (count != 1) throw new InvalidDataException("Native Lighthouse ambient donor did not load.");
        RestoreSpatialRegistration();
    }

    private static void RestoreSpatialRegistration()
    {
        var current = Singleton<SpatialAudioSystem>.Instance;
        if (!current || !LighthouseSceneLoader.IsNativeAmbienceDonor(current.gameObject.scene.name)) return;
        Singleton<SpatialAudioSystem>.TryRelease(current);
        foreach (var system in Resources.FindObjectsOfTypeAll<SpatialAudioSystem>())
        {
            if (!system || !LighthouseSceneLoader.Owns(system.gameObject.scene.name)) continue;
            Singleton<SpatialAudioSystem>.Create(system);
            break;
        }
    }
}
