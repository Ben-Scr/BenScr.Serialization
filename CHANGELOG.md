# Changelog

## 1.0.0

- Migrate SerializerCS to BenScr.Serialization, retaining net9.0 and adding net10.0.
- Preserve JSON, XML, gzip and contractless MessagePack representations with legacy fixture coverage.
- Add byte/depth limits, stream APIs and in-memory deserialization.
- Honor compressed JSON options and make shared defaults read-only.
- Reject XML DTDs/external entities, truncated gzip and trailing MessagePack data.
- Require an explicit type for reliable cross-format file conversion.
- Replace unauthenticated encrypted JSON with versioned AES-256-GCM and documented migration.
- Add package verification, public API baselines, portable PDB/Source Link and fresh consumer checks.
