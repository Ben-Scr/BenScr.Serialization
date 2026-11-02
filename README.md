# BenScr.Serialization

Typed JSON, XML, MessagePack, gzip compression and authenticated encrypted JSON for .NET 9 and .NET 10.

Created by Ben Schneider — https://github.com/Ben-Scr

```sh
dotnet add package BenScr.Serialization --version 1.0.0
```

```csharp
using BenScr.Serialization;

var player = new Player { Name = "Ada", Score = 42 };
Json.Save("saves/player.json", player);
Player restored = Json.Load<Player>("saves/player.json");

byte[] compact = Binary.Serialize(player);
Player? fromMemory = Binary.Deserialize<Player>(compact);

FileConverter.ConvertTo<Player>(
    "saves/player.json", Format.Json, Format.Xml, "saves/player.xml");

public sealed class Player
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
}
```

The [complete example](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/Playground/Program.cs)
also covers gzip, encrypted JSON and caller-owned streams.

## Formats and contracts

| API | Representation and defaults |
| --- | --- |
| Json | System.Text.Json; public fields included, case-insensitive property names, compact UTF-8 output. |
| Xml | XmlSerializer-compatible application types, UTF-8 XML declaration, DTDs and external entities prohibited. |
| Binary | MessagePack 3.1.12, contractless public members, Lz4BlockArray, UntrustedData security settings. |
| JsonSecure | BSSJ version 1: password-derived AES-256-GCM with authenticated metadata and a random salt/nonce. |
| FileConverter | JSON/XML/MessagePack conversion through an explicit generic type T. |

Json and Xml provide SerializeCompressed, DeserializeCompressed, SaveCompressed and LoadCompressed
for a single gzip member. Both compressed length and expanded bytes are checked. Gzip CRC32 and
length footers are validated, including when a truncated stream would otherwise return partial data.
CRC32 detects corruption; it does not authenticate a file.

Save creates parent directories, serializes before replacing the destination, and uses a temporary file
in the same directory. Serialization failure leaves an existing destination unchanged. Replacement is
not a power-loss durability guarantee and does not promise preservation of old file metadata.

Load returns the supplied default only when the file is missing or the parsed document is null/nil.
Malformed data, authentication failures, access errors and unrelated user-code exceptions are not
converted into a success or fallback value. Invalid arguments are programming errors.

Write and Read leave their caller-owned streams open. Read consumes through EOF; it is intended for
one complete document, not framed network messages. A failed destination write may have written a prefix.

## Limits and custom behavior

```csharp
var limits = new SerializationLimits(maxBytes: 1024 * 1024, maxDepth: 32);
Player? player = Json.Deserialize<Player>(jsonText, limits: limits);
```

Defaults are 16 MiB per encoded/expanded payload and nesting depth 64. Depth is configurable from
1 through 128. Limits are inclusive. JSON limits count encoded UTF-8 bytes, not UTF-16 characters.
XML depth counts element levels, with the root at level 1. XML checks nesting before constructing
application objects, including unknown elements.

JSON and XML serialization bound their encoded buffers. MessagePack serialization checks its encoded
result before returning, and can allocate intermediate buffers; MessagePack expansion limits apply
during deserialization. Object sizes, application getters/setters, constructors, custom converters and
custom MessagePack formatters are not a resource sandbox. Choose smaller limits for exposed inputs
and use only trusted application types and custom options.

Json.DefaultJson and the historical Json.FormatedJson are read-only. FormattedJson is the correctly
spelled alias. To customize, clone the options:

```csharp
var options = new System.Text.Json.JsonSerializerOptions(Json.DefaultJson)
{
    WriteIndented = true
};
Json.SaveCompressed("player.json.gz", player, options: options);
```

Custom JSON options are copied per operation. Their depth cannot exceed SerializationLimits.
MessagePack options retain the caller's resolver/compression but always use UntrustedData protections
and the requested depth/expansion limits. No typeless resolver is enabled by this library.
Callers must synchronize mutation of their own values, streams and options. Independent calls can run
concurrently; concurrent writes to the same file have last-successful-replacement semantics.

## Authenticated encrypted JSON

```csharp
byte[] encrypted = JsonSecure.Encrypt(passphrase, player);
Player? restored = JsonSecure.Decrypt<Player>(passphrase, encrypted);
```

The [format specification](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/docs/FORMATS.md)
defines every byte. Each encryption uses platform cryptographic randomness for a fresh 16-byte salt
and 12-byte nonce, derives a 32-byte key using PBKDF2-HMAC-SHA256 with 600,000 iterations, and uses a
16-byte AES-GCM tag. Authentication succeeds before JSON parsing. Temporary key/plaintext byte buffers
are cleared; managed strings and returned application objects cannot be erased by this API.

Use strong passphrases and protect them outside the document. This API encrypts application data; it is
not a password-verifier database. Envelopes are not bound to filenames or external identities. Applications
that require anti-replay or substitution protection must enforce those higher-level rules.
AES-GCM platform support is required. Windows and Ubuntu x64 are the verification targets; no browser,
native AOT or trimming support is promised.

## Migration and build

Replace SerializerCS rather than installing both packages with overlapping namespaces. Review the
[migration matrix](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/MIGRATION.md), especially
typed conversion, immutable JSON defaults and encrypted file incompatibility. EasySerialize is a distinct
keyed-storage wrapper; its separate APIs are inventoried and are not silently treated as aliases.

Requirements: .NET SDK 10.0.303, .NET 9/10 runtimes, PowerShell 7 and Git. SDK and package versions are
pinned, transitive dependencies locked, and public APIs checked against a reviewed baseline.

```powershell
dotnet test --project tests/BenScr.Serialization.Tests.csproj -c Release
pwsh -File tools/Verify.ps1
```

The verifier checks builds, tests, API surface, archive metadata, license/icon, matching portable PDBs
and Source Link checksums. It then runs an external consumer with an empty package cache. A standalone
consumer check can target the public feed:

```powershell
pwsh -File tools/VerifyConsumer.ps1 -Published
```

See [release procedure](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/RELEASE.md),
[format/provenance details](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/docs/PROVENANCE.md)
and [changelog](https://github.com/Ben-Scr/BenScr.Serialization/blob/master/CHANGELOG.md).
