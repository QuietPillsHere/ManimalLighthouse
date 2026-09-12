using System;
using System.Collections.Generic;
using System.IO;
using Manimal.Lighthouse.Shared;
using Newtonsoft.Json;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseBindingVerification
{
    private static readonly int RainDensity = Shader.PropertyToID("_RainDensity");
    private static readonly int MinAmbient = Shader.PropertyToID("_MinAmbient");

    public static bool Requested()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg == "-manimal-lighthouse-verify-binding")
            {
                return true;
            }
        }

        return false;
    }

    public static void RunAndQuit()
    {
        var root = Path.Combine(Plugin.Root, "Diagnostics");
        var report = Path.Combine(root, "binding-result.json");
        AssetBundle? bundle = null;
        var exit = 2;

        try
        {
            var path = Path.Combine(root, "lighthouse_binding_probe.bundle");

            ManifestRules.VerifyFile(root, "lighthouse_binding_probe.bundle",
                File.ReadAllText(Path.Combine(root, "probe.sha256")).Trim());
            bundle = AssetBundle.LoadFromFile(path);

            if (!bundle)
            {
                throw new InvalidDataException("Runtime binding probe bundle did not load.");
            }

            var prefab = bundle.LoadAsset<GameObject>("Assets/Probe/LighthouseBindingProbe.prefab");

            if (!prefab)
            {
                throw new InvalidDataException("Runtime binding probe prefab did not load.");
            }

            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "EFT.Ballistics.BallisticCollider", "EFT.Interactive.TreeInteractive", "SpatialAudioRoom",
                "EFT.Game.Spawning.SpawnPointMarker", "CullingLightObject",
                "Koenigz.PerfectCulling.EFT.PerfectCullingAdaptiveGrid", "MetaXRAcousticGeometry",
                "Unity.AI.Navigation.NavMeshSurface"
            };
            var found = new List<string>();

            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component)
                {
                    throw new InvalidDataException("A target MonoBehaviour binding is missing.");
                }

                var type = component.GetType();
                var name = type.FullName!;

                if (!expected.Remove(name))
                {
                    throw new InvalidDataException("Unexpected or duplicate component binding: " + name);
                }

                var assembly = type.Assembly.GetName().Name!;

                if (assembly.StartsWith("ManimalLighthouse.Authoring", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Authoring assembly leaked into runtime binding.");
                }

                found.Add(name + "@" + assembly);
            }

            if (expected.Count != 0)
            {
                throw new InvalidDataException("Not all eight target components were bound.");
            }

            var rainVerified = false;

            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument == "-manimal-lighthouse-verify-rain")
                {
                    rainVerified = VerifyRain(root);
                }
            }

            File.WriteAllText(report,
                JsonConvert.SerializeObject(
                    new
                    {
                        status = "passed",
                        targetBuild = "0.16.9.40743",
                        componentTypes = found,
                        rainVerified,
                        bundleSha256 = ManifestRules.Hash(path),
                        scope =
                            "Real SPT client bundle load and prefab type binding; optional rain material isolation check; no raid entered"
                    }, Formatting.Indented));
            Plugin.Log.LogInfo("[BindingVerification] PASS: all eight components bound to actual SPT runtime types.");
            exit = 0;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[BindingVerification] FAIL: " + e);

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(report,
                    JsonConvert.SerializeObject(new
                    {
                        status = "failed",
                        error = e.ToString()
                    }, Formatting.Indented));
            }
            catch (Exception writeError)
            {
                Plugin.Log.LogError(writeError);
            }
        }
        finally
        {
            if (bundle)
            {
                bundle!.Unload(true);
            }

            Application.Quit(exit);
        }
    }

    private static bool VerifyRain(string directory)
    {
        var fixture = AssetBundle.LoadFromFile(Path.Combine(directory, "lighthouse_rain_probe.bundle"));

        if (!fixture)
        {
            throw new InvalidDataException("Rain fixture missing.");
        }

        try
        {
            var source = fixture.LoadAsset<Material>("rain/native");

            if (!source || !source.shader)
            {
                throw new InvalidDataException("Native rain fixture material missing.");
            }

            var originalShader = source.shader;

            source.SetFloat(RainDensity, .0225f);
            source.SetFloat(MinAmbient, .25f);

            var runtime = LighthouseRainRendering.CreateRuntimeMaterial(source);

            if (runtime == source || source.shader != originalShader ||
                runtime.shader.name != "Manimal/Lighthouse/RainDrops" ||
                runtime.mainTexture != source.mainTexture || runtime.renderQueue != source.renderQueue ||
                Mathf.Abs(runtime.GetFloat(RainDensity) - .0225f) > .00001f ||
                Mathf.Abs(runtime.GetFloat(MinAmbient) - .25f) > .00001f ||
                LighthouseRainRendering.CreateRuntimeMaterial(runtime) != runtime)
            {
                throw new InvalidDataException("Rain material isolation/value preservation failed: sameObject=" +
                                               (runtime == source) +
                                               ", sourceUnchanged=" + (source.shader == originalShader) + ", shader=" +
                                               runtime.shader.name +
                                               ", textureUnchanged=" + (runtime.mainTexture == source.mainTexture) +
                                               ", queue=" + runtime.renderQueue + "/" + source.renderQueue +
                                               ", density=" + runtime.GetFloat(RainDensity) + ", ambient=" +
                                               runtime.GetFloat(MinAmbient));
            }

            Plugin.Log.LogInfo(
                "[BindingVerification] PASS: embedded rain shader loaded; native asset unchanged, owned copy preserves density, ambient and texture.");

            return true;
        }
        finally
        {
            LighthouseRainRendering.Clear();
            fixture.Unload(true);
        }
    }
}