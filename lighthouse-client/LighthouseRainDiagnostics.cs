using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

// Retire saved mesh copies, repair the live material, and opt into heavy capture.
internal static class LighthouseRainDiagnostics
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(RainFallDrops), nameof(RainFallDrops.Init)),
        postfix: new HarmonyMethod(typeof(LighthouseRainDiagnostics), nameof(AfterInit)));

    internal static void AfterInit(RainFallDrops __instance, MeshRenderer ____rainRenderer, ref Material ____closeCopy)
    {
        if (!LighthouseSceneLoader.IsNativeDonor(__instance.gameObject.scene.name)) return;
        var removed = LighthouseRainCopies.DisableSavedCopies(__instance.gameObject.scene, ____rainRenderer);
        Plugin.Log.LogInfo("Lighthouse rain: disabled " + removed + " saved rain renderers; preserved the live weather-controlled renderer.");
        try
        {
            if (!____rainRenderer) throw new InvalidOperationException("Native rain renderer is missing.");
            ____closeCopy = LighthouseRainRendering.CreateRuntimeMaterial(____closeCopy);
            ____rainRenderer.sharedMaterial = ____closeCopy;
            var liveMaterial = ____rainRenderer ? ____rainRenderer.sharedMaterial : null;
            if (liveMaterial == null || !liveMaterial || !liveMaterial.shader || liveMaterial.shader.name != "Manimal/Lighthouse/RainDrops")
                throw new InvalidOperationException("Live rain renderer did not receive the brightness override.");
            Plugin.Log.LogInfo("Lighthouse rain: live renderer bound to Manimal/Lighthouse/RainDrops; native motion, density and roof mask preserved.");
        }
        catch (Exception error) { Plugin.Log.LogError("Lighthouse rain material repair failed: " + error); }
        if (!Plugin.CaptureRain.Value) return;
        if (__instance.GetComponent<LighthouseRainCapture>()) return;
        var capture = __instance.gameObject.AddComponent<LighthouseRainCapture>();
        capture.Rain = __instance; capture.Renderer = ____rainRenderer; capture.Material = ____closeCopy;
        Plugin.Log.LogInfo("Lighthouse rain capture armed: up to 24 camera position/orientation samples during rain.");
    }
}

public sealed class LighthouseRainCapture : MonoBehaviour
{
    internal RainFallDrops Rain = null!;
    internal MeshRenderer Renderer = null!;
    internal Material Material = null!;
    private int _samples;
    private Vector3 _lastPosition, _lastForward;
    private float _next;
    private string? _directory;

    private static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
    private static float[] C(Color value) => new[] { value.r, value.g, value.b, value.a };

    private void LateUpdate()
    {
        if (Time.unscaledTime < _next || !Rain || Rain.Intensity <= Rain._intensityThreshold) return;
        _next = Time.unscaledTime + 1f;
        var camera = Camera.main;
        if (!camera || !Renderer || !Material) return;
        var yaw = Mathf.FloorToInt(Mathf.Repeat(camera.transform.eulerAngles.y + 22.5f, 360f) / 45f);
        var pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
        if (_samples != 0 && (camera.transform.position - _lastPosition).sqrMagnitude < .25f &&
            Vector3.Angle(camera.transform.forward, _lastForward) < 5f) return;
        try
        {
            if (_directory == null)
            {
                _directory = Path.Combine(Plugin.Root, "diagnostics", "rain-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(_directory);
            }
            var shader = Material.shader;
            var depth = Shader.GetGlobalTexture("_WeatherDepthMap");
            var obstacle = WeatherObstacle.Instance;
            var cameras = new List<object>();
            foreach (var active in Camera.allCameras)
                cameras.Add(new { active.name, path = active.actualRenderingPath.ToString(), position = V(active.transform.position), forward = V(active.transform.forward), active.cullingMask });
            var renderers = new List<object>();
            foreach (var root in Rain.gameObject.scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.name.StartsWith("RainFall(", StringComparison.Ordinal)) continue;
                    var material = renderer.sharedMaterial;
                    renderers.Add(new { renderer.name, controlled = renderer == Renderer, renderer.enabled, renderer.forceRenderingOff,
                        active = renderer.gameObject.activeInHierarchy, position = V(renderer.transform.position),
                        center = V(renderer.bounds.center), extents = V(renderer.bounds.extents),
                        material = material ? material.name : null, shader = material && material.shader ? material.shader.name : null,
                        density = material ? material.GetFloat("_RainDensity") : 0f,
                        ambient = material ? material.GetFloat("_MinAmbient") : 0f });
                }
            var report = new {
                sample = _samples, yaw, pitch, intensity = Rain.Intensity, Rain.MinAmbient, Rain.MinAmbientAddition, Rain.MinAmbientAdditionCoef,
                camera = camera.name, position = V(camera.transform.position), forward = V(camera.transform.forward),
                shader = shader ? shader.name : null, supported = shader && shader.isSupported,
                shaderId = shader ? shader.GetInstanceID() : 0,
                sourceShaderId = Rain._close && Rain._close.shader ? Rain._close.shader.GetInstanceID() : 0,
                Material.renderQueue, Material.shaderKeywords, texture = Material.mainTexture ? Material.mainTexture.name : null,
                alpha = Material.GetFloat("_AlphaMult"), ambient = Material.GetFloat("_MinAmbient"), density = Material.GetFloat("_RainDensity"),
                size = V(Material.GetVector("_Size")), falling = V(Material.GetVector("_FallingVector")),
                mapStart = V(Shader.GetGlobalVector("_MapStart")), mapScale = V(Shader.GetGlobalVector("_MapScale")),
                ambientLight = C(RenderSettings.ambientLight), ambientIntensity = RenderSettings.ambientIntensity,
                depthTexture = depth ? depth.name : null, depthWidth = depth ? depth.width : 0,
                obstacle = obstacle ? obstacle.gameObject.name : null, obstacleScene = obstacle ? obstacle.gameObject.scene.name : null,
                obstacleBounds = obstacle && obstacle.MeshCollider ? V(obstacle.MeshCollider.bounds.size) : null,
                underRain = RainController.IsCameraUnderRain, cameras, renderers
            };
            File.WriteAllText(Path.Combine(_directory, "sample-" + _samples.ToString("D2") + ".json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            if (_samples == 0 && depth)
                File.WriteAllText(Path.Combine(_directory, "depth-grid.json"), JsonConvert.SerializeObject(LighthouseRainDepthCapture.Read(depth)));
            _lastPosition = camera.transform.position; _lastForward = camera.transform.forward;
            _samples++;
            Plugin.Log.LogInfo("Lighthouse rain captured sample " + _samples + ": shader=" + (shader ? shader.name : "null") + ", ambient=" + Material.GetFloat("_MinAmbient") + ", depth=" + (depth ? depth.name : "null") + ". " + _directory);
            if (_samples == 24) enabled = false;
        }
        catch (Exception error)
        {
            // A diagnostic failure must not disrupt a raid or retry every frame.
            enabled = false;
            Plugin.Log.LogWarning("Lighthouse rain capture stopped: " + error);
        }
    }
}

