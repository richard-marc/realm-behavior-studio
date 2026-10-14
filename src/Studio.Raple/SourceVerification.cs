using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Studio.Core;

namespace Studio.Raple;

public record ExportVerification(bool Accepted, string[] Errors, int SourceFiles);
public record NativeSourceStamp(string SourceHash, string EditedSourceHash, string ReferenceHash, string EmbeddedResourceHash, int SourceFiles,bool StateOriginInstrumentation=false,bool CallbackInstrumentation=false,bool PacketInstrumentation=false,bool SharedEventInstrumentation=false,bool TransitionInstrumentation=false);
public record NativeCompilation(ExportVerification Verification, byte[] Assembly, byte[] Symbols, NativeSourceStamp Stamp)
{
    internal CSharpCompilation? Compiler { get; init; }
}
public static class SourceVerification
{
    internal static string[] SourcePaths(string serverRoot) => Directory.EnumerateFiles(Path.Combine(serverRoot, "TKR.WorldServer"), "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin") && !p.Replace('\\', '/').Contains("/behaviors/new/modifiers/"))
        .OrderBy(p => p, StringComparer.Ordinal).ToArray();
    internal static CSharpCompilation SemanticCompilation(string serverRoot, string[] paths, string[] references)
        => CSharpCompilation.Create("TKR.WorldServer", paths.Select(p => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(p), Encoding.UTF8), new CSharpParseOptions(LanguageVersion.CSharp12), p)),
            references.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable, deterministic: true));
    internal static string[] ReferencePaths(string dir)
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var packRoots = new[] { Path.Combine(packages, "microsoft.netcore.app.ref"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet/packs/Microsoft.NETCore.App.Ref") };
        var referencePack = packRoots.Where(Directory.Exists).SelectMany(p => Directory.EnumerateDirectories(p))
            .Where(p => Path.GetFileName(p).StartsWith("8.")).OrderByDescending(p => Version.Parse(Path.GetFileName(p))).FirstOrDefault()
            ?? throw new InvalidOperationException("The original .NET 8 reference pack is missing. Restore/build the private server project before exporting.");
        return Directory.EnumerateFiles(Path.Combine(referencePack, "ref/net8.0"), "*.dll").Concat(ServerCompileReferences(dir))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
    // C# is this server's actual behavior parser. Compile the complete original WorldServer sources,
    // replacing the project's source documents, against .NET 8 reference assemblies and the server's
    // restored compile assets. Studio's own dependencies can select newer framework variants.
    public static ExportVerification Check(string serverRoot, string originalFile, string exportedSource)
        => Compile(serverRoot, originalFile, exportedSource).Verification;
    public static string EditedSourceHash(StudioProject project)
    {
        project.ValidateSourceSet();
        if(project.AdditionalSources.Count==0)return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.Source)));
        return Hash(project.AdditionalSources.Select(s=>(s.SourceFile,s.Source)).Prepend((project.SourceFile,project.Source))
            .OrderBy(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase).Select(s=>(Path.GetFullPath(s.SourceFile),Encoding.UTF8.GetBytes(s.Source))));
    }
    public static NativeCompilation Compile(string serverRoot, string originalFile, string exportedSource, CancellationToken cancellation = default)
        =>Compile(serverRoot,new StudioProject{SourceFile=originalFile,Source=exportedSource},cancellation);
    public static IReadOnlyDictionary<string,string> SourceSet(string serverRoot,StudioProject project)
    {
        project.ValidateSourceSet();var paths=SourcePaths(serverRoot).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources=project.AdditionalSources.Select(s=>(s.SourceFile,s.Source)).Prepend((project.SourceFile,project.Source)).ToDictionary(s=>Path.GetFullPath(s.SourceFile),s=>s.Source,StringComparer.OrdinalIgnoreCase);
        if(sources.Keys.Any(p=>!paths.Contains(p)))throw new ArgumentException("Every project source document must identify an original file in this server's source tree.");return sources;
    }
    public static NativeCompilation Compile(string serverRoot,StudioProject project,CancellationToken cancellation=default,bool captureStateOrigins=false)
    {
        var dir = Path.Combine(serverRoot, "TKR.WorldServer");
        var paths = SourcePaths(serverRoot);
        var edits=SourceSet(serverRoot,project);
        // The SDK normally generates this attribute under obj. Preserve the actual
        // referenced engine identity so the host can bind to the emitted replacement.
        var identity = $"[assembly: global::System.Reflection.AssemblyVersion(\"{typeof(TKR.WorldServer.logic.State).Assembly.GetName().Version}\")]";
        var sources = paths.Select(p => (Path: p, Text: edits.TryGetValue(Path.GetFullPath(p),out var edited)?edited:File.ReadAllText(p)))
            .Append((Path: Path.Combine(dir, "_Studio.NativeAssemblyIdentity.g.cs"), Text: identity)).ToArray();
        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(SourceText.From(p.Text, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.CSharp12), path: p.Path));
        var referencePaths = ReferencePaths(dir);
        // Embedded JavaScript is part of this native runtime (notably Oryx4). Retain the
        // original project's declared logical names rather than emitting a source-only DLL.
        var resources = XDocument.Load(Path.Combine(dir, "TKR.WorldServer.csproj")).Descendants("EmbeddedResource")
            .Where(e => e.Attribute("Include") != null).Select(e => (Name: (string?)e.Attribute("LogicalName") ?? throw new InvalidOperationException("An embedded resource needs its original logical name."),
                Data: File.ReadAllBytes(Path.Combine(dir, (string)e.Attribute("Include")!)))).OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
        var stamp = new NativeSourceStamp(Hash(sources.Select(p => (Path.GetRelativePath(dir, p.Path), Encoding.UTF8.GetBytes(p.Text)))),
            Hash(edits.OrderBy(e=>e.Key,StringComparer.OrdinalIgnoreCase).Select(e=>(Path.GetRelativePath(dir,e.Key),Encoding.UTF8.GetBytes(e.Value)))),
            Hash(referencePaths.Select(p => (Path.GetFileName(p), File.ReadAllBytes(p)))), Hash(resources.Select(r => (r.Name, r.Data))), paths.Length);
        var compilation = CSharpCompilation.Create("TKR.WorldServer", trees,
            referencePaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable, deterministic: true));
        using var output = new MemoryStream(); using var symbols = new MemoryStream();
        var executable=captureStateOrigins&&!compilation.GetDiagnostics(cancellation).Any(d=>d.Severity==DiagnosticSeverity.Error)
            ?NativeStateOrigins.Instrument(compilation,project):compilation;
        var emitted = executable.Emit(output, symbols, manifestResources: resources.Select(r => new ResourceDescription(r.Name, () => new MemoryStream(r.Data, false), true)),
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "TKR.WorldServer.pdb"), cancellationToken: cancellation);
        var errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        return new(new(errors.Length == 0, errors, paths.Length), emitted.Success ? output.ToArray() : [], emitted.Success ? symbols.ToArray() : [], stamp with{StateOriginInstrumentation=captureStateOrigins,CallbackInstrumentation=captureStateOrigins,PacketInstrumentation=captureStateOrigins,SharedEventInstrumentation=captureStateOrigins,TransitionInstrumentation=captureStateOrigins}) { Compiler = compilation };
    }
    public static string Hash(IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, data) in entries) { var key = Encoding.UTF8.GetBytes(name.Replace('\\', '/')); hash.AppendData(BitConverter.GetBytes(key.Length)); hash.AppendData(key); hash.AppendData(BitConverter.GetBytes(data.Length)); hash.AppendData(data); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static IEnumerable<string> ServerCompileReferences(string projectDirectory)
    {
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath)) throw new InvalidOperationException("Restore/build the original server before exporting; its compile assets are missing.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = assets.RootElement;
        var target = root.GetProperty("targets").EnumerateObject().First(p => p.Name == "net8.0" || p.Name == ".NETCoreApp,Version=v8.0").Value;
        var packageRoots = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToArray();
        foreach (var library in target.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("compile", out var compile)) continue;
            foreach (var asset in compile.EnumerateObject().Where(p => p.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                string? path;
                if (library.Value.GetProperty("type").GetString() == "project")
                    path = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(asset.Name));
                else
                {
                    var packagePath = root.GetProperty("libraries").GetProperty(library.Name).GetProperty("path").GetString()!;
                    path = packageRoots.Select(p => Path.Combine(p, packagePath, asset.Name)).FirstOrDefault(File.Exists);
                }
                if (path is null || !File.Exists(path)) throw new InvalidOperationException($"Original server compile dependency is missing: {library.Name}/{asset.Name}. Restore/build the original server.");
                yield return path;
            }
        }
    }
    public static string Fingerprint(string root, string pattern = "*.xml")
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
