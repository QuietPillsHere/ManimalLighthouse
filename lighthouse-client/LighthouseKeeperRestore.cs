using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cutscene;
using EFT.Interactive;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Lighthouse.Client;

// the converter could not author the retail gate switches, gates, radio switches and keeper
// cutscene triggers, so the built scenes carry their objects without those components and the
// referrers hold null slots. recreate them from the recovered field table before anything
// dereferences them: BufferZoneContainer.Awake, LighthouseKeeperZone.Awake and LocationScene.Awake
// all run in undefined order during scene activation, so each one calls in here first.
internal static class LighthouseKeeperRestore
{
    private sealed class SceneState
    {
        internal readonly List<WorldInteractiveObject> Interactives = [];
        internal bool Registered;
    }

    private static readonly Dictionary<int, SceneState> States = new();
    private static readonly FieldInfo Containers = typeof(LocationScene).GetField("_typeToContainer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(LocationScene), "_typeToContainer");
    private static JObject? _table;

    internal static void BeforeAwake(Component component)
    {
        var scene = component.gameObject.scene;

        if (LighthouseSceneLoader.Owns(scene.name))
        {
            Ensure(scene);
        }
    }

    internal static void OnLocationSceneAwake(LocationScene instance)
    {
        var scene = instance.gameObject.scene;

        if (!LighthouseSceneLoader.Owns(scene.name))
        {
            return;
        }

        var state = Ensure(scene);

        if (state != null)
        {
            Register(instance, state);
        }
    }

    internal static void Clear()
    {
        States.Clear();
    }

    private static SceneState? Ensure(Scene scene)
    {
        if (States.TryGetValue(scene.handle, out var existing))
        {
            return existing;
        }

        var state = new SceneState();
        States[scene.handle] = state;
        var retailName = scene.name.EndsWith("_ML", StringComparison.Ordinal) ? scene.name.Substring(0, scene.name.Length - 3) : scene.name;
        var table = Table();
        var restored = 0;

        foreach (var entry in (JArray)table["components"]!)
        {
            if ((string?)entry["scene"] != retailName)
            {
                continue;
            }

            try
            {
                var component = Restore(scene, retailName, (JObject)entry);

                if (component is WorldInteractiveObject interactive)
                {
                    state.Interactives.Add(interactive);
                }

                restored++;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError("Lighthouse restore failed for " + entry["type"] + " at " + entry["path"] + ": " + exception);
            }
        }

        foreach (var link in (JArray)table["links"]!)
        {
            if ((string?)link["scene"] != retailName)
            {
                continue;
            }

            try
            {
                var owner = FindComponent(scene, (string)link["path"]!, (string)link["type"]!)
                    ?? throw new InvalidDataException("Link owner missing: " + link["type"] + " at " + link["path"]);
                var target = Resolve(scene, (JObject)link["target"]!, typeof(UnityEngine.Object), (string)link["field"]!)
                    ?? throw new InvalidDataException("Link target missing for " + link["field"]);
                LighthouseSerializedFields.SetField(owner, (string)link["field"]!, target);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError("Lighthouse restore link failed for " + link["type"] + "." + link["field"] + ": " + exception);
            }
        }

        if (table["invoke"] is JArray invokes)
        {
            foreach (var call in invokes)
            {
                if ((string?)call["scene"] != retailName)
                {
                    continue;
                }

                try
                {
                    var target = FindComponent(scene, (string)call["path"]!, (string)call["type"]!)
                        ?? throw new InvalidDataException("Invoke target missing: " + call["type"] + " at " + call["path"]);
                    var method = target.GetType().GetMethod((string)call["method"]!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                        ?? throw new MissingMethodException(target.GetType().Name, (string)call["method"]!);
                    method.Invoke(target, null);
                }
                catch (Exception exception)
                {
                    Plugin.Log.LogError("Lighthouse restore invoke failed for " + call["type"] + "." + call["method"] + ": " + exception);
                }
            }
        }

        // a LocationScene that already ran Awake holds its arrays in a dictionary copy too
        foreach (var loaded in LocationScene.LoadedScenes)
        {
            if (loaded && loaded.gameObject.scene == scene)
            {
                Register(loaded, state);
            }
        }

        if (restored != 0)
        {
            Plugin.Log.LogInfo("Lighthouse: restored " + restored + " components in " + scene.name + ".");
        }

        return state;
    }

    private static Component Restore(Scene scene, string retailName, JObject entry)
    {
        var typeName = (string)entry["type"]!;
        var path = (string)entry["path"]!;
        var type = FindType(typeName) ?? throw new InvalidDataException("Unknown component type " + typeName);
        var owner = FindGameObject(scene, path) ?? throw new InvalidDataException("Missing object " + path);

        if (owner.GetComponent(type))
        {
            throw new InvalidOperationException(typeName + " already exists at " + path);
        }

        // Awake must see the serialized state, so add while inactive and activate afterwards
        var active = owner.activeSelf;
        var inactiveAdd = entry["inactiveAdd"]?.Value<bool>() ?? true;

        if (inactiveAdd)
        {
            owner.SetActive(false);
        }

        try
        {
            var component = owner.AddComponent(type);
            var context = typeName + "@" + path;
            LighthouseSerializedFields.Apply(component, (JObject)entry["fields"]!, (pointer, fieldType, field) => Resolve(scene, pointer, fieldType, field), context);

            if (component is Behaviour behaviour)
            {
                behaviour.enabled = entry["enabled"]?.Value<bool>() ?? true;
            }

            if (component is BaseCutsceneTrigger trigger)
            {
                ApplyCutsceneStart(trigger, retailName);
            }

            return component;
        }
        finally
        {
            if (inactiveAdd)
            {
                owner.SetActive(active);
            }
        }
    }

    private static Type? FindType(string name)
    {
        var type = typeof(WorldInteractiveObject).Assembly.GetType(name, false);

        if (type != null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(name, false);

            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    // the start-info table is keyed by the retail scene name; the bundled scene carries an _ML suffix
    private static void ApplyCutsceneStart(BaseCutsceneTrigger trigger, string retailName)
    {
        var info = CutsceneTriggerStartInfoSO.Instance.GetPlayerStartInfo(retailName, trigger.CutsceneID);

        if (info == null)
        {
            Plugin.Log.LogWarning("Lighthouse: no cutscene start info for " + retailName + " id " + trigger.CutsceneID);
            return;
        }

        LighthouseSerializedFields.SetField(trigger, "_startPosition", info.startPosition);
        LighthouseSerializedFields.SetField(trigger, "_startViewDirection", info.startViewDirection);
        LighthouseSerializedFields.SetField(trigger, "_startPlayerPosLevel", info.startPlayerPosLevel);
        LighthouseSerializedFields.SetField(trigger, "_needToProneAtStart", info.needToProneAtStart);
        LighthouseSerializedFields.SetField(trigger, "_cutsceneEndPlayerPosition", info.cutsceneEndPlayerPos);
    }

    private static void Register(LocationScene instance, SceneState state)
    {
        if (state.Registered || state.Interactives.Count == 0)
        {
            return;
        }

        var current = instance.WorldInteractiveObjects ?? [];
        var merged = new WorldInteractiveObject[current.Length + state.Interactives.Count];
        Array.Copy(current, merged, current.Length);

        for (var i = 0; i < state.Interactives.Count; i++)
        {
            merged[current.Length + i] = state.Interactives[i];
        }

        instance.WorldInteractiveObjects = merged;

        if (Containers.GetValue(instance) is Dictionary<Type, Array> containers && containers.ContainsKey(typeof(WorldInteractiveObject)))
        {
            containers[typeof(WorldInteractiveObject)] = merged;
        }

        state.Registered = true;
    }

    private static UnityEngine.Object? Resolve(Scene scene, JObject pointer, Type fieldType, string context)
    {
        if (pointer["$asset"] is { } asset)
        {
            return LighthouseKeeperAssets.Get((string)asset!);
        }

        if (pointer["$gameObject"] is { } gameObjectPath)
        {
            return FindGameObject(scene, (string)gameObjectPath!);
        }

        if (pointer["$transform"] is { } transformPath)
        {
            return FindGameObject(scene, (string)transformPath!)?.transform;
        }

        if (pointer["$component"] is JObject component)
        {
            return FindComponent(scene, (string)component["path"]!, (string)component["type"]!);
        }

        throw new InvalidDataException("Unknown pointer at " + context + ": " + pointer);
    }

    private static Component? FindComponent(Scene scene, string path, string typeName)
    {
        var owner = FindGameObject(scene, path);

        if (!owner)
        {
            return null;
        }

        var components = owner!.GetComponents<Component>();

        foreach (var component in components)
        {
            if (component && component.GetType().FullName == typeName)
            {
                return component;
            }
        }

        foreach (var component in components)
        {
            if (component && IsNamed(component.GetType(), typeName))
            {
                return component;
            }
        }

        return null;
    }

    private static bool IsNamed(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.FullName == name)
            {
                return true;
            }
        }

        return false;
    }

    // paths are the inventory form: name~n per level, n counting same-named siblings in order
    private static GameObject? FindGameObject(Scene scene, string path)
    {
        var segments = path.Split('/');
        Transform? current = null;

        for (var depth = 0; depth < segments.Length; depth++)
        {
            var separator = segments[depth].LastIndexOf('~');
            var name = segments[depth].Substring(0, separator).Replace("%2F", "/");
            var ordinal = int.Parse(segments[depth].Substring(separator + 1));
            Transform? found = null;
            var seen = 0;

            if (current == null)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != name) { continue; }
                    if (seen++ == ordinal) { found = root.transform; break; }
                }
            }
            else
            {
                for (var i = 0; i < current.childCount; i++)
                {
                    var child = current.GetChild(i);
                    if (child.name != name) { continue; }
                    if (seen++ == ordinal) { found = child; break; }
                }
            }

            if (found == null)
            {
                return null;
            }

            current = found;
        }

        return current?.gameObject;
    }

    private static JObject Table()
    {
        if (_table != null)
        {
            return _table;
        }

        using var stream = typeof(LighthouseKeeperRestore).Assembly.GetManifestResourceStream("Manimal.Lighthouse.Keeper.json")
            ?? throw new InvalidOperationException("Embedded keeper restore table is missing.");
        using var reader = new StreamReader(stream);
        _table = JObject.Parse(reader.ReadToEnd());
        return _table;
    }
}
