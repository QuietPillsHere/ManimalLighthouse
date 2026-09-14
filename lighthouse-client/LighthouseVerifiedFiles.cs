using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Client;

// Session-only verification for files that are loaded by the replacement scene.
// The cache deliberately contains no persisted state: every process still gets a
// fresh content check the first time it sees a file.
internal static class LighthouseVerifiedFiles
{
    private const int HashBufferSize = 1024 * 1024;
    private const double ProgressIntervalSeconds = 0.1;
    private static readonly object CacheLock = new();
    private static readonly HashSet<VerificationKey> Verified = new();

    internal static void Verify(
        string root,
        string relativePath,
        string expectedSha256,
        CancellationToken cancellationToken,
        Action<float>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A file root is required.", nameof(root));
        }

        ValidateSha256(expectedSha256);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ManifestRules.Resolve(root, relativePath);
        var before = GetExistingFile(path);
        var expected = expectedSha256.ToLowerInvariant();
        var key = new VerificationKey(path, expected, before.Length, before.LastWriteTimeUtc.Ticks);

        lock (CacheLock)
        {
            if (Verified.Contains(key))
            {
                progress?.Invoke(1f);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var actual = Hash(path, before.Length, cancellationToken, progress);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Missing or mismatched Lighthouse payload: " + path);
        }

        var after = GetExistingFile(path);
        if (after.Length != before.Length || after.LastWriteTimeUtc.Ticks != before.LastWriteTimeUtc.Ticks)
        {
            throw new IOException("Lighthouse file changed during verification: " + path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Report completion before publishing the cache entry. If a progress
        // callback cancels or fails, this verification must remain uncached.
        progress?.Invoke(1f);
        cancellationToken.ThrowIfCancellationRequested();

        lock (CacheLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Verified.Add(key);
        }
    }

    private static FileInfo GetExistingFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidDataException("Missing Lighthouse payload: " + path);
        }

        return file;
    }

    private static string Hash(
        string path,
        long expectedLength,
        CancellationToken cancellationToken,
        Action<float>? progress)
    {
        using var input = OpenForVerification(path);
        using var hash = SHA256.Create();
        var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            var totalRead = 0L;
            var lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();

            progress?.Invoke(0f);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                hash.TransformBlock(buffer, 0, read, buffer, 0);
                totalRead += read;

                if (progress != null && expectedLength > 0)
                {
                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    var elapsed = (now - lastProgress) / (double)System.Diagnostics.Stopwatch.Frequency;

                    if (elapsed >= ProgressIntervalSeconds)
                    {
                        var value = Math.Min(1f, totalRead / (float)expectedLength);
                        if (value < 1f)
                        {
                            lastProgress = now;
                            progress(value);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
            }

            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (totalRead != expectedLength)
            {
                throw new IOException("Lighthouse file changed during verification: " + path);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return BitConverter.ToString(hash.Hash!).Replace("-", "").ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static FileStream OpenForVerification(string path)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException("Missing Lighthouse payload: " + path, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new InvalidDataException("Missing Lighthouse payload: " + path, exception);
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value is not { Length: 64 })
        {
            throw new InvalidDataException("SHA-256 is required for every Lighthouse payload.");
        }

        foreach (var character in value)
        {
            if (character is (< '0' or > '9') and (< 'a' or > 'f')
                and (< 'A' or > 'F'))
            {
                throw new InvalidDataException("Invalid SHA-256.");
            }
        }
    }

    private readonly struct VerificationKey : IEquatable<VerificationKey>
    {
        private readonly string _path;
        private readonly string _sha256;
        private readonly long _length;
        private readonly long _modified;

        internal VerificationKey(string path, string sha256, long length, long modified)
        {
            _path = path;
            _sha256 = sha256;
            _length = length;
            _modified = modified;
        }

        public bool Equals(VerificationKey other) =>
            _length == other._length
            && _modified == other._modified
            && string.Equals(_path, other._path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_sha256, other._sha256, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is VerificationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(_path);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_sha256);
                hash = (hash * 397) ^ _length.GetHashCode();
                hash = (hash * 397) ^ _modified.GetHashCode();
                return hash;
            }
        }
    }
}
