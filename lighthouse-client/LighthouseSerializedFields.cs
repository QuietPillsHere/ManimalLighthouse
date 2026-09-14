using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Lighthouse.Client;

// applies a typetree-shaped json object (target 0.16.9 field names) to a live component through reflection
internal static class LighthouseSerializedFields
{
    internal delegate UnityEngine.Object? Resolver(JObject pointer, Type fieldType, string context);

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static void Apply(object target, JObject fields, Resolver resolve, string context)
    {
        foreach (var property in fields.Properties())
        {
            var field = FindField(target.GetType(), property.Name);

            if (field == null)
            {
                Plugin.Log.LogWarning("Lighthouse restore: " + context + " has no field " + property.Name);
                continue;
            }

            field.SetValue(target, Convert(field.FieldType, property.Value, resolve, context + "." + property.Name));
        }
    }

    internal static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, Declared);

            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    internal static void SetField(object target, string name, object? value)
    {
        var field = FindField(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static object? Convert(Type type, JToken value, Resolver resolve, string context)
    {
        if (value.Type == JTokenType.Null)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            return resolve((JObject)value, type, context);
        }

        if (type == typeof(bool))
        {
            return value.Type == JTokenType.Boolean ? value.Value<bool>() : value.Value<long>() != 0;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, value.Value<long>());
        }

        if (type == typeof(string)) { return value.Value<string>(); }
        if (type == typeof(int)) { return value.Value<int>(); }
        if (type == typeof(float)) { return value.Value<float>(); }
        if (type == typeof(double)) { return value.Value<double>(); }
        if (type == typeof(long)) { return value.Value<long>(); }
        if (type == typeof(byte)) { return value.Value<byte>(); }
        if (type == typeof(short)) { return value.Value<short>(); }
        if (type == typeof(uint)) { return value.Value<uint>(); }

        if (type == typeof(Vector3))
        {
            return new Vector3(Number(value, "x"), Number(value, "y"), Number(value, "z"));
        }

        if (type == typeof(Vector2))
        {
            return new Vector2(Number(value, "x"), Number(value, "y"));
        }

        if (type == typeof(Quaternion))
        {
            return new Quaternion(Number(value, "x"), Number(value, "y"), Number(value, "z"), Number(value, "w"));
        }

        if (type == typeof(Color))
        {
            return new Color(Number(value, "r"), Number(value, "g"), Number(value, "b"), Number(value, "a"));
        }

        if (type == typeof(AnimationCurve))
        {
            return Curve((JObject)value);
        }

        if (type.IsArray)
        {
            var items = (JArray)value;
            var element = type.GetElementType()!;
            var result = Array.CreateInstance(element, items.Count);

            for (var i = 0; i < items.Count; i++)
            {
                result.SetValue(Convert(element, items[i], resolve, context + "[" + i + "]"), i);
            }

            return result;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var items = (JArray)value;
            var element = type.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(type, true);

            for (var i = 0; i < items.Count; i++)
            {
                list.Add(Convert(element, items[i], resolve, context + "[" + i + "]"));
            }

            return list;
        }

        if (value is JObject nested)
        {
            var instance = Activator.CreateInstance(type, true);
            Apply(instance, nested, resolve, context);
            return instance;
        }

        throw new InvalidDataException("Unsupported serialized field type " + type.FullName + " at " + context);
    }

    private static float Number(JToken value, string name)
    {
        var token = value[name];
        return token == null ? 0f : token.Value<float>();
    }

    private static AnimationCurve Curve(JObject value)
    {
        var source = value["m_Curve"] as JArray;
        var keys = new Keyframe[source?.Count ?? 0];

        for (var i = 0; i < keys.Length; i++)
        {
            var key = source![i];
            keys[i] = new Keyframe(Number(key, "time"), Number(key, "value"), Number(key, "inSlope"), Number(key, "outSlope"), Number(key, "inWeight"), Number(key, "outWeight"))
            {
                weightedMode = (WeightedMode)(key["weightedMode"]?.Value<int>() ?? 0)
            };
        }

        return new AnimationCurve(keys);
    }
}
