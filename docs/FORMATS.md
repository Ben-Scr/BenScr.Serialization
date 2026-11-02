# Format contracts

JSON uses System.Text.Json. XML uses System.Xml.Serialization.XmlSerializer with DTD processing
prohibited and no external resolver. MessagePack uses MessagePack-CSharp 3.1.12 with the standard
contractless resolver, Lz4BlockArray compression and UntrustedData protections. These formats have
their own versioning/schema contracts; this package does not add an outer version byte to legacy files.

Gzip methods accept one logical member and validate its final CRC32 and ISIZE against the expanded
bytes. Concatenated-member streams and trailing bytes are outside the contract. CRC32 is a standard
integrity checksum, not authentication. Encoded and expanded lengths are bounded before parsing.

## BSSJ version 1

All sizes below are in bytes. The maximum envelope size is SerializationLimits.MaxBytes, including
the 49-byte overhead. The UTF-8 JSON plaintext is also subject to JSON size/depth limits.

| Offset | Length | Meaning |
| --- | --- | --- |
| 0 | 4 | ASCII BSSJ magic |
| 4 | 1 | Version 1 |
| 5 | 16 | Random salt |
| 21 | 12 | Random AES-GCM nonce |
| 33 | 16 | Authentication tag |
| 49 | Remaining | AES-GCM ciphertext of UTF-8 JSON |

Key derivation is the platform Rfc2898DeriveBytes.Pbkdf2 implementation: password string encoded as
UTF-8, 16-byte salt, PBKDF2-HMAC-SHA256, 600,000 iterations, 32-byte output. Passwords are nonempty and
limited to 1,024 UTF-16 code units; no normalization or trimming is performed.

AES-256-GCM uses the 32-byte derived key, 12-byte nonce and 16-byte tag. Bytes 0 through 32 are
authenticated associated data. Each encryption call obtains a fresh salt and nonce through
RandomNumberGenerator. Callers cannot inject nonces. Decryption authenticates before parsing JSON.
Changing magic/version is rejected before derivation; changing salt, nonce, tag or ciphertext fails
authentication. The format has no attacker-controlled work-factor field.

Unsupported versions and truncated headers throw InvalidDataException. Tag mismatch or wrong password
throws CryptographicException (normally AuthenticationTagMismatchException). A valid encrypted JSON
null returns null/default according to the API. Application JSON failures remain visible.

PBKDF2 is used here because the .NET platform supplies it and the previous API is password based.
Its work factor follows the current PBKDF2-SHA256 guidance; this is not a FIPS certification claim or
an assertion that password-derived encryption is immune to offline guessing. Future parameter changes
require a new format version.

References:
- [Microsoft AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm)
- [Microsoft PBKDF2](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2)
- [OWASP PBKDF2 work factors](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2)
- [MessagePack security](https://github.com/MessagePack-CSharp/MessagePack-CSharp#security)
