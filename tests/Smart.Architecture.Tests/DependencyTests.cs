using System.Xml.Linq;
using Smart.Contracts;
using Smart.Data;
using Xunit;
namespace Smart.Architecture.Tests;

public sealed class DependencyTests
{
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Smart.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Solution root was not found.");
    }
    private static IEnumerable<string> Files(string directory, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(directory, pattern)) yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || name is "bin" or "obj" or "artifacts" or "tests") continue;
            foreach (var file in Files(child, pattern)) yield return file;
        }
    }
    [Fact] public void EveryBusinessProjectObeysDependencyDirection()
    {
        var projects = Files(Root(), "*.csproj").Where(x => x.EndsWith(".Domain.csproj", StringComparison.Ordinal) || x.EndsWith(".Application.csproj", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(projects);
        foreach (var file in projects)
        {
            var document = XDocument.Load(file);
            var domain = file.EndsWith(".Domain.csproj", StringComparison.Ordinal);
            Assert.Empty(document.Descendants("Reference"));
            foreach (var package in document.Descendants("PackageReference"))
                Assert.True(!domain && package.Attribute("Include")?.Value == "Smart.Contracts", file + " adds a business-layer package dependency.");
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var name = Path.GetFileName(reference.Attribute("Include")!.Value.Replace('\\', '/'));
                Assert.False(domain, file + " has a domain project dependency.");
                Assert.True(name == "Smart.Contracts.csproj" || name.EndsWith(".Domain.csproj", StringComparison.Ordinal), file + " -> " + name);
            }
        }
    }
    [Fact] public void EveryBusinessSourceAvoidsRawReadsAndHardware()
    {
        var forbidden = new[] { "Smart.Data", "Smart.Adapters", "Smart.Runtime", "Smart.Hosting", "RawRead", "ReadCompleteness", "RequireFresh",
            "UsedFallback", "DataAvailable", "GameApiReadContext", "Task.Run", "DllImport", "IInputDevice", "IActionExecutor" };
        foreach (var project in Files(Root(), "*.csproj").Where(x => x.EndsWith(".Domain.csproj", StringComparison.Ordinal) || x.EndsWith(".Application.csproj", StringComparison.Ordinal)))
            foreach (var file in Files(Path.GetDirectoryName(project)!, "*.cs"))
                foreach (var term in forbidden) Assert.DoesNotContain(term, File.ReadAllText(file), StringComparison.Ordinal);
    }
    [Fact] public void BusinessSnapshotApiExportsOnlyPublishedValues()
    {
        foreach (var method in typeof(ISnapshotReader).GetMethods())
        {
            Assert.Equal(typeof(ValueTask<>), method.ReturnType.GetGenericTypeDefinition());
            Assert.Equal(typeof(PublishedSnapshot<>), method.ReturnType.GenericTypeArguments[0].GetGenericTypeDefinition());
        }
        Assert.Equal(new[] { "Stable" }, Enum.GetNames<SnapshotReadPolicy>());
    }
    [Fact] public void TemplateContainsNoGameBusiness()
    {
        // Only the maintained template source must remain business-free. Generated consumers
        // intentionally add domain concepts; all dependency and raw-read checks above still run.
        if (!File.Exists(Path.Combine(Root(), ".template.config", "template.json"))) return;
        var forbidden = new[] { "ActorVitals", "RecoveryModule", "Health", "Combat", "Aion", "Loot", "LicenseKey" };
        foreach (var file in Files(Path.Combine(Root(), "template"), "*.cs"))
            foreach (var term in forbidden) Assert.DoesNotContain(term, File.ReadAllText(file), StringComparison.Ordinal);
    }
}
