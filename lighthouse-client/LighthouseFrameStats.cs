using System;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace Manimal.Lighthouse.Client;

// Numeric counters only during play: no scene scans, file writes, GPU readback,
// or forced collections. Log one summary per minute and at raid teardown.
public sealed class LighthouseFrameStats : MonoBehaviour
{
    private int _frames, _over50, _over100, _gcFrames, _slowGcFrames;
    private int _lastGc, _lastFullGc, _collections, _fullCollections;
    private float _seconds, _worst;
    private bool _wasFocused;

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)),
        postfix: new HarmonyMethod(typeof(LighthouseFrameStats), nameof(StartTracking)));

    private static void StartTracking(GameWorld __instance)
    {
        if (!LighthouseSceneLoader.HasReplacement || __instance.GetComponent<LighthouseFrameStats>()) return;
        __instance.gameObject.AddComponent<LighthouseFrameStats>();
    }

    private void Awake()
    {
        _lastGc = GC.CollectionCount(0);
        _lastFullGc = GC.CollectionCount(GC.MaxGeneration);
        _wasFocused = Application.isFocused;
        Plugin.Log.LogInfo("Lighthouse frame counters started; incremental GC=" + GarbageCollector.isIncremental +
            ", GC mode=" + GarbageCollector.GCMode + ". Heavy rain capture=" + Plugin.CaptureRain.Value + ".");
    }

    private void Update()
    {
        var gc = GC.CollectionCount(0);
        var full = GC.CollectionCount(GC.MaxGeneration);
        var collected = gc != _lastGc || full != _lastFullGc;
        var focused = Application.isFocused;
        if (focused && _wasFocused)
        {
            var delta = Time.unscaledDeltaTime;
            _frames++;
            _seconds += delta;
            if (delta > _worst) _worst = delta;
            if (delta > .05f) { _over50++; if (collected) _slowGcFrames++; }
            if (delta > .1f) _over100++;
            if (collected) _gcFrames++;
            _collections += gc - _lastGc;
            _fullCollections += full - _lastFullGc;
        }
        _lastGc = gc;
        _lastFullGc = full;
        _wasFocused = focused;
        if (_seconds >= 60f) Report();
    }

    private void OnDestroy() { if (_frames != 0) Report(); }

    private void Report()
    {
        Plugin.Log.LogInfo("Lighthouse frame stats: seconds=" + _seconds.ToString("F1") + ", frames=" + _frames +
            ", meanMs=" + (_seconds * 1000f / Math.Max(_frames, 1)).ToString("F1") + ", worstMs=" + (_worst * 1000f).ToString("F1") +
            ", over50ms=" + _over50 + ", over100ms=" + _over100 + ", gcFrames=" + _gcFrames +
            ", slowFramesWithGC=" + _slowGcFrames + ", gen0=" + _collections + ", fullGC=" + _fullCollections +
            ", managedMiB=" + GC.GetTotalMemory(false) / (1024 * 1024) + ". GC overlap is correlation, not a measured pause duration.");
        _frames = _over50 = _over100 = _gcFrames = _slowGcFrames = _collections = _fullCollections = 0;
        _seconds = _worst = 0;
    }
}
