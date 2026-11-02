using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length == 2)
{
    AssemblyLoadContext.Default.Resolving += (_, name) => { string candidate = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, name.Name + ".dll"); return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null; };
    Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
    string api = string.Join("\n", assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal).SelectMany(type =>
        new[] { $"{type.FullName} : {type.BaseType}" }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => "  " + member.MemberType + " " + member).Order(StringComparer.Ordinal)))) + "\n";
    if (File.Exists(args[1]))
    {
        Check(File.ReadAllText(args[1]).Replace("\r\n", "\n") == api, "Public API differs: " + args[1]);
        Console.WriteLine("Public API baseline matches.");
    }
    else
    {
        File.WriteAllText(args[1], api);
        Console.WriteLine("API inventory written to " + args[1]);
    }
    return;
}

if (args.Length != 5 || args[0] != "package")
{
    throw new ArgumentException("Usage: PackageAudit <assembly> <API file> OR package <nupkg> <snupkg> <commit> <root>");
}
string packagePath = args[1];
string symbolsPath = args[2];
string commit = args[3];
string root = Path.GetFullPath(args[4]);
using var package = ZipFile.OpenRead(packagePath);
using var symbols = ZipFile.OpenRead(symbolsPath);
XDocument specification = XDocument.Load(new MemoryStream(Read(package, "BenScr.Serialization.nuspec")));
XElement metadata = specification.Root!.Elements().Single();
string Value(string name) => metadata.Elements().Single(element => element.Name.LocalName == name).Value;
Check(Value("id") == "BenScr.Serialization" && Value("version") == "1.0.0", "Package identity mismatch.");
XElement repository = metadata.Elements().Single(element => element.Name.LocalName == "repository");
Check((string?)repository.Attribute("commit") == commit, "Repository commit mismatch.");
Check((string?)repository.Attribute("url") == "https://github.com/Ben-Scr/BenScr.Serialization", "Repository URL mismatch.");
XElement[] dependencies = metadata.Descendants().Where(element => element.Name.LocalName == "dependency").ToArray();
Check(dependencies.Length == 2 && dependencies.All(item => (string?)item.Attribute("id") == "MessagePack" && ((string?)item.Attribute("version")) == "[3.1.12]"), "Unexpected runtime dependencies.");
Check(Value("icon") == "icon.png" && Value("readme") == "README.md" && Value("license") == "LICENSE.txt", "Package metadata paths mismatch.");
foreach (string name in new[] { "README.md", "LICENSE.txt" })
{
    Check(Read(package, name).SequenceEqual(File.ReadAllBytes(Path.Combine(root, name))), "Packaged document mismatch: " + name);
}
byte[] icon = Read(package, "icon.png");
Check(icon.Length < 1_000_000 && icon.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG.");
Check(Convert.ToHexStringLower(SHA256.HashData(icon)) == "a5dcabceb766e13c1840a95130ee98a458e12e3713fd9b07f3ceae75a1f5b993", "Icon hash mismatch.");
Check(icon.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "branding", "BenScr_Icon_Master.png"))), "Icon differs from master.");
File.WriteAllBytes(Path.Combine(root, "artifacts", "icon-from-package.png"), icon);

var sourceUrls = new HashSet<string>(StringComparer.Ordinal);
foreach (string framework in new[] { "net9.0", "net10.0" })
{
    string stem = $"lib/{framework}/BenScr.Serialization";
    byte[] assemblyBytes = Read(package, stem + ".dll");
    byte[] pdbBytes = Read(symbols, stem + ".pdb");
    Check(Read(package, stem + ".xml").Length > 1000, "Missing XML documentation.");
    using var pdbStream = new MemoryStream(pdbBytes);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
    MetadataReader reader = provider.GetMetadataReader();
    var pdbId = new BlobContentId(reader.DebugMetadataHeader!.Id);
    using var pe = new PEReader(new MemoryStream(assemblyBytes));
    DebugDirectoryEntry codeView = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
    CodeViewDebugDirectoryData codeViewData = pe.ReadCodeViewDebugDirectoryData(codeView);
    Check(codeViewData.Guid == pdbId.Guid && codeView.Stamp == pdbId.Stamp, "PDB does not match assembly.");
    var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    CustomDebugInformation sourceLink = reader.CustomDebugInformation
        .Select(reader.GetCustomDebugInformation).Single(info => reader.GetGuid(info.Kind) == sourceLinkKind);
    using JsonDocument json = JsonDocument.Parse(reader.GetBlobBytes(sourceLink.Value));
    JsonProperty mapping = json.RootElement.GetProperty("documents").EnumerateObject().Single();
    string urlPattern = mapping.Value.GetString()!;
    Check(urlPattern == $"https://raw.githubusercontent.com/Ben-Scr/BenScr.Serialization/{commit}/*", "Source Link URL mismatch.");
    string documentPrefix = mapping.Name.TrimEnd('*');
    foreach (DocumentHandle handle in reader.Documents)
    {
        Document document = reader.GetDocument(handle);
        string name = reader.GetString(document.Name);
        Check(!name.Contains("C:\\Users\\", StringComparison.OrdinalIgnoreCase), "Developer path leaked.");
        if (!name.StartsWith(documentPrefix, StringComparison.Ordinal))
        {
            continue;
        }
        string relative = name[documentPrefix.Length..];
        if (!relative.StartsWith("Serializer/Src/", StringComparison.Ordinal))
        {
            continue;
        }
        byte[] sourceBytes = File.ReadAllBytes(Path.Combine(root, relative));
        Check(reader.GetBlobBytes(document.Hash).SequenceEqual(SHA256.HashData(sourceBytes)), "Source checksum mismatch: " + relative);
        sourceUrls.Add(urlPattern.Replace("*", relative, StringComparison.Ordinal));
    }
}
Check(sourceUrls.Count >= 7, "Missing source mappings.");
string auditPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(packagePath))!, "package-audit.json");
File.WriteAllText(auditPath, JsonSerializer.Serialize(new
{
    package_id = "BenScr.Serialization",
    version = "1.0.0",
    repository_commit = commit,
    frameworks = new[] { "net9.0", "net10.0" },
    dependencies = new[] { "MessagePack 3.1.12" },
    icon_sha256 = Convert.ToHexStringLower(SHA256.HashData(icon)),
    package_sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packagePath))),
    symbols_sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(symbolsPath))),
    source_urls = sourceUrls.Order(StringComparer.Ordinal).ToArray(),
    nuspec_checked = true,
    pdb_assembly_identity_checked = true,
    source_checksums_checked = true
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Package, documentation, license, icon, PDB identities and Source Link checks passed.");

static byte[] Read(ZipArchive archive, string path)
{
    using Stream stream = (archive.GetEntry(path) ?? throw new InvalidDataException("Missing entry: " + path)).Open();
    using var output = new MemoryStream();
    stream.CopyTo(output);
    return output.ToArray();
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}

