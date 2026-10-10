using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Security.Cryptography;
using System.Text.Json;

namespace Studio.Raple;

public record ExportVerification(bool Accepted, string[] Errors, int SourceFiles);
public static class SourceVerification
{
    // C# is this server's actual behavior parser. Compile the complete original WorldServer sources,
    // replacing just the imported file, against .NET 8 reference assemblies and the server's
    // restored compile assets. Studio's own dependencies can select newer framework variants.
    public static ExportVerification Check(string serverRoot, string originalFile, string exportedSource)
    {
        var dir = Path.Combine(serverRoot, "TKR.WorldServer");
        var paths = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin") && !p.Replace('\\', '/').Contains("/behaviors/new/modifiers/"))
            .ToArray();
        var original = Path.GetFullPath(originalFile);
        if (!paths.Any(p => Path.GetFullPath(p).Equals(original, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("The imported file is not in this server's behavior source tree.");
        var trees = paths.Select(p => CSharpSyntaxTree.ParseText(Path.GetFullPath(p).Equals(original, StringComparison.OrdinalIgnoreCase) ? exportedSource : File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.CSharp12), path: p));
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var packRoots = new[] { Path.Combine(packages, "microsoft.netcore.app.ref"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet/packs/Microsoft.NETCore.App.Ref") };
        var referencePack = packRoots.Where(Directory.Exists).SelectMany(p => Directory.EnumerateDirectories(p))
            .Where(p => Path.GetFileName(p).StartsWith("8.")).OrderByDescending(p => Version.Parse(Path.GetFileName(p))).FirstOrDefault()
            ?? throw new InvalidOperationException("The original .NET 8 reference pack is missing. Restore/build the private server project before exporting.");
        var referencePaths = Directory.EnumerateFiles(Path.Combine(referencePack, "ref/net8.0"), "*.dll")
            .Concat(ServerCompileReferences(dir))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
        var compilation = CSharpCompilation.Create("TKR.WorldServer", trees,
            referencePaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        var errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        return new(errors.Length == 0, errors, paths.Length);
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
