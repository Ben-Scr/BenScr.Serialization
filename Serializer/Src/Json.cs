using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BenScr.Serialization;

/// <summary>JSON and single-member gzip JSON. Caller-provided streams remain open.</summary>
public static class Json
{
    /// <summary>Read-only defaults: public fields, case-insensitive names, compact output.</summary>
    public static readonly JsonSerializerOptions DefaultJson = CreateDefaults(false);
    /// <summary>Read-only indented defaults; the historical spelling is retained.</summary>
    public static readonly JsonSerializerOptions FormatedJson = CreateDefaults(true);
    /// <summary>Gets the read-only indented defaults.</summary>
    public static JsonSerializerOptions FormattedJson => FormatedJson;

    /// <summary>Serializes JSON within the encoded UTF-8 byte and nesting limits.</summary>
    public static string Serialize<T>(T obj, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
        => Encoding.UTF8.GetString(Encode(obj, options, limits ?? SerializationLimits.Default));

    /// <summary>Parses one JSON value. JSON null returns default(T); malformed input throws JsonException.</summary>
    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        limits ??= SerializationLimits.Default;
        PayloadIO.CheckLength(Encoding.UTF8.GetByteCount(json), limits.MaxBytes);
        return JsonSerializer.Deserialize<T>(json, Prepare(options, limits));
    }

    /// <summary>Serializes to one gzip member; JSON and compressed bytes obey the byte limit.</summary>
    public static byte[] SerializeCompressed<T>(T obj, CompressionLevel compressionLevel = CompressionLevel.Fastest,
        JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return PayloadIO.Compress(Encode(obj, options, limits), compressionLevel, limits.MaxBytes);
    }

    /// <summary>Expands one gzip JSON member within the byte limit, validates its footer and parses it.</summary>
    public static T? DeserializeCompressed<T>(byte[] bytes, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return Decode<T>(PayloadIO.Expand(bytes, limits.MaxBytes), options, limits);
    }

    /// <summary>Writes a complete UTF-8 JSON value without closing the stream; write errors propagate.</summary>
    public static void Write<T>(Stream destination, T obj, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(Encode(obj, options, limits ?? SerializationLimits.Default));
    }

    /// <summary>Reads one UTF-8 JSON document to EOF without closing the stream, enforcing byte limits.</summary>
    public static T? Read<T>(Stream source, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return Decode<T>(PayloadIO.Read(source, limits.MaxBytes), options, limits);
    }

    /// <summary>Replaces a file after successful serialization; creates missing parent directories.</summary>
    public static void Save<T>(string path, T obj, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
        => PayloadIO.Save(path, Encode(obj, options, limits ?? SerializationLimits.Default));

    /// <summary>Saves gzip JSON; supplied JSON options are honored.</summary>
    public static void SaveCompressed<T>(string path, T obj, CompressionLevel compressionLevel = CompressionLevel.Fastest,
        JsonSerializerOptions? options = null, SerializationLimits? limits = null)
        => PayloadIO.Save(path, SerializeCompressed(obj, compressionLevel, options, limits));

    /// <summary>Loads JSON; only a missing file or JSON null returns defaultValue. Other failures propagate.</summary>
    public static T Load<T>(string path, T defaultValue = default!, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : Decode<T>(bytes, options, limits) ?? defaultValue;
    }

    /// <summary>Loads gzip JSON; only a missing file or JSON null returns defaultValue.</summary>
    public static T LoadCompressed<T>(string path, T defaultValue = default!, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : DeserializeCompressed<T>(bytes, options, limits) ?? defaultValue;
    }

    internal static byte[] Encode<T>(T obj, JsonSerializerOptions? options, SerializationLimits limits)
    {
        using var stream = new PayloadIO.LimitedMemoryStream(limits.MaxBytes);
        JsonSerializer.Serialize(stream, obj, Prepare(options, limits));
        return stream.ToArray();
    }

    internal static T? Decode<T>(byte[] bytes, JsonSerializerOptions? options, SerializationLimits limits)
    {
        PayloadIO.CheckLength(bytes.Length, limits.MaxBytes);
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(), Prepare(options, limits));
    }

    private static JsonSerializerOptions Prepare(JsonSerializerOptions? options, SerializationLimits limits)
    {
        var result = new JsonSerializerOptions(options ?? DefaultJson);
        result.MaxDepth = Math.Min(result.MaxDepth == 0 ? 64 : result.MaxDepth, limits.MaxDepth);
        return result;
    }

    private static JsonSerializerOptions CreateDefaults(bool indented)
    {
        var result = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true, WriteIndented = indented };
        result.MakeReadOnly(populateMissingResolver: true);
        return result;
    }
}
