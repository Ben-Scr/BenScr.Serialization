using System;
using System.IO;

namespace BenScr.Serialization;

/// <summary>Supported non-encrypted, non-gzip file formats.</summary>
public enum Format
{
    /// <summary>UTF-8 JSON.</summary>
    Json = 0,
    /// <summary>Typed XML.</summary>
    Xml = 1,
    /// <summary>Contractless MessagePack with optional LZ4 compression.</summary>
    Binary = 2
}

/// <summary>Converts files through an explicit application-owned data type.</summary>
public static class FileConverter
{
    /// <summary>Converts a file using T as its schema; null documents and missing sources are rejected.</summary>
    /// <remarks>The source is parsed before destination replacement. Omitted newPath changes the extension.</remarks>
    public static void ConvertTo<T>(string path, Format original, Format to, string? newPath = null, SerializationLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Enum.IsDefined(original) || !Enum.IsDefined(to))
        {
            throw new ArgumentOutOfRangeException(nameof(original), "Both formats must be defined values.");
        }
        limits ??= SerializationLimits.Default;
        byte[] bytes = PayloadIO.Load(path, limits.MaxBytes) ?? throw new FileNotFoundException("Source file not found.", path);
        T? value = original switch
        {
            Format.Json => Json.Decode<T>(bytes, null, limits),
            Format.Xml => Xml.Deserialize<T>(bytes, limits),
            Format.Binary => Binary.Deserialize<T>(bytes, limits: limits),
            _ => throw new ArgumentOutOfRangeException(nameof(original))
        };
        if (value is null)
        {
            throw new InvalidDataException("A null document cannot be converted to a typed file.");
        }
        newPath ??= Path.ChangeExtension(path, to switch { Format.Json => ".json", Format.Xml => ".xml", _ => ".bin" });
        switch (to)
        {
            case Format.Json: Json.Save(newPath, value, limits: limits); break;
            case Format.Xml: Xml.Save(newPath, value, limits); break;
            case Format.Binary: Binary.Save(newPath, value, limits: limits); break;
        }
    }
}
