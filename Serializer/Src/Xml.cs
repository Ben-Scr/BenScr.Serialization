using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace BenScr.Serialization;

/// <summary>Typed XML and single-member gzip XML with byte limits and external entities disabled.</summary>
public static class Xml
{
    /// <summary>Serializes to UTF-8 XML, including a declaration, within the encoded byte limit.</summary>
    public static byte[] Serialize<T>(T obj, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        using var stream = new PayloadIO.LimitedMemoryStream(limits.MaxBytes);
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }))
        {
            new XmlSerializer(typeof(T)).Serialize(writer, obj);
        }
        byte[] result = stream.ToArray();
        Validate(result, limits);
        return result;
    }

    /// <summary>Parses one typed XML document; DTDs, external entities and trailing documents are rejected.</summary>
    public static T? Deserialize<T>(byte[] bytes, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        limits ??= SerializationLimits.Default;
        PayloadIO.CheckLength(bytes.Length, limits.MaxBytes);
        Validate(bytes, limits);
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, Settings(limits));
        return (T?)new XmlSerializer(typeof(T)).Deserialize(reader);
    }

    /// <summary>Serializes XML and compresses it as one gzip member within both byte limits.</summary>
    public static byte[] SerializeCompressed<T>(T obj, CompressionLevel compressionLevel = CompressionLevel.Fastest, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return PayloadIO.Compress(Serialize(obj, limits), compressionLevel, limits.MaxBytes);
    }

    /// <summary>Expands one gzip member with footer validation, then parses typed XML.</summary>
    public static T? DeserializeCompressed<T>(byte[] bytes, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return Deserialize<T>(PayloadIO.Expand(bytes, limits.MaxBytes), limits);
    }

    /// <summary>Writes typed XML without closing the caller's stream.</summary>
    public static void Write<T>(Stream destination, T obj, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(Serialize(obj, limits));
    }

    /// <summary>Reads typed XML to EOF without closing the caller's stream.</summary>
    public static T? Read<T>(Stream source, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return Deserialize<T>(PayloadIO.Read(source, limits.MaxBytes), limits);
    }

    /// <summary>Replaces a file after successful serialization, creating parent directories as needed.</summary>
    public static void Save<T>(string path, T obj, SerializationLimits? limits = null) => PayloadIO.Save(path, Serialize(obj, limits));

    /// <summary>Saves one gzip member containing typed XML.</summary>
    public static void SaveCompressed<T>(string path, T obj, CompressionLevel compressionLevel = CompressionLevel.Fastest, SerializationLimits? limits = null)
        => PayloadIO.Save(path, SerializeCompressed(obj, compressionLevel, limits));

    /// <summary>Loads typed XML; only a missing file or XML nil returns defaultValue.</summary>
    public static T Load<T>(string path, T defaultValue = default!, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : Deserialize<T>(bytes, limits) ?? defaultValue;
    }

    /// <summary>Loads gzip XML; only a missing file or XML nil returns defaultValue.</summary>
    public static T LoadCompressed<T>(string path, T defaultValue = default!, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : DeserializeCompressed<T>(bytes, limits) ?? defaultValue;
    }

    private static XmlReaderSettings Settings(SerializationLimits limits) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = limits.MaxBytes,
        CloseInput = false
    };

    private static void Validate(byte[] bytes, SerializationLimits limits)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(input, Settings(limits));
        // Inspect unknown elements too, before object construction or arbitrary property setters.
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth >= limits.MaxDepth)
            {
                throw new InvalidDataException("XML exceeds the configured nesting limit.");
            }
        }
    }
}
