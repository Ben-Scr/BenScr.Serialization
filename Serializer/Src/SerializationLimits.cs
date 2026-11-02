using System;

namespace BenScr.Serialization;

/// <summary>Immutable limits applied to each encoded and expanded payload.</summary>
public sealed class SerializationLimits
{
    /// <summary>The default byte limit, 16 MiB.</summary>
    public const int DefaultMaxBytes = 16 * 1024 * 1024;
    /// <summary>The default nesting limit.</summary>
    public const int DefaultMaxDepth = 64;
    /// <summary>Gets the immutable default limits.</summary>
    public static SerializationLimits Default { get; } = new();
    /// <summary>Gets the inclusive encoded and expanded byte limit.</summary>
    public int MaxBytes { get; }
    /// <summary>Gets the inclusive nesting limit.</summary>
    public int MaxDepth { get; }

    /// <summary>Creates limits; bytes must be positive and depth between 1 and 128.</summary>
    public SerializationLimits(int maxBytes = DefaultMaxBytes, int maxDepth = DefaultMaxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (maxDepth is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }
        MaxBytes = maxBytes;
        MaxDepth = maxDepth;
    }
}
