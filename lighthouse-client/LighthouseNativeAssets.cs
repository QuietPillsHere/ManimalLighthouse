using System;
using System.IO;
using System.Threading;
using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseNativeAssets
{
    // Called by the serialized scene-loading operation on its verification worker.
    // Unity owns and resolves the external objects; no native assets are destroyed here.
    internal static void Verify(string dataPath, ContentManifest manifest, CancellationToken cancellationToken)
    {
        Verify(dataPath, manifest, cancellationToken, null);
    }

    internal static void Verify(
        string dataPath,
        ContentManifest manifest,
        CancellationToken cancellationToken,
        Action<float>? progress)
    {
        var count = manifest.NativeFiles.Count;
        if (count == 0)
        {
            progress?.Invoke(1f);
            return;
        }

        var index = 0;
        foreach (var dependency in manifest.NativeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ManifestRules.Resolve(dataPath, dependency.Path);
            if (!File.Exists(path))
            {
                throw new InvalidDataException("Lighthouse requires the supported SPT client data. Missing: " + dependency.Path);
            }

            var fileIndex = index++;
            Action<float>? fileProgress = progress == null
                ? null
                : value =>
                {
                    var clamped = value < 0f ? 0f : value > 1f ? 1f : value;
                    progress((fileIndex + clamped) / count);
                };

            try
            {
                LighthouseVerifiedFiles.Verify(dataPath, dependency.Path, dependency.Sha256, cancellationToken, fileProgress);
            }
            catch (InvalidDataException exception) when (!File.Exists(path))
            {
                throw new InvalidDataException("Lighthouse requires the supported SPT client data. Missing: " + dependency.Path, exception);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException("Lighthouse native assets do not match this content build: " + dependency.Path
                    + ". Install the supported SPT client/content combination.", exception);
            }
            catch (IOException exception)
            {
                throw new IOException("Native player data changed during verification: " + dependency.Path, exception);
            }
        }
    }
}
