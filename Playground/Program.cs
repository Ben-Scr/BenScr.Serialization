using System;
using System.IO;
using BenScr.Serialization;

string directory = Path.Combine(Path.GetTempPath(), "benscr-serialization-example-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var source = new Player { Name = "Ada", Score = 42 };
    string jsonPath = Path.Combine(directory, "player.json");
    Json.Save(jsonPath, source);
    FileConverter.ConvertTo<Player>(jsonPath, Format.Json, Format.Xml);
    Player xml = Xml.Load<Player>(Path.Combine(directory, "player.xml"));
    Check(xml.Name == "Ada" && xml.Score == 42, "typed JSON to XML conversion");
    byte[] binary = Binary.Serialize(source);
    Check(Binary.Deserialize<Player>(binary)!.Score == 42, "MessagePack round trip");
    byte[] compressed = Json.SerializeCompressed(source);
    Check(Json.DeserializeCompressed<Player>(compressed)!.Name == "Ada", "gzip JSON round trip");
    byte[] encrypted = JsonSecure.Encrypt("example-only-passphrase", source);
    Check(JsonSecure.Decrypt<Player>("example-only-passphrase", encrypted)!.Score == 42, "authenticated encryption");
    using var stream = new MemoryStream();
    Json.Write(stream, source);
    stream.Position = 0;
    Check(Json.Read<Player>(stream)!.Score == 42 && stream.CanRead, "caller-owned stream");
    Console.WriteLine("All package consumer scenarios passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition) { throw new InvalidOperationException(description); }
    Console.WriteLine("Passed: " + description);
}

public sealed class Player
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
}
