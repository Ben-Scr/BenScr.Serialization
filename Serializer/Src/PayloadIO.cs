using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace BenScr.Serialization;

internal static class PayloadIO
{
    internal static void CheckLength(long length, int maximum)
    {
        if (length > maximum)
        {
            throw new InvalidDataException("The payload exceeds the configured byte limit.");
        }
    }

    internal static byte[] Read(Stream source, int maximum)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var output = new LimitedMemoryStream(maximum);
        source.CopyTo(output, 81920);
        return output.ToArray();
    }

    internal static byte[]? Load(string path, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            CheckLength(source.Length, maximum);
            return Read(source, maximum);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal static void Save(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, ".benscr-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static byte[] Compress(byte[] bytes, CompressionLevel level, int maximum)
    {
        using var output = new LimitedMemoryStream(maximum);
        using (var gzip = new GZipStream(output, level, leaveOpen: true))
        {
            gzip.Write(bytes);
        }
        return output.ToArray();
    }

    internal static byte[] Expand(byte[] bytes, int maximum)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        CheckLength(bytes.Length, maximum);
        if (bytes.Length < 18)
        {
            throw new InvalidDataException("The gzip payload is truncated.");
        }
        using var input = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        byte[] expanded = Read(gzip, maximum);
        // A missing footer can otherwise yield partial output from GZipStream.
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8));
        uint expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4));
        if (expectedLength != (uint)expanded.Length || expectedCrc != Crc32(expanded))
        {
            throw new InvalidDataException("The gzip checksum or uncompressed length is invalid.");
        }
        return expanded;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }
        return ~crc;
    }

    internal sealed class LimitedMemoryStream(int maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckLength(Position + count, maximum);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckLength(Position + buffer.Length, maximum);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            CheckLength(Position + 1, maximum);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            CheckLength(value, maximum);
            base.SetLength(value);
        }
    }
}
