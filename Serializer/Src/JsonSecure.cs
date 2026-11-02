using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace BenScr.Serialization;

/// <summary>Authenticated password-encrypted JSON in the BSSJ format. Legacy CBC files are rejected.</summary>
public static class JsonSecure
{
    private const int HeaderSize = 5;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private const int Overhead = HeaderSize + SaltSize + NonceSize + TagSize;

    /// <summary>Encrypts bounded JSON with AES-256-GCM and PBKDF2-SHA256 (600,000 iterations).</summary>
    /// <remarks>Each call uses a fresh 16-byte salt and 12-byte nonce. The tag is 16 bytes.</remarks>
    public static byte[] Encrypt<T>(string password, T obj, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ValidatePassword(password);
        limits ??= SerializationLimits.Default;
        byte[] plaintext = Json.Encode(obj, options, limits);
        byte[]? key = null;
        try
        {
            PayloadIO.CheckLength((long)plaintext.Length + Overhead, limits.MaxBytes);
            byte[] output = new byte[plaintext.Length + Overhead];
            "BSSJ"u8.CopyTo(output);
            output[4] = 1;
            Span<byte> salt = output.AsSpan(HeaderSize, SaltSize);
            Span<byte> nonce = output.AsSpan(HeaderSize + SaltSize, NonceSize);
            Span<byte> tag = output.AsSpan(HeaderSize + SaltSize + NonceSize, TagSize);
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(nonce);
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            using var cipher = new AesGcm(key, TagSize);
            cipher.Encrypt(nonce, plaintext, output.AsSpan(Overhead), tag, output.AsSpan(0, HeaderSize + SaltSize + NonceSize));
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>Authenticates before parsing. Wrong passwords/tampering throw CryptographicException.</summary>
    /// <remarks>Unknown versions, legacy files and truncated headers throw InvalidDataException.</remarks>
    public static T? Decrypt<T>(string password, byte[] bytes, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ValidatePassword(password);
        ArgumentNullException.ThrowIfNull(bytes);
        limits ??= SerializationLimits.Default;
        PayloadIO.CheckLength(bytes.Length, limits.MaxBytes);
        if (bytes.Length < Overhead || !bytes.AsSpan(0, 4).SequenceEqual("BSSJ"u8) || bytes[4] != 1)
        {
            throw new InvalidDataException("Expected a complete BSSJ version 1 encrypted JSON payload.");
        }
        ReadOnlySpan<byte> salt = bytes.AsSpan(HeaderSize, SaltSize);
        ReadOnlySpan<byte> nonce = bytes.AsSpan(HeaderSize + SaltSize, NonceSize);
        ReadOnlySpan<byte> tag = bytes.AsSpan(HeaderSize + SaltSize + NonceSize, TagSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        byte[] plaintext = new byte[bytes.Length - Overhead];
        try
        {
            using var cipher = new AesGcm(key, TagSize);
            cipher.Decrypt(nonce, bytes.AsSpan(Overhead), tag, plaintext, bytes.AsSpan(0, HeaderSize + SaltSize + NonceSize));
            return Json.Decode<T>(plaintext, options, limits);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Replaces a file only after encryption succeeds, creating parent directories as needed.</summary>
    public static void Save<T>(string path, string password, T obj, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
        => PayloadIO.Save(path, Encrypt(password, obj, options, limits));

    /// <summary>Only a missing file or authenticated JSON null returns defaultValue; other failures propagate.</summary>
    public static T Load<T>(string path, string password, T defaultValue = default!, JsonSerializerOptions? options = null, SerializationLimits? limits = null)
    {
        ValidatePassword(password);
        limits ??= SerializationLimits.Default;
        byte[]? bytes = PayloadIO.Load(path, limits.MaxBytes);
        return bytes is null ? defaultValue : Decrypt<T>(password, bytes, options, limits) ?? defaultValue;
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(password), "Passwords may contain at most 1,024 UTF-16 code units.");
        }
    }
}
