using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using BenScr.Serialization;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Xunit;

namespace BenScr.Serialization.Tests;

public sealed class SerializationTests
{
    private static Sample Data() => new() { Name = "Zoë 東京", Count = 42, Values = [1, 2, 3] };

    [Fact]
    public void CompressedMessagePackRejectsTrailingExpandedValues()
    {
        var resolver = CompositeResolver.Create([new AppendedValueFormatter()], [StandardResolver.Instance]);
        var options = Binary.DefaultMsgPack.WithResolver(resolver).WithCompressionMinLength(1);
        byte[] payload = MessagePackSerializer.Serialize(new int[128], options, TestContext.Current.CancellationToken);
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<int[]>(payload));
    }

    [Fact]
    public void MessagePackDepthCannotBeRelaxedByCallerOptions()
    {
        byte[] payload = [0x91, 0x91, 0x91, 0x91, 0x01];
        var options = Binary.DefaultMsgPack.WithSecurity(MessagePackSecurity.TrustedData);
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<object>(payload, options, new SerializationLimits(maxDepth: 2)));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("binary")]
    public void StreamWriteFailuresPropagateWithoutClosingTheDestination(string format)
    {
        using var destination = new BrokenWriteStream();
        Assert.Throws<IOException>(() =>
        {
            if (format == "json") { Json.Write(destination, Data()); }
            else if (format == "xml") { Serialization.Xml.Write(destination, Data()); }
            else { Binary.Write(destination, Data()); }
        });
        Assert.True(destination.CanWrite);
    }

    [Theory]
    [InlineData("legacy.json")]
    [InlineData("legacy.json.gz")]
    [InlineData("legacy.xml")]
    [InlineData("legacy.xml.gz")]
    [InlineData("legacy.msgpack")]
    public void PublishedLegacyFixturesRemainReadable(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
        int[] actual = file switch
        {
            "legacy.json" => Json.Load<int[]>(path),
            "legacy.json.gz" => Json.LoadCompressed<int[]>(path),
            "legacy.xml" => Serialization.Xml.Load<int[]>(path),
            "legacy.xml.gz" => Serialization.Xml.LoadCompressed<int[]>(path),
            _ => Binary.Load<int[]>(path)
        };
        Assert.Equal(new[] { 1, 42, -7, 0, int.MaxValue }, actual);
    }

    [Fact]
    public void BinaryRejectsImpossibleCollectionLengthsBeforeAllocation()
    {
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<int[]>([0xdd, 0x7f, 0xff, 0xff, 0xff]));
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<Dictionary<string, int>>([0xdf, 0x7f, 0xff, 0xff, 0xff]));
    }

    [Fact]
    public void ShortMalformedMessagePackIsBounded()
    {
        var random = new Random(910513);
        var limits = new SerializationLimits(512, 8);
        for (int iteration = 0; iteration < 2000; iteration++)
        {
            byte[] bytes = new byte[random.Next(0, 128)];
            random.NextBytes(bytes);
            try { Binary.Deserialize<object>(bytes, limits: limits); }
            catch (MessagePackSerializationException) { }
            catch (InvalidDataException) { }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("json-gzip")]
    [InlineData("xml")]
    [InlineData("xml-gzip")]
    [InlineData("binary")]
    public void TypedRoundTrip(string format)
    {
        Sample input = Data();
        Sample? output = format switch
        {
            "json" => Json.Deserialize<Sample>(Json.Serialize(input)),
            "json-gzip" => Json.DeserializeCompressed<Sample>(Json.SerializeCompressed(input)),
            "xml" => Serialization.Xml.Deserialize<Sample>(Serialization.Xml.Serialize(input)),
            "xml-gzip" => Serialization.Xml.DeserializeCompressed<Sample>(Serialization.Xml.SerializeCompressed(input)),
            _ => Binary.Deserialize<Sample>(Binary.Serialize(input))
        };
        Assert.NotNull(output);
        Assert.Equal(input.Name, output.Name);
        Assert.Equal(input.Count, output.Count);
        Assert.Equal(input.Values, output.Values);
    }

    [Fact]
    public void JsonDefaultsIncludeFieldsAndIgnoreCase()
    {
        Assert.Equal(12, Json.Deserialize<FieldSample>("{\"value\":12}")!.Value);
        Assert.Contains("\"Value\":12", Json.Serialize(new FieldSample { Value = 12 }));
        Assert.True(Json.DefaultJson.IsReadOnly);
        Assert.True(Json.FormatedJson.IsReadOnly);
        Assert.Same(Json.FormatedJson, Json.FormattedJson);
        Assert.Throws<InvalidOperationException>(() => Json.DefaultJson.IncludeFields = false);
    }

    [Fact]
    public void JsonCompressedHonorsCustomConverterAndNamingPolicy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.Converters.Add(new MarkerConverter());
        using var temporary = new TemporaryDirectory();
        string path = temporary.File("record.gz");
        Json.SaveCompressed(path, new Marker { Number = 31 }, options: options);
        Assert.Equal(31, Json.LoadCompressed<Marker>(path, options: options).Number);
        using var raw = new MemoryStream(File.ReadAllBytes(path));
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        Assert.Equal("\"marker:31\"", reader.ReadToEnd());
        Assert.False(options.IsReadOnly);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("json-gzip")]
    [InlineData("xml")]
    [InlineData("xml-gzip")]
    [InlineData("binary")]
    public void MissingFileReturnsExplicitDefault(string format)
    {
        using var temporary = new TemporaryDirectory();
        string missing = temporary.File("missing/value");
        Sample expected = Data();
        Sample actual = format switch
        {
            "json" => Json.Load(missing, expected),
            "json-gzip" => Json.LoadCompressed(missing, expected),
            "xml" => Serialization.Xml.Load(missing, expected),
            "xml-gzip" => Serialization.Xml.LoadCompressed(missing, expected),
            _ => Binary.Load(missing, expected)
        };
        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("json-gzip")]
    [InlineData("xml")]
    [InlineData("xml-gzip")]
    [InlineData("binary")]
    public void SaveCreatesParentsAndRoundTripsFiles(string format)
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.File("nested/record.data");
        Sample value = Data();
        Sample loaded;
        switch (format)
        {
            case "json": Json.Save(path, value); loaded = Json.Load<Sample>(path); break;
            case "json-gzip": Json.SaveCompressed(path, value); loaded = Json.LoadCompressed<Sample>(path); break;
            case "xml": Serialization.Xml.Save(path, value); loaded = Serialization.Xml.Load<Sample>(path); break;
            case "xml-gzip": Serialization.Xml.SaveCompressed(path, value); loaded = Serialization.Xml.LoadCompressed<Sample>(path); break;
            default: Binary.Save(path, value); loaded = Binary.Load<Sample>(path); break;
        }
        Assert.Equal(value.Name, loaded.Name);
        Assert.Equal(value.Values, loaded.Values);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void JsonLimitsUseUtf8BytesAndAreInclusive()
    {
        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Assert.Equal("\"é\"", Json.Serialize("é", options, new SerializationLimits(4)));
        Assert.Equal("é", Json.Deserialize<string>("\"é\"", options, new SerializationLimits(4)));
        Assert.Throws<InvalidDataException>(() => Json.Serialize("é", options, new SerializationLimits(3)));
        Assert.Throws<InvalidDataException>(() => Json.Deserialize<string>("\"é\"", options, new SerializationLimits(3)));
    }

    [Fact]
    public void JsonNullHasDefinedBehavior()
    {
        Assert.Equal("null", Json.Serialize<string?>(null));
        Assert.Null(Json.Deserialize<string>("null"));
        using var temporary = new TemporaryDirectory();
        Json.Save<string?>(temporary.File("null"), null);
        Assert.Equal("fallback", Json.Load(temporary.File("null"), "fallback"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{} {}")]
    [InlineData("{\"Count\":\"x\"}")]
    [InlineData("{\"Count\":1,}")]
    public void MalformedJsonFails(string text) => Assert.Throws<JsonException>(() => Json.Deserialize<Sample>(text));

    [Fact]
    public void JsonDepthCannotBeRelaxedByCustomOptions()
    {
        Assert.Throws<JsonException>(() => Json.Deserialize<object>("[[[[0]]]]",
            new JsonSerializerOptions { MaxDepth = 100 }, new SerializationLimits(maxDepth: 3)));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    public void GzipExpansionBombIsBounded(string format)
    {
        byte[] data = format == "json"
            ? Json.SerializeCompressed(new string('x', 5000))
            : Serialization.Xml.SerializeCompressed(new string('x', 5000));
        Assert.True(data.Length < 1024);
        Assert.Throws<InvalidDataException>(() =>
        {
            if (format == "json") { Json.DeserializeCompressed<string>(data, limits: new SerializationLimits(1024)); }
            else { Serialization.Xml.DeserializeCompressed<string>(data, new SerializationLimits(1024)); }
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void TruncatedGzipIsRejected(int removed)
    {
        byte[] bytes = Json.SerializeCompressed(Data());
        Assert.Throws<InvalidDataException>(() => Json.DeserializeCompressed<Sample>(bytes[..^removed]));
    }

    [Fact]
    public void GzipChecksumCorruptionIsRejected()
    {
        byte[] bytes = Json.SerializeCompressed(Data());
        bytes[^8] ^= 1;
        Assert.Throws<InvalidDataException>(() => Json.DeserializeCompressed<Sample>(bytes));
    }

    [Fact]
    public void XmlExternalEntitiesAndDtdAreRejected()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<!DOCTYPE Sample [<!ENTITY secret SYSTEM 'file:///not-allowed'>]><Sample><Name>&secret;</Name></Sample>");
        Assert.Throws<XmlException>(() => Serialization.Xml.Deserialize<Sample>(bytes));
    }

    [Fact]
    public void XmlTrailingAndUnknownDeepContentAreRejected()
    {
        Assert.Throws<XmlException>(() => Serialization.Xml.Deserialize<Sample>("<Sample/><Sample/>"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Serialization.Xml.Deserialize<Sample>(
            "<Sample><Unknown><Nested/></Unknown></Sample>"u8.ToArray(), new SerializationLimits(maxDepth: 2)));
    }

    [Fact]
    public void XmlEncodingDeclarationMatchesUtf8Bytes()
    {
        byte[] bytes = Serialization.Xml.Serialize(Data());
        string text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
        Assert.Contains("東京", text);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    }

    [Fact]
    public void BinaryKnownMessagePackVectorAndTrailingData()
    {
        var uncompressed = Binary.DefaultMsgPack.WithCompression(MessagePackCompression.None);
        Assert.Equal(new byte[] { 0x93, 0x01, 0x02, 0x03 }, Binary.Serialize(new[] { 1, 2, 3 }, uncompressed));
        Assert.Equal(new[] { 1, 2, 3 }, Binary.Deserialize<int[]>([0x93, 0x01, 0x02, 0x03], uncompressed));
        Assert.Throws<InvalidDataException>(() => Binary.Deserialize<int>([0x01, 0x02], uncompressed));
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<int[]>([0x93, 0x01], uncompressed));
    }

    [Fact]
    public void BinaryExpandedSizeIsBoundedEvenWithPermissiveCallerOptions()
    {
        byte[] compressed = Binary.Serialize(new string('x', 10000));
        Assert.True(compressed.Length < 512);
        var permissive = Binary.DefaultMsgPack.WithSecurity(MessagePackSecurity.TrustedData);
        Assert.Throws<MessagePackSerializationException>(() => Binary.Deserialize<string>(compressed, permissive, new SerializationLimits(512)));
        Assert.True(Binary.DefaultMsgPack.Security.HashCollisionResistant);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("binary")]
    public void StreamsRemainOpenAndSupportPartialReads(string format)
    {
        using var destination = new MemoryStream();
        Sample value = Data();
        switch (format)
        {
            case "json": Json.Write(destination, value); break;
            case "xml": Serialization.Xml.Write(destination, value); break;
            default: Binary.Write(destination, value); break;
        }
        Assert.True(destination.CanWrite);
        using var source = new PartialStream(destination.ToArray());
        Sample? result = format switch
        {
            "json" => Json.Read<Sample>(source),
            "xml" => Serialization.Xml.Read<Sample>(source),
            _ => Binary.Read<Sample>(source)
        };
        Assert.Equal(value.Name, result!.Name);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("binary")]
    public void StreamReadFailuresPropagate(string format)
    {
        using var source = new BrokenStream();
        Assert.Throws<IOException>(() =>
        {
            if (format == "json") { Json.Read<Sample>(source); }
            else if (format == "xml") { Serialization.Xml.Read<Sample>(source); }
            else { Binary.Read<Sample>(source); }
        });
        Assert.True(source.CanRead);
    }

    [Fact]
    public void FailedSerializationPreservesExistingFile()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.File("existing");
        File.WriteAllText(path, "original");
        Assert.Throws<InvalidDataException>(() => Json.Save(path, new string('x', 1000), limits: new SerializationLimits(5)));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temporary.Path));
    }

    [Theory]
    [InlineData(Format.Json, Format.Xml)]
    [InlineData(Format.Json, Format.Binary)]
    [InlineData(Format.Xml, Format.Json)]
    [InlineData(Format.Xml, Format.Binary)]
    [InlineData(Format.Binary, Format.Json)]
    [InlineData(Format.Binary, Format.Xml)]
    [InlineData(Format.Json, Format.Json)]
    [InlineData(Format.Xml, Format.Xml)]
    [InlineData(Format.Binary, Format.Binary)]
    public void TypedConversionPreservesData(Format original, Format target)
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.File("source");
        string output = temporary.File("result");
        Sample value = Data();
        switch (original)
        {
            case Format.Json: Json.Save(source, value); break;
            case Format.Xml: Serialization.Xml.Save(source, value); break;
            default: Binary.Save(source, value); break;
        }
        FileConverter.ConvertTo<Sample>(source, original, target, output);
        Sample result = target switch
        {
            Format.Json => Json.Load<Sample>(output),
            Format.Xml => Serialization.Xml.Load<Sample>(output),
            _ => Binary.Load<Sample>(output)
        };
        Assert.Equal(value.Name, result.Name);
        Assert.Equal(value.Count, result.Count);
        Assert.Equal(value.Values, result.Values);
    }

    [Fact]
    public void ConversionValidatesNullMissingAndEnums()
    {
        using var temporary = new TemporaryDirectory();
        Assert.Throws<FileNotFoundException>(() => FileConverter.ConvertTo<Sample>(temporary.File("missing"), Format.Json, Format.Xml));
        Json.Save<Sample?>(temporary.File("null.json"), null);
        Assert.Throws<InvalidDataException>(() => FileConverter.ConvertTo<Sample>(temporary.File("null.json"), Format.Json, Format.Xml));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileConverter.ConvertTo<Sample>(temporary.File("null.json"), (Format)9, Format.Xml));
    }

    [Fact]
    public void IndependentCallsWorkConcurrently()
    {
        Parallel.For(0, 100, i =>
        {
            Assert.Equal(i, Json.Deserialize<int>(Json.Serialize(i)));
            Assert.Equal(i, Binary.Deserialize<int>(Binary.Serialize(i)));
        });
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(-1, 64)]
    [InlineData(10, 0)]
    [InlineData(10, 129)]
    public void LimitsRejectInvalidValues(int bytes, int depth)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SerializationLimits(bytes, depth));

    [Fact]
    public void NullArgumentsAreProgrammingErrors()
    {
        Assert.Throws<ArgumentNullException>(() => Json.Deserialize<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Json.DeserializeCompressed<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Serialization.Xml.Deserialize<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Binary.Deserialize<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Json.Read<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Binary.Write(null!, 1));
    }

    [Fact]
    public void FixedSeedRoundTripsPreserveCollections()
    {
        var random = new Random(71023);
        for (int iteration = 0; iteration < 200; iteration++)
        {
            int[] values = Enumerable.Range(0, random.Next(0, 200)).Select(_ => random.Next()).ToArray();
            Assert.Equal(values, Binary.Deserialize<int[]>(Binary.Serialize(values)));
            Assert.Equal(values, Json.Deserialize<int[]>(Json.Serialize(values)));
            Assert.Equal(values, Serialization.Xml.Deserialize<int[]>(Serialization.Xml.Serialize(values)));
        }
    }

    [Fact]
    public void EncryptionRoundTripsWithIndependentPlatformDecrypt()
    {
        byte[] envelope = JsonSecure.Encrypt("correct horse Ω", Data());
        Assert.Equal("BSSJ", Encoding.ASCII.GetString(envelope, 0, 4));
        Assert.Equal(1, envelope[4]);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("correct horse Ω", envelope.AsSpan(5, 16), 600000, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[envelope.Length - 49];
        using var cipher = new AesGcm(key, 16);
        cipher.Decrypt(envelope.AsSpan(21, 12), envelope.AsSpan(49), envelope.AsSpan(33, 16), plaintext, envelope.AsSpan(0, 33));
        Sample? independentlyRead = JsonSerializer.Deserialize<Sample>(plaintext);
        Assert.Equal(Data().Name, independentlyRead!.Name);
        Assert.Equal(Data().Name, JsonSecure.Decrypt<Sample>("correct horse Ω", envelope)!.Name);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);
    }

    [Fact]
    public void IndependentPlatformEnvelopeCanBeRead()
    {
        byte[] plaintext = "{\"Count\":25,\"Name\":\"fixture\"}"u8.ToArray();
        byte[] envelope = new byte[49 + plaintext.Length];
        "BSSJ"u8.CopyTo(envelope);
        envelope[4] = 1;
        for (int i = 5; i < 33; i++) { envelope[i] = (byte)i; }
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("fixture-only", envelope.AsSpan(5, 16), 600000, HashAlgorithmName.SHA256, 32);
        using var cipher = new AesGcm(key, 16);
        cipher.Encrypt(envelope.AsSpan(21, 12), plaintext, envelope.AsSpan(49), envelope.AsSpan(33, 16), envelope.AsSpan(0, 33));
        Assert.Equal(25, JsonSecure.Decrypt<Sample>("fixture-only", envelope)!.Count);
        CryptographicOperations.ZeroMemory(key);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(21)]
    [InlineData(33)]
    [InlineData(49)]
    public void EncryptedPayloadTamperingIsRejectedBeforeParsing(int index)
    {
        byte[] bytes = JsonSecure.Encrypt("password", Data());
        bytes[index] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => JsonSecure.Decrypt<Sample>("password", bytes));
    }

    [Fact]
    public void EncryptionUsesFreshSaltsAndNonces()
    {
        byte[] first = JsonSecure.Encrypt("same password", Data());
        byte[] second = JsonSecure.Encrypt("same password", Data());
        Assert.NotEqual(first.AsSpan(5, 16).ToArray(), second.AsSpan(5, 16).ToArray());
        Assert.NotEqual(first.AsSpan(21, 12).ToArray(), second.AsSpan(21, 12).ToArray());
        Assert.ThrowsAny<CryptographicException>(() => JsonSecure.Decrypt<Sample>("wrong", first));
    }

    [Fact]
    public void EncryptionRejectsUnknownVersionsTruncationAndLimits()
    {
        byte[] bytes = JsonSecure.Encrypt("password", 42);
        bytes[4] = 2;
        Assert.Throws<InvalidDataException>(() => JsonSecure.Decrypt<int>("password", bytes));
        Assert.Throws<InvalidDataException>(() => JsonSecure.Decrypt<int>("password", new byte[32]));
        Assert.Throws<InvalidDataException>(() => JsonSecure.Encrypt("password", 42, limits: new SerializationLimits(50)));
        Assert.Throws<ArgumentException>(() => JsonSecure.Encrypt("", 42));
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSecure.Encrypt(new string('x', 1025), 42));
    }

    [Fact]
    public void EncryptedFileFailuresDoNotBecomeDefaultValues()
    {
        using var temporary = new TemporaryDirectory();
        string path = temporary.File("secure");
        JsonSecure.Save(path, "pw", Data());
        Assert.Equal(42, JsonSecure.Load<Sample>(path, "pw").Count);
        Assert.ThrowsAny<CryptographicException>(() => JsonSecure.Load(path, "wrong", new Sample()));
        Assert.Equal(17, JsonSecure.Load(temporary.File("missing"), "pw", 17));
    }
}

public sealed class Sample
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public int[] Values { get; set; } = [];
}

public sealed class FieldSample
{
    public int Value;
}

public sealed class Marker
{
    public int Number { get; set; }
}

internal sealed class MarkerConverter : JsonConverter<Marker>
{
    public override Marker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new() { Number = int.Parse(reader.GetString()!["marker:".Length..], System.Globalization.CultureInfo.InvariantCulture) };
    public override void Write(Utf8JsonWriter writer, Marker value, JsonSerializerOptions options)
        => writer.WriteStringValue("marker:" + value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "benscr-serialization-test-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class PartialStream(byte[] data) : MemoryStream(data, writable: false)
{
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 3)]);
    public override void CopyTo(Stream destination, int bufferSize)
    {
        byte[] buffer = new byte[3];
        int count;
        while ((count = Read(buffer, 0, buffer.Length)) != 0) { destination.Write(buffer, 0, count); }
    }
}

internal sealed class BrokenStream : MemoryStream
{
    public override void CopyTo(Stream destination, int bufferSize) => throw new IOException("Deliberate read failure.");
}

internal sealed class BrokenWriteStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Deliberate write failure.");
    public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Deliberate write failure.");
}

internal sealed class AppendedValueFormatter : IMessagePackFormatter<int[]>
{
    public void Serialize(ref MessagePackWriter writer, int[] value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        foreach (int item in value) { writer.Write(item); }
        writer.Write(42);
    }

    public int[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => throw new NotSupportedException();
}
