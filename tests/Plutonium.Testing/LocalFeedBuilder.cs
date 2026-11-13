using System.IO.Compression;
using System.Xml.Linq;

namespace Plutonium.Testing;

/// <summary>Describes the packages produced by one local-feed pack.</summary>
/// <param name="Directory">The feed directory.</param>
/// <param name="PackagePaths">The <c>.nupkg</c> files in the feed.</param>
/// <param name="Failure">A diagnostic when packing failed, or null on success.</param>
public sealed record LocalFeed(string Directory, IReadOnlyList<string> PackagePaths, string? Failure = null)
{
    /// <summary>Gets a value indicating whether packing succeeded.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>Describes the result of staging a package dependency closure into a feed.</summary>
/// <param name="StagedPackages">The package IDs and versions copied into the feed.</param>
/// <param name="Failure">A diagnostic when staging failed, or null on success.</param>
public sealed record FeedStagingResult(IReadOnlyList<string> StagedPackages, string? Failure = null)
{
    /// <summary>Gets a value indicating whether every dependency was staged.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Packs the repository's package project into a disposable local feed and stages the
/// dependency closure needed for an offline consumer restore.
/// </summary>
public sealed class LocalFeedBuilder
{
    private readonly RepositoryLayout _layout;
    private readonly DotNetCli _dotnet;

    /// <summary>Initializes a new instance of the <see cref="LocalFeedBuilder"/> class.</summary>
    /// <param name="layout">The repository layout.</param>
    /// <param name="dotnet">The resolved .NET CLI.</param>
    public LocalFeedBuilder(RepositoryLayout layout, DotNetCli dotnet)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dotnet);
        _layout = layout;
        _dotnet = dotnet;
    }

    /// <summary>Packs the solution, which must produce one distributable <c>PlutoniumBuild</c> package.</summary>
    /// <param name="workspace">The workspace that owns the feed directory.</param>
    /// <param name="version">An optional test-only version override.</param>
    /// <param name="cancellationToken">A cancellation token for the pack wait.</param>
    /// <returns>The feed description.</returns>
    public async Task<LocalFeed> PackAsync(
        TempWorkspace workspace,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var feedDirectory = workspace.PathFor("feed");
        Directory.CreateDirectory(feedDirectory);

        var arguments = new List<string>
        {
            "pack",
            _layout.SolutionPath,
            "-c",
            "Debug",
            "-o",
            feedDirectory,
            "--no-restore",
            "-nologo",
            "-v:minimal",
        };

        if (!string.IsNullOrWhiteSpace(version))
        {
            arguments.Add("-p:Version=" + version);
        }

        var result = await _dotnet.RunAsync(
            arguments,
            _layout.Root,
            timeout: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var packagePaths = Directory.Exists(feedDirectory)
            ? Directory.GetFiles(feedDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        var failure = result.Succeeded
            ? null
            : "dotnet pack did not succeed." + Environment.NewLine + result.Describe();

        return new LocalFeed(feedDirectory, packagePaths, failure);
    }

    /// <summary>Stages one package and its transitive dependencies from the global package cache.</summary>
    /// <param name="feed">The feed to copy packages into.</param>
    /// <param name="packageId">The root package ID.</param>
    /// <param name="version">The root package version.</param>
    /// <returns>The staging result.</returns>
    public FeedStagingResult StagePackageClosure(LocalFeed feed, string packageId, string version)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var packageRoot = ResolveGlobalPackagesFolder();
        var staged = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failure = Stage(packageId, version, packageRoot, feed.Directory, staged, visited);
        return new FeedStagingResult(staged, failure);
    }

    /// <summary>Resolves the NuGet global packages folder used for staging.</summary>
    /// <returns>The absolute folder path.</returns>
    public static string ResolveGlobalPackagesFolder()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nuget", "packages");
    }

    private static string? Stage(
        string packageId,
        string version,
        string packageRoot,
        string feedDirectory,
        List<string> staged,
        HashSet<string> visited)
    {
        var normalizedVersion = NormalizeVersion(version);
        var key = packageId + "/" + normalizedVersion;
        if (!visited.Add(key))
        {
            return null;
        }

        var packageDirectory = Path.Combine(packageRoot, packageId.ToLowerInvariant(), normalizedVersion.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory))
        {
            return "Package not found in the global cache: " + packageId + " " + normalizedVersion +
                   ". Expected directory: " + packageDirectory +
                   ". Run a restore that includes this package before the offline test.";
        }

        var packagePath = Directory.GetFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
        if (packagePath is null)
        {
            return "No .nupkg found for " + packageId + " " + normalizedVersion +
                   " in " + packageDirectory + ".";
        }

        var destination = Path.Combine(feedDirectory, Path.GetFileName(packagePath));
        if (!File.Exists(destination))
        {
            File.Copy(packagePath, destination);
        }

        staged.Add(packageId + " " + normalizedVersion);

        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspecEntry is null)
        {
            return "No .nuspec entry found in " + packagePath + ".";
        }

        using var stream = nuspecEntry.Open();
        var document = XDocument.Load(stream);
        var dependenciesElement = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "dependencies");
        var dependencyElements = SelectDependencies(dependenciesElement);
        foreach (var dependency in dependencyElements)
        {
            var dependencyId = dependency.Attribute("id")?.Value;
            var dependencyVersion = dependency.Attribute("version")?.Value;
            if (string.IsNullOrWhiteSpace(dependencyId) || string.IsNullOrWhiteSpace(dependencyVersion))
            {
                continue;
            }

            var failure = Stage(dependencyId, dependencyVersion, packageRoot, feedDirectory, staged, visited);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private static IEnumerable<XElement> SelectDependencies(XElement? dependenciesElement)
    {
        if (dependenciesElement is null)
        {
            return [];
        }

        var groups = dependenciesElement.Elements()
            .Where(element => element.Name.LocalName == "group")
            .Select(element => (
                Element: element,
                Rank: TargetFrameworkRank(element.Attribute("targetFramework")?.Value)))
            .Where(pair => pair.Rank >= 0)
            .OrderByDescending(pair => pair.Rank)
            .ToArray();

        var selected = groups.Length > 0
            ? groups[0].Element
            : dependenciesElement;

        return selected.Elements().Where(element => element.Name.LocalName == "dependency");
    }

    private static int TargetFrameworkRank(string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            return 0;
        }

        var value = targetFramework.Trim();
        if (value.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
            value.Length > 3 &&
            char.IsDigit(value[3]))
        {
            return int.TryParse(value[3..].Split('.')[0], out var major) && major >= 5
                ? 10_000 + major
                : -1;
        }

        if (value.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVersionRank(value["netcoreapp".Length..], 5_000);
        }

        if (value.StartsWith(".NETCoreApp", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVersionRank(value[".NETCoreApp".Length..], 5_000);
        }

        if (value.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVersionRank(value[".NETStandard".Length..], 3_000);
        }

        if (value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVersionRank(value["netstandard".Length..], 3_000);
        }

        return -1;
    }

    private static int ParseVersionRank(string value, int baseRank)
    {
        var parts = value.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major))
        {
            return -1;
        }

        var minor = parts.Length > 1 && int.TryParse(parts[1], out var parsedMinor)
            ? parsedMinor
            : 0;
        return baseRank + (major * 10) + minor;
    }

    private static string NormalizeVersion(string version)
    {
        var value = version.Trim();
        if (value.StartsWith('[') || value.StartsWith('('))
        {
            value = value[1..];
        }

        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            value = value[..comma];
        }

        value = value.TrimEnd(']', ')', ' ');
        var metadata = value.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            value = value[..metadata];
        }

        return value;
    }
}
