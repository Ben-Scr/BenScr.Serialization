# Migration to BenScr.Serialization 1.0.0

The public source is Ben-Scr/Serializer, master at 918d4db5af1ec2b58b782bb765028227c5346447.
SerializerCS 1.1.0 records that commit in its nuspec. Its source project at that commit says 1.0.9;
the published package is the authority for the legacy package version. The repository history and
original author license are retained. The source repository and existing packages are unchanged.

Replace the SerializerCS PackageReference with BenScr.Serialization. The namespace remains
BenScr.Serialization; the assembly identity changes to BenScr.Serialization. Recompile consumers.
Do not reference both packages in the same project. net9.0 remains supported and net10.0 is added.

## Public API mapping

The complete reflected inventories are docs/LegacySerializerApi.txt and docs/LegacyEasySerializeApi.txt.
Every reflected member is classified individually in docs/api-migration.json.

| Legacy API | New behavior/replacement |
| --- | --- |
| Format.Json, Xml, Binary | Names and numeric values 0, 1, 2 retained. |
| Json.Serialize, SerializeCompressed | Retained with an optional final SerializationLimits argument. |
| Json.Save, SaveCompressed | Retained; options now work consistently in compressed files. Complete serialization precedes destination replacement. |
| Json.Load, LoadCompressed | Retained with optional limits. Only missing files and null documents return the fallback. |
| Json.DefaultJson, FormatedJson | Fields retained, but options are immutable. Clone to customize. FormattedJson alias added. |
| Xml.Serialize, SerializeCompressed, Save, SaveCompressed | Retained with optional limits; UTF-8, deterministic declaration, bounded output. |
| Xml.Load, LoadCompressed | Retained with optional limits; DTDs/XXE prohibited and depth checked before construction. Missing uncompressed XML now uses the same fallback contract. |
| Binary.Serialize, Save, Load | Retained with optional limits; MessagePack updated from 3.1.4 to 3.1.12 with UntrustedData protections and bounded expansion. |
| Binary.DefaultMsgPack | Field retained; contractless LZ4 defaults retained with security protections. |
| JsonSecure.Save, Load | Call shape retained with optional limits, but file format changes to authenticated BSSJ v1. Corruption and wrong-password failures now propagate. |
| FileConverter.ConvertTo | Replaced by ConvertTo<T>; a concrete application schema is required for reliable XML/JSON/MessagePack conversion. |
| Read/Write and Deserialize methods | Added for explicit in-memory and caller-owned stream use. |

Optional parameter additions preserve ordinary source calls, not binary compatibility with the old
assembly. Limits intentionally reject oversized/deep documents previously accepted without bounds.
User-supplied converters/formatters may impose additional type or resource constraints.

## Stored data

JSON, XML, gzip JSON/XML, and MessagePack remain interoperable with legacy valid representations.
Checked-in fixture files were produced using the actual published SerializerCS 1.1.0 with its
MessagePack 3.1.4 dependency and are read by the new implementation in tests. Explicitly permissive
legacy behavior, such as changing shared JSON defaults or relying on ignored compressed options, is
not preserved.

The old JsonSecure format was salt(16) + IV(16) + AES-CBC ciphertext, using PBKDF2-SHA256 at 100,000
iterations and no authentication tag. It also hid some decoding failures. It is not accepted by BSSJ v1.
For trusted historical files only, use a separately isolated original-version application to read and
validate the data, then write it with the new library. Retain backups and verify the migrated values.
There is no automatic fallback to the unauthenticated format.

## EasySerialize comparison

The publicly distributed EasySerialize 1.0.0 is a separate assembly and depends on SerializerCS >=1.0.7.
Its public surface is a keyed filesystem wrapper with global MainPath, Format and IsCompressed fields,
key serialization/loading/deletion, directory listing/counting/clearing, and path-extension helpers.
It is not an alternate name for SerializerCS.

Those keyed-store and directory-management APIs are outside this package's migration scope. Applications
can retain EasySerialize while planning their own store migration, or move their explicit path/schema
management to Json/Xml/Binary and System.IO. This package does not reproduce recursive deletion or
global keyed-store state. docs/api-migration.json maps every member as outside scope with an explicit
replacement direction. No private EasySerialize source is included or required for these observations;
the comparison uses its public NuGet assembly, nuspec, README and license.
