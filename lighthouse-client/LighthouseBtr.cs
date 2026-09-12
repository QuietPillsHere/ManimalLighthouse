using System;
using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using EFT;
using EFT.Vehicle;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseBtr
{
    internal static bool BindMapPaths(BtrController __instance, string locationID)
    {
        if (locationID != "Lighthouse" || !LighthouseSceneLoader.HasReplacement)
        {
            return true;
        }

        if (__instance.MapPathsConfiguration)
        {
            return false;
        }

        MapPathConfig? found = null;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<MapPathConfig>())
        {
            if (!candidate || !LighthouseSceneLoader.Owns(candidate.gameObject.scene.name))
            {
                continue;
            }

            if (found)
            {
                throw new InvalidDataException("Multiple replacement Lighthouse BTR path configurations.");
            }

            found = candidate;
        }

        if (!found)
        {
            throw new InvalidDataException("Replacement Lighthouse BTR path configuration is missing.");
        }

        var global = Singleton<GlobalConfiguration>.Instance;

        if (global == null || !global.BTRSettings.MapsConfigs.TryGetValue("Lighthouse", out var routes))
        {
            throw new InvalidDataException("Server did not provide Lighthouse BTR routes. Restart the updated server.");
        }

        ValidatePaths(found!, routes);
        AlignEntryPoints(found!, routes);
        found!.gameObject.SetActive(true);

        __instance.MapPathsConfiguration = found;
        Plugin.Log.LogInfo("Lighthouse BTR: bound scene paths (10 destinations, 72 splines, 4 routes). Native spawn timer and services enabled.");

        return false;
    }

    private static void ValidatePaths(MapPathConfig map, BTRMapPath routes)
    {
        var destinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var destination in map.PathDestinations)
        {
            if (!destination || !destinations.Add(destination.id))
            {
                throw new InvalidDataException("Missing or duplicate BTR destination.");
            }
        }

        var splines = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in map.PathSplines)
        {
            if (!part || !part.spline || !splines.Add(part.id))
            {
                throw new InvalidDataException("Missing or duplicate BTR spline.");
            }
        }

        if (destinations.Count != 10 || splines.Count != 72 || routes.pathsConfigurations is not { Length: 4 })
        {
            throw new InvalidDataException("Lighthouse BTR path counts do not match audited content.");
        }

        foreach (var route in routes.pathsConfigurations)
        {
            if (route?.pathPoints == null || route.pathPoints.Count < 2)
            {
                throw new InvalidDataException("Empty Lighthouse BTR route.");
            }

            RequireDestination(destinations, route.enterPoint);
            RequireDestination(destinations, route.exitPoint);

            foreach (var stop in route.pathPoints)
            {
                RequireDestination(destinations, stop);
            }

            RequireEdge(splines, route.enterPoint, route.pathPoints[0]);

            for (var i = 1; i < route.pathPoints.Count; i++)
            {
                RequireEdge(splines, route.pathPoints[i - 1], route.pathPoints[i]);
            }

            var last = route.pathPoints[^1];

            if (route.circle)
            {
                RequireEdge(splines, last, route.pathPoints[0]);
                RequireEdge(splines, route.pathPoints[0], route.exitPoint);
            }
            else
            {
                RequireEdge(splines, last, route.exitPoint);
            }
        }
    }

    private static void RequireDestination(HashSet<string> destinations, string id)
    {
        if (!destinations.Contains(id))
        {
            throw new InvalidDataException("Missing Lighthouse BTR stop: " + id);
        }
    }

    private static void AlignEntryPoints(MapPathConfig map, BTRMapPath routes)
    {
        var headings = new Dictionary<string, Vector3>(StringComparer.Ordinal);

        foreach (var route in routes.pathsConfigurations)
        {
            var entry = map.GetPartPathByID(route.enterPoint);
            var spline = map.GetPathSplineByPoints(route.enterPoint, route.pathPoints[0]).spline;
            var points = spline.Points;

            if (points == null || points.Length < 4)
            {
                throw new InvalidDataException("Invalid BTR entry spline: " + route.enterPoint);
            }

            var first = spline.transform.TransformPoint(points[0]);
            var last = spline.transform.TransformPoint(points[^1]);
            var atStart = (entry.transform.position - first).sqrMagnitude < (entry.transform.position - last).sqrMagnitude;
            var tangent = atStart
                ? spline.transform.TransformPoint(points[1]) - first
                : spline.transform.TransformPoint(points[^2]) - last;

            tangent.y = 0;

            if (tangent.sqrMagnitude < 0.0001f)
            {
                throw new InvalidDataException("Degenerate BTR entry tangent: " + route.enterPoint);
            }

            tangent.Normalize();

            if (headings.TryGetValue(route.enterPoint, out var previous))
            {
                if (Vector3.Angle(previous, tangent) > 1f)
                {
                    throw new InvalidDataException("Conflicting BTR entry headings: " + route.enterPoint);
                }

                continue;
            }

            headings.Add(route.enterPoint, tangent);

            var angle = Vector3.Angle(entry.transform.forward, tangent);

            entry.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            Plugin.Log.LogInfo("Lighthouse BTR: aligned entry " + route.enterPoint + " with first spline; corrected " + angle.ToString("F1") + " degrees.");
        }
    }
    
    private static void RequireEdge(HashSet<string> splines, string from, string to)
    {
        if (!splines.Contains("s_" + from + "_" + to) && !splines.Contains("s_" + to + "_" + from))
        {
            throw new InvalidDataException("Missing Lighthouse BTR connection: " + from + " -> " + to);
        }
    }
    
    private static bool Owns(BtrController controller) => controller.MapPathsConfiguration &&
        LighthouseSceneLoader.Owns(controller.MapPathsConfiguration.gameObject.scene.name);
    
    internal static void VehicleLoaded(BtrController __instance)
    {
        if (!Owns(__instance))
        {
            return;
        }

        if (__instance.BtrVehicle)
        {
            Plugin.Log.LogInfo("Lighthouse BTR: native vehicle prefab created.");
        }
        else
        {
            Plugin.Log.LogError("Lighthouse BTR: native vehicle prefab could not be loaded.");
        }
    }
    
    internal static void ServerInitialized(BtrController __instance)
    {
        if (Owns(__instance) && __instance.BtrVehicle)
        {
            Plugin.Log.LogInfo("Lighthouse BTR: server initialized route " + __instance.BtrVehicle.CurrentPathConfig.id + "; movement starting.");
        }
    }
    internal static void ViewInitialized(BtrController __instance)
    {
        if (Owns(__instance))
        {
            Plugin.Log.LogInfo("Lighthouse BTR: client view and service events initialized.");
        }
    }
}
