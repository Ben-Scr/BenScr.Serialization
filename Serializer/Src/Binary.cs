using System;
using System.IO;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace BenScr.Serialization;

/// <summary>MessagePack with contractless public members, LZ4 and untrusted-data protections.</summary>
public static class Binary
{
    /// <summary>Immutable defaults compatible with legacy contractless LZ4 values.</summary>
    public static readonly MessagePackSerializerOptions DefaultMsgPack = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance)
        .WithCompression(MessagePackCompression.Lz4BlockArray)
        .WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(64)
            .WithMaximumDecompressedSize(SerializationLimits.DefaultMaxBytes));

    /// <summary>Serializes one value and checks encoded length before return.</summary>
    /// <remarks>Serialization can allocate intermediate buffers; expanded size is enforced on deserialization.</remarks>
    public static byte[] Serialize<T>(T obj, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[] bytes = MessagePackSerializer.Serialize(obj, Prepare(options, limits));
        PayloadIO.CheckLength(bytes.Length, limits.MaxBytes);
        return bytes;
    }

    /// <summary>Parses exactly one value; trailing bytes are rejected and expanded size/depth are bounded.</summary>
    public static T? Deserialize<T>(byte[] bytes, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        limits ??= SerializationLimits.Default;
        PayloadIO.CheckLength(bytes.Length, limits.MaxBytes);
        var reader = new MessagePackReader(bytes.AsMemory());
        MessagePackSerializerOptions prepared = Prepare(options, limits);
        if (prepared.Compression != MessagePackCompression.None)
        {
            prepared = prepared.WithResolver(new EndCheckingResolver<T>(prepared.Resolver));
        }
        T? result = MessagePackSerializer.Deserialize<T>(ref reader, prepared);
        if (!reader.End)
        {
            throw new InvalidDataException("Trailing data follows the MessagePack value.");
        }
        return result;
    }

    /// <summary>Writes one value without closing the caller's stream.</summary>
    public static void Write<T>(Stream destination, T obj, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(Serialize(obj, options, limits));
    }

    /// <summary>Reads one value to EOF without closing the caller's stream.</summary>
    public static T? Read<T>(Stream source, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        return Deserialize<T>(PayloadIO.Read(source, limits.MaxBytes), options, limits);
    }

    /// <summary>Replaces a file after successful serialization, creating parent directories as needed.</summary>
    public static void Save<T>(string path, T obj, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
        => PayloadIO.Save(path, Serialize(obj, options, limits));

    /// <summary>Loads one value; only a missing file or MessagePack nil returns defaultValue.</summary>
    public static T Load<T>(string path, T defaultValue = default!, MessagePackSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : Deserialize<T>(bytes, options, limits) ?? defaultValue;
    }

    private static MessagePackSerializerOptions Prepare(MessagePackSerializerOptions? options, SerializationLimits limits)
        => (options ?? DefaultMsgPack).WithSecurity(MessagePackSecurity.UntrustedData
            .WithMaximumObjectGraphDepth(limits.MaxDepth).WithMaximumDecompressedSize(limits.MaxBytes));

    // The outer reader sees the complete LZ4 container, so validate its expanded root separately.
    private sealed class EndCheckingResolver<T>(IFormatterResolver inner) : IFormatterResolver
    {
        private readonly IMessagePackFormatter<T> formatter = new EndCheckingFormatter<T>(inner);

        public IMessagePackFormatter<TValue>? GetFormatter<TValue>() => typeof(TValue) == typeof(T)
            ? (IMessagePackFormatter<TValue>)(object)formatter
            : inner.GetFormatter<TValue>();
    }

    private sealed class EndCheckingFormatter<T>(IFormatterResolver inner) : IMessagePackFormatter<T>
    {
        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
            => inner.GetFormatterWithVerify<T>().Serialize(ref writer, value, options.WithResolver(inner));

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            T value = inner.GetFormatterWithVerify<T>().Deserialize(ref reader, options.WithResolver(inner));
            if (!reader.End)
            {
                throw new MessagePackSerializationException("Trailing data follows the expanded MessagePack value.");
            }
            return value;
        }
    }
}
