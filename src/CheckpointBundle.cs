using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace StS2UnDoFloor;

/// <summary>
/// The on-disk / in-cloud form of every checkpoint of one run, as a single file. The envelope is plain JSON so it
/// travels through the game's string-based <c>ISaveStore</c> API unchanged; the checkpoint saves themselves are a
/// JSON array of strings (the game writes indented run JSON, so line framing is out), compressed and base64-encoded.
/// Consecutive saves of one run repeat most of their ~50-100 KB, which only pays off with a window larger than the
/// saves: Brotli with a 4 MB window packs a real run about ten times tighter than gzip (32 KB window). Both are read.
/// Layout:
/// <code>
/// { "schema_version": 1, "run_start_time": 1727000000, "checkpoint_count": 12,
///   "encoding": "brotli-base64", "payload": "G7wB..." }
/// </code>
/// No game types are referenced here so the codec can be tested outside the game.
/// </summary>
internal static class CheckpointBundle
{
    public const int SchemaVersion = 1;

    /// <summary>File name of the bundle inside a profile's saves directory; the same file is overwritten for every run.</summary>
    public const string FileName = "undofloor_checkpoints.save";

    private const string BrotliEncoding = "brotli-base64";
    private const string GzipEncoding = "gzip-base64";

    // Quality 5 / 22-bit window: ~1 KB per checkpoint on real runs in a few ms; higher qualities gain little.
    private const int BrotliQuality = 5;
    private const int BrotliWindowBits = 22;

    public sealed class Decoded
    {
        public long RunStartTime { get; }

        public IReadOnlyList<string> CheckpointJsons { get; }

        public Decoded(long runStartTime, IReadOnlyList<string> checkpointJsons)
        {
            RunStartTime = runStartTime;
            CheckpointJsons = checkpointJsons;
        }
    }

    public static string Encode(long runStartTime, IReadOnlyList<string> checkpointJsons)
    {
        byte[] payload = Compress(checkpointJsons);
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteNumber("run_start_time", runStartTime);
            writer.WriteNumber("checkpoint_count", checkpointJsons.Count);
            writer.WriteString("encoding", BrotliEncoding);
            writer.WriteBase64String("payload", payload);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <exception cref="InvalidDataException">The envelope is malformed or from an unsupported schema.</exception>
    public static Decoded Decode(string envelope)
    {
        using JsonDocument document = JsonDocument.Parse(envelope);
        JsonElement root = document.RootElement;
        int schema = ReadInt(root, "schema_version");
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported checkpoint bundle schema {schema} (this build reads {SchemaVersion}).");
        }
        string encoding = root.TryGetProperty("encoding", out JsonElement encodingElement) ? encodingElement.GetString() ?? "" : "";
        if (encoding != BrotliEncoding && encoding != GzipEncoding)
        {
            throw new InvalidDataException($"Unsupported checkpoint bundle encoding '{encoding}'.");
        }
        long runStartTime = root.GetProperty("run_start_time").GetInt64();
        int count = ReadInt(root, "checkpoint_count");
        List<string> jsons = Decompress(root.GetProperty("payload").GetBytesFromBase64(), encoding == GzipEncoding);
        if (jsons.Count != count)
        {
            throw new InvalidDataException($"Checkpoint bundle declares {count} checkpoints but holds {jsons.Count}.");
        }
        return new Decoded(runStartTime, jsons);
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException($"Checkpoint bundle is missing '{name}'.");
        }
        return element.GetInt32();
    }

    /// <summary>brotli(UTF-8 JSON array of the checkpoint JSON strings).</summary>
    private static byte[] Compress(IReadOnlyList<string> checkpointJsons)
    {
        byte[] plain;
        using (MemoryStream buffer = new MemoryStream())
        {
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                foreach (string json in checkpointJsons)
                {
                    writer.WriteStringValue(json);
                }
                writer.WriteEndArray();
            }
            plain = buffer.ToArray();
        }
        using BrotliEncoder encoder = new BrotliEncoder(BrotliQuality, BrotliWindowBits);
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(plain.Length)];
        OperationStatus status = encoder.Compress(plain, compressed, out int consumed, out int written, isFinalBlock: true);
        if (status != OperationStatus.Done || consumed != plain.Length)
        {
            throw new InvalidOperationException($"Brotli compression did not complete ({status}, {consumed}/{plain.Length} bytes consumed).");
        }
        return compressed.AsSpan(0, written).ToArray();
    }

    private static List<string> Decompress(byte[] payload, bool gzip)
    {
        using MemoryStream input = new MemoryStream(payload);
        using Stream inflater = gzip
            ? new GZipStream(input, CompressionMode.Decompress)
            : new BrotliStream(input, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(inflater);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Checkpoint bundle payload is not a JSON array.");
        }
        List<string> jsons = new List<string>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            jsons.Add(element.GetString() ?? throw new InvalidDataException("Checkpoint bundle payload holds a null entry."));
        }
        return jsons;
    }
}
