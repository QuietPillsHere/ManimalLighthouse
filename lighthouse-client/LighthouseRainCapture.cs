using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace Manimal.Lighthouse.Client;

[SuppressMessage("ReSharper", "Unity.PerformanceCriticalCodeCameraMain")]
[SuppressMessage("ReSharper", "Unity.PerformanceCriticalCodeInvocation")]
public sealed class LighthouseRainCapture : MonoBehaviour
{
    private static readonly int WeatherDepthMap = Shader.PropertyToID("_WeatherDepthMap");
    private static readonly int RainDensity = Shader.PropertyToID("_RainDensity");
    private static readonly int MinAmbient = Shader.PropertyToID("_MinAmbient");
    private static readonly int AlphaMult = Shader.PropertyToID("_AlphaMult");
    private static readonly int Size = Shader.PropertyToID("_Size");
    private static readonly int FallingVector = Shader.PropertyToID("_FallingVector");
    private static readonly int MapStart = Shader.PropertyToID("_MapStart");
    private static readonly int MapScale = Shader.PropertyToID("_MapScale");
    
    internal RainFallDrops Rain = null!;
    internal MeshRenderer Renderer = null!;
    internal Material Material = null!;
    
    private int _samples;
    private Vector3 _lastPosition, _lastForward;
    private float _next;
    private string? _directory;
    private bool _depthCaptured;
    private bool _depthFailureLogged;

    private static float[] V(Vector3 value) => [value.x, value.y, value.z];
    private static float[] C(Color value) => [value.r, value.g, value.b, value.a];

    private static float? ReadFloat(Material material, int property) =>
        material && material.HasFloat(property) ? material.GetFloat(property) : null;

    private static float[]? ReadVector(Material material, int property) =>
        material && material.HasVector(property) ? V(material.GetVector(property)) : null;

    private void LateUpdate()
    {
        if (Time.unscaledTime < _next || !Rain || Rain.Intensity <= Rain._intensityThreshold)
        {
            return;
        }

        _next = Time.unscaledTime + 1f;

        var camera = Camera.main;

        if (!camera || !Renderer || !Material)
        {
            return;
        }

        var yaw = Mathf.FloorToInt(Mathf.Repeat(camera!.transform.eulerAngles.y + 22.5f, 360f) / 45f);
        var pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);

        try
        {
            if (_directory == null)
            {
                _directory = Path.Combine(Plugin.Root, "diagnostics", "rain-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_directory);
            }

            var shader = Material.shader;
            var depth = Shader.GetGlobalTexture(WeatherDepthMap);

            // Retry independently of camera movement until depth is saved or capture ends.
            if (!_depthCaptured && depth)
            {
                try
                {
                    File.WriteAllText(Path.Combine(_directory, "depth-grid.json"), JsonConvert.SerializeObject(LighthouseRainDepthCapture.Read(depth)));
                    _depthCaptured = true;
                }
                catch (Exception error)
                {
                    if (!_depthFailureLogged)
                    {
                        _depthFailureLogged = true;
                        Plugin.Log.LogWarning("Lighthouse rain depth capture failed; will retry while capture is active: " + error);
                    }
                }
            }

            if (_samples != 0 && (camera.transform.position - _lastPosition).sqrMagnitude < .25f &&
                Vector3.Angle(camera.transform.forward, _lastForward) < 5f)
            {
                return;
            }

            var obstacle = WeatherObstacle.Instance;
            var cameras = new List<object>();

            foreach (var active in Camera.allCameras)
            {
                cameras.Add(new
                {
                    active.name,
                    path = active.actualRenderingPath.ToString(),
                    position = V(active.transform.position),
                    forward = V(active.transform.forward),
                    active.cullingMask
                });
            }

            var renderers = new List<object>();

            foreach (var root in Rain.gameObject.scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.name.StartsWith("RainFall(", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var material = renderer.sharedMaterial;

                    renderers.Add(new
                    {
                        renderer.name,
                        controlled = renderer == Renderer,
                        renderer.enabled,
                        renderer.forceRenderingOff,
                        active = renderer.gameObject.activeInHierarchy,
                        position = V(renderer.transform.position),
                        center = V(renderer.bounds.center),
                        extents = V(renderer.bounds.extents),
                        material = material ? material.name : null,
                        shader = material && material.shader ? material.shader.name : null,
                        density = ReadFloat(material, RainDensity),
                        ambient = ReadFloat(material, MinAmbient)
                    });
                }
            }

            var report = new
            {
                sample = _samples,
                yaw,
                pitch,
                intensity = Rain.Intensity,
                Rain.MinAmbient,
                Rain.MinAmbientAddition,
                Rain.MinAmbientAdditionCoef,
                camera = camera.name,
                position = V(camera.transform.position),
                forward = V(camera.transform.forward),
                shader = shader ? shader.name : null,
                supported = shader && shader.isSupported,
                shaderId = shader ? shader.GetInstanceID() : 0,
                sourceShaderId = Rain._close && Rain._close.shader ? Rain._close.shader.GetInstanceID() : 0,
                Material.renderQueue,
                Material.shaderKeywords,
                texture = Material.mainTexture ? Material.mainTexture.name : null,
                alpha = ReadFloat(Material, AlphaMult),
                ambient = ReadFloat(Material, MinAmbient),
                density = ReadFloat(Material, RainDensity),
                size = ReadVector(Material, Size),
                falling = ReadVector(Material, FallingVector),
                mapStart = V(Shader.GetGlobalVector(MapStart)),
                mapScale = V(Shader.GetGlobalVector(MapScale)),
                ambientLight = C(RenderSettings.ambientLight),
                depthTexture = depth ? depth.name : null,
                depthWidth = depth ? depth.width : 0,
                obstacle = obstacle ? obstacle.gameObject.name : null,
                obstacleScene = obstacle ? obstacle.gameObject.scene.name : null,
                obstacleBounds = obstacle && obstacle.MeshCollider ? V(obstacle.MeshCollider.bounds.size) : null,
                underRain = RainController.IsCameraUnderRain,
                cameras,
                renderers
            };

            File.WriteAllText(Path.Combine(_directory, "sample-" + _samples.ToString("D2") + ".json"), JsonConvert.SerializeObject(report, Formatting.Indented));

            _lastPosition = camera.transform.position;
            _lastForward = camera.transform.forward;
            _samples++;
            
            Plugin.Log.LogInfo("Lighthouse rain captured sample " + _samples + ": shader=" + (shader ? shader.name : "null") + ", ambient=" + ReadFloat(Material, MinAmbient) + ", depth=" + (depth ? depth.name : "null") + ". " + _directory);

            if (_samples == 24)
            {
                enabled = false;
            }
        }
        catch (Exception error)
        {
            enabled = false;
            Plugin.Log.LogWarning("Lighthouse rain capture stopped: " + error);
        }
    }
}
