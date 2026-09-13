using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Manimal.Lighthouse.Shared;

public static class ManifestRules
{
    public const string FileName = "lighthouse-content.json";

    public static bool UsesNativeEnvironment(ContentManifest manifest) =>
        manifest.UseNativeEnvironment || manifest.Mode == "test";

    public static void Validate(ContentManifest manifest)
    {
        if (manifest == null || manifest.Schema is not (1 or 2))
        {
            throw new InvalidDataException("Unsupported Lighthouse manifest schema.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ContentId))
        {
            throw new InvalidDataException("Missing content identity.");
        }

        if (manifest.TargetClientBuild != "0.16.9.40743")
        {
            throw new InvalidDataException("Unsupported target client build.");
        }

        if (manifest.Scenes is not { Count: 29 })
        {
            throw new InvalidDataException("The replacement must contain all 29 Lighthouse scenes.");
        }

        if (manifest.Bundles == null || manifest.Bundles.Count == 0)
        {
            throw new InvalidDataException("No scene bundles declared.");
        }

        var original = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacement = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offline = 0;

        foreach (var scene in manifest.Scenes)
        {
            if (scene == null || string.IsNullOrWhiteSpace(scene.OriginalPath) || string.IsNullOrWhiteSpace(scene.ReplacementPath))
            {
                throw new InvalidDataException("Empty scene entry.");
            }

            if (!scene.OriginalPath.StartsWith("Assets/Content/Locations/Lighthouse/", StringComparison.Ordinal)
                || !scene.OriginalPath.EndsWith(".unity", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unexpected original scene path.");
            }

            if (!scene.ReplacementPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !scene.ReplacementPath.EndsWith("_ML.unity", StringComparison.Ordinal)
                || scene.ReplacementPath.Contains(".."))
            {
                throw new InvalidDataException("Replacement scenes require unique _ML names under Assets.");
            }

            if (!original.Add(scene.OriginalPath) || !replacement.Add(Path.GetFileNameWithoutExtension(scene.ReplacementPath)))
            {
                throw new InvalidDataException("Duplicate scene identity.");
            }

            if (!scene.OnlyOffline)
            {
                continue;
            }

            offline++;

            if (!scene.OriginalPath.EndsWith("/Lighthouse_AI.unity", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Only the AI scene may be offline-only.");
            }
        }

        if (offline != 1
            || !manifest.Scenes[0].OriginalPath.EndsWith("/Lighthouse_Scripts.unity", StringComparison.Ordinal)
            || !manifest.Scenes[1].OriginalPath.EndsWith("/Lighthouse_Terrain.unity", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid scene ordering or offline flags.");
        }

        var bundlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in manifest.Bundles)
        {
            if (bundle == null || !bundlePaths.Add(bundle.Path))
            {
                throw new InvalidDataException("Duplicate or empty bundle entry.");
            }

            ValidateRelativePath(bundle.Path);
            ValidateHash(bundle.Sha256);
        }

        if (manifest.Mode != "development" && manifest.Mode != "probe" && manifest.Mode != "rework"
            && manifest.Mode != "test")
        {
            throw new InvalidDataException("Unknown map mode.");
        }

        ValidateFiles(manifest.ServerFiles);
        ValidateFiles(manifest.Sidecars);
        ValidateFiles(manifest.NativeFiles);
        if ((manifest.Schema == 1 && manifest.NativeFiles.Count != 0)
            || (manifest.Schema == 2 && manifest.NativeFiles.Count == 0))
        {
            throw new InvalidDataException("Native asset dependencies require a nonempty schema 2 contract.");
        }
        foreach (var file in manifest.NativeFiles)
        {
            if (!Regex.IsMatch(file.Path, @"^(?:(?:sharedassets[0-9]+|resources)\.assets(?:\.resS)?|(?:sharedassets[0-9]+|resources)\.resource)$"))
            {
                throw new InvalidDataException("Unsupported native player-data file: " + file.Path);
            }
        }

        switch (manifest.Ready)
        {
            case true when manifest.Mode == "development":
                {
                    throw new InvalidDataException("Development content cannot be marked ready.");
                }

            case true when manifest.Mode == "test":
                {
                    throw new InvalidDataException("Test content must not claim release readiness.");
                }
        }
    }

    private static void ValidateFiles(List<PayloadFile> files)
    {
        if (files == null)
        {
            throw new InvalidDataException("Missing payload list.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file == null || !paths.Add(file.Path))
            {
                throw new InvalidDataException("Duplicate payload file.");
            }

            ValidateRelativePath(file.Path);
            ValidateHash(file.Sha256);
        }
    }

    public static void ValidateRelativePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":")
            || relative.Contains("\\"))
        {
            throw new InvalidDataException("Payload path must be relative with forward slashes.");
        }

        foreach (var part in relative.Split('/'))
        {
            if (part is "" or "." or "..")
            {
                throw new InvalidDataException("Invalid payload path segment.");
            }
        }
    }

    public static string Resolve(string root, string relative)
    {
        ValidateRelativePath(relative);

        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));

        return !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidDataException("Payload escapes its root.") : path;
    }

    private static void ValidateHash(string hash)
    {
        if (hash is not { Length: 64 })
        {
            throw new InvalidDataException("SHA-256 is required for every payload.");
        }

        foreach (var c in hash)
        {
            if (c is (< '0' or > '9') and (< 'a' or > 'f'))
            {
                throw new InvalidDataException("Invalid SHA-256.");
            }
        }
    }

    public static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();

        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    public static void VerifyFile(string root, string relative, string expected)
    {
        ValidateHash(expected);

        var path = Resolve(root, relative);

        if (!File.Exists(path) || Hash(path) != expected)
        {
            throw new InvalidDataException("Missing or mismatched Lighthouse payload: " + relative);
        }
    }

    public static void CheckCapability(ContentManifest manifest, string hash, ServerCapability capability)
    {
        if (capability is not { Schema: 1 }
            || !capability.Ready || !manifest.Ready && manifest.Mode != "test"
            || capability.ContentId != manifest.ContentId
            || capability.ManifestSha256 != hash || capability.Mode != manifest.Mode)
        {
            throw new InvalidDataException("Lighthouse client/server content mismatch or incomplete conversion.");
        }
    }
}
