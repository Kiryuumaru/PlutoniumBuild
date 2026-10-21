using System.IO.Compression;
using System.Xml.Linq;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Packaging.Tests;

/// <summary>
/// Pins the public package shape: one distributable, net10.0 assemblies, XML
/// documentation, and a single external dependency.
/// </summary>
[Collection("packaging")]
public sealed class PackageArchiveTests
{
    private readonly PackagingFixture _fixture;

    /// <summary>Initializes the test class with the shared pack.</summary>
    /// <param name="fixture">The shared packaging fixture.</param>
    public PackageArchiveTests(PackagingFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Packing the solution yields exactly the distributable package.</summary>
    [Fact]
    public void SolutionPack_ProducesOneDistributablePackage()
    {
        Assert.True(_fixture.Feed.Succeeded, _fixture.Feed.Failure);

        var packageNames = _fixture.Feed.PackagePaths
            .Where(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();

        Assert.True(
            packageNames.Length == 1,
            "Expected exactly one distributable package, found: " + string.Join(", ", packageNames));
        Assert.Equal("PlutoniumBuild." + _fixture.Version + ".nupkg", packageNames[0], ignoreCase: true);
    }

    /// <summary>The manifest declares MIT, a readme, and only ApplicationBuilderHelpers.</summary>
    [Fact]
    public void Manifest_DeclaresLicenseReadmeAndSingleDependency()
    {
        using var archive = OpenPackage();
        var metadata = ReadMetadata(archive);

        Assert.Equal("PlutoniumBuild", Value(metadata, "id"));
        Assert.Equal(_fixture.Version, Value(metadata, "version"));
        Assert.False(string.IsNullOrWhiteSpace(Value(metadata, "authors")));
        Assert.False(string.IsNullOrWhiteSpace(Value(metadata, "description")));

        var license = metadata.Descendants().FirstOrDefault(element => element.Name.LocalName == "license");
        Assert.NotNull(license);
        Assert.Equal("expression", license.Attribute("type")?.Value);
        Assert.Equal("MIT", license.Value.Trim());

        var readme = metadata.Descendants().FirstOrDefault(element => element.Name.LocalName == "readme");
        Assert.NotNull(readme);
        Assert.False(string.IsNullOrWhiteSpace(readme.Value));
        Assert.Contains(
            archive.Entries,
            entry => string.Equals(entry.FullName, readme.Value.Trim(), StringComparison.OrdinalIgnoreCase));

        var dependencies = metadata.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => (
                Id: element.Attribute("id")?.Value,
                Version: element.Attribute("version")?.Value))
            .Distinct()
            .ToArray();

        Assert.True(dependencies.Length == 1, "Expected one dependency entry, found: " +
            string.Join(", ", dependencies.Select(pair => pair.Id + " " + pair.Version)));
        Assert.Equal("ApplicationBuilderHelpers", dependencies[0].Id);
        Assert.Equal("4.1.160", dependencies[0].Version);
        Assert.DoesNotContain(
            dependencies,
            pair => pair.Id?.StartsWith("Plutonium", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>The archive carries only the net10.0 library and its documentation.</summary>
    [Fact]
    public void Archive_CarriesOnlyNet10Assemblies()
    {
        using var archive = OpenPackage();
        var libraryEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var frameworks = libraryEntries
            .Select(entry => entry.FullName.Split('/'))
            .Where(parts => parts.Length >= 3)
            .Select(parts => parts[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(["net10.0"], frameworks);

        var files = libraryEntries
            .Select(entry => entry.FullName.Split('/')[^1])
            .ToArray();

        var required = new[]
        {
            "Plutonium.Core.dll",
            "Plutonium.Core.xml",
            "Plutonium.Core.pdb",
            "Plutonium.Helpers.dll",
            "Plutonium.Helpers.xml",
            "Plutonium.Helpers.pdb",
            "Plutonium.Shell.dll",
            "Plutonium.Shell.xml",
            "Plutonium.Shell.pdb",
        };

        Assert.All(
            required,
            name => Assert.Contains(name, files, StringComparer.OrdinalIgnoreCase));

        var assemblies = files
            .Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            ["Plutonium.Core.dll", "Plutonium.Helpers.dll", "Plutonium.Shell.dll"],
            assemblies,
            StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            files,
            name => name.StartsWith("PlutoniumBuild.", StringComparison.OrdinalIgnoreCase));

        Assert.All(
            libraryEntries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
            entry => Assert.True(entry.Length > 0, "Documentation entry is empty: " + entry.FullName));

        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    }

    private ZipArchive OpenPackage()
    {
        Assert.True(_fixture.Feed.Succeeded, _fixture.Feed.Failure);
        var packagePath = _fixture.Feed.PackagePaths.FirstOrDefault(
            path => string.Equals(
                Path.GetFileName(path),
                "PlutoniumBuild." + _fixture.Version + ".nupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.True(
            packagePath is not null,
            "Package not found in feed. Feed contents: " +
            string.Join(", ", _fixture.Feed.PackagePaths.Select(Path.GetFileName)));
        return ZipFile.OpenRead(packagePath!);
    }

    private static XElement ReadMetadata(ZipArchive archive)
    {
        var nuspec = archive.Entries.FirstOrDefault(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(nuspec);
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var metadata = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");
        Assert.NotNull(metadata);
        return metadata!;
    }

    private static string Value(XElement metadata, string name) =>
        metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim()
        ?? string.Empty;
}
