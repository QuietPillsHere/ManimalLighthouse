using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseNativeAssets
{
    private sealed class VerifiedFile
    {
        internal long Length;
        internal long Modified;
        internal string Sha256 = "";
    }

    private static readonly Dictionary<string, VerifiedFile> Verified = new(StringComparer.OrdinalIgnoreCase);

    // Called by the serialized scene-loading operation on its verification worker.
    // Unity owns and resolves the external objects; no native assets are destroyed here.
    internal static void Verify(string dataPath, ContentManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var dependency in manifest.NativeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ManifestRules.Resolve(dataPath, dependency.Path);
            var before = new FileInfo(path);
            if (!before.Exists)
            {
                throw new InvalidDataException("Lighthouse requires the supported SPT client data. Missing: " + dependency.Path);
            }
            var length = before.Length;
            var modified = before.LastWriteTimeUtc.Ticks;
            if (Verified.TryGetValue(path, out var cached) && cached.Length == length
                && cached.Modified == modified && cached.Sha256 == dependency.Sha256)
            {
                continue;
            }
            if (ManifestRules.Hash(path) != dependency.Sha256)
            {
                throw new InvalidDataException("Lighthouse native assets do not match this content build: " + dependency.Path
                    + ". Install the supported SPT client/content combination.");
            }
            before.Refresh();
            if (!before.Exists || before.Length != length || before.LastWriteTimeUtc.Ticks != modified)
            {
                throw new IOException("Native player data changed during verification: " + dependency.Path);
            }
            Verified[path] = new VerifiedFile { Length = length, Modified = modified, Sha256 = dependency.Sha256 };
        }
    }
}
