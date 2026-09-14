using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Client;

// Verification of files loaded by the replacement scenes. A file is hashed once and
// remembered by path + expected hash + length + mtime; when CacheFile is set the
// entries persist across processes, so the multi-GB native contract only costs a
// full read after an install changes. Size and mtime are the invalidation signal —
// an in-place edit that preserves both would slip through, which is the usual
// trade-off for this kind of cache.
internal static class LighthouseVerifiedFiles
{
    private const int HashBufferSize = 1024 * 1024;
    private const double ProgressIntervalSeconds = 0.1;
    private const int MaxPersistedEntries = 4096;
    private static readonly object CacheLock = new();
    private static readonly HashSet<VerificationKey> Verified = new();
    private static bool _loaded;

    // set once by the plugin; null keeps the cache in memory only (tests, probes)
    internal static string? CacheFile { get; set; }

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
            EnsureLoaded();
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
            if (Verified.Add(key))
            {
                Persist(key);
            }
        }
    }

    // tests only: forget the in-memory entries so the next Verify reloads from disk
    internal static void ClearMemoryCache()
    {
        lock (CacheLock)
        {
            Verified.Clear();
            _loaded = false;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var file = CacheFile;
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return;
        }

        var kept = new List<VerificationKey>();
        var dropped = 0;
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (!VerificationKey.TryParse(line, out var key))
                {
                    dropped++;
                    continue;
                }

                // entries whose file moved on are dead weight; prune them at load
                var info = new FileInfo(key.Path);
                if (!info.Exists || !key.Matches(info))
                {
                    dropped++;
                    continue;
                }

                if (Verified.Add(key))
                {
                    kept.Add(key);
                }
            }
        }
        catch (Exception)
        {
            // an unreadable cache only costs a rehash
            return;
        }

        if (dropped != 0 || kept.Count > MaxPersistedEntries)
        {
            Rewrite(file, kept);
        }
    }

    private static void Persist(VerificationKey key)
    {
        var file = CacheFile;
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, key.ToLine() + "\n");
        }
        catch (Exception)
        {
            // persistence is best effort; the in-memory entry still holds for this process
        }
    }

    private static void Rewrite(string file, List<VerificationKey> keys)
    {
        try
        {
            var temp = file + ".tmp";
            using (var writer = new StreamWriter(temp, false))
            {
                var start = Math.Max(0, keys.Count - MaxPersistedEntries);
                for (var index = start; index < keys.Count; index++)
                {
                    writer.Write(keys[index].ToLine());
                    writer.Write('\n');
                }
            }

            File.Copy(temp, file, true);
            File.Delete(temp);
        }
        catch (Exception)
        {
            // stale lines just get pruned again next load
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

        internal string Path => _path;

        internal bool Matches(FileInfo info) => info.Length == _length && info.LastWriteTimeUtc.Ticks == _modified;

        // one line per entry: sha256 \t length \t mtime ticks \t full path
        internal string ToLine() =>
            _sha256 + "\t" + _length.ToString(CultureInfo.InvariantCulture) + "\t"
            + _modified.ToString(CultureInfo.InvariantCulture) + "\t" + _path;

        internal static bool TryParse(string line, out VerificationKey key)
        {
            key = default;
            var parts = line.Split('\t');
            if (parts.Length != 4 || parts[0].Length != 64 || parts[3].Length == 0)
            {
                return false;
            }

            foreach (var character in parts[0])
            {
                if (character is (< '0' or > '9') and (< 'a' or > 'f'))
                {
                    return false;
                }
            }

            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var modified))
            {
                return false;
            }

            key = new VerificationKey(parts[3], parts[0], length, modified);
            return true;
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
