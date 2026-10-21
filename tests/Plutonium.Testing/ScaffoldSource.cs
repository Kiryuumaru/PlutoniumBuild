using System.Text.Json.Nodes;

namespace Plutonium.Testing;

/// <summary>
/// A disposable copy of the repository's scaffold payload, committed into a
/// local git repository so init and update tests can pin a real commit SHA.
/// </summary>
public sealed class ScaffoldSource
{
    private ScaffoldSource(GitRepository repository, string headSha)
    {
        Repository = repository;
        HeadSha = headSha;
    }

    /// <summary>Gets the source git repository.</summary>
    public GitRepository Repository { get; }

    /// <summary>Gets the commit SHA tests should pin.</summary>
    public string HeadSha { get; private set; }

    /// <summary>Gets the source repository path passed to init or update.</summary>
    public string SourcePath => Repository.RootPath;

    /// <summary>Gets the path of the source init script.</summary>
    public string InitScriptPath => Path.Combine(Repository.RootPath, "init.sh");

    /// <summary>Gets the source scaffold build directory.</summary>
    public string ScaffoldBuildPath => Path.Combine(Repository.RootPath, "scaffold", "build");

    /// <summary>
    /// Creates a source repository containing <c>scaffold/build</c>,
    /// <c>scaffold/root</c>, and the init scripts.
    /// </summary>
    /// <param name="workspace">The workspace that owns the repository.</param>
    /// <param name="layout">The repository layout that supplies the scaffold payload.</param>
    /// <param name="sdkVersionOverride">
    /// An optional SDK version written into <c>build/global.json</c> and the lock,
    /// used to run the scaffold against the SDK installed on this machine.
    /// </param>
    /// <returns>The committed source.</returns>
    /// <exception cref="InvalidOperationException">The scaffold payload is absent.</exception>
    public static Task<ScaffoldSource> CreateAsync(
        TempWorkspace workspace,
        RepositoryLayout layout,
        string? sdkVersionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(layout);

        if (!Directory.Exists(layout.ScaffoldBuildDirectory))
        {
            throw new InvalidOperationException(
                "Scaffold payload not found at '" + layout.ScaffoldBuildDirectory +
                "'. The scaffold/ tree has not landed yet.");
        }

        if (!Directory.Exists(layout.ScaffoldRootDirectory))
        {
            throw new InvalidOperationException(
                "Scaffold root payload not found at '" + layout.ScaffoldRootDirectory + "'.");
        }

        var repository = GitRepository.Create(workspace, "scaffold-source");
        repository.CopyDirectory(layout.ScaffoldBuildDirectory, "scaffold/build");
        repository.CopyDirectory(layout.ScaffoldRootDirectory, "scaffold/root");

        var sourceProps = Path.Combine(layout.Root, "src", "Directory.Build.props");
        if (File.Exists(sourceProps))
        {
            var targetProps = Path.Combine(repository.RootPath, "src", "Directory.Build.props");
            Directory.CreateDirectory(Path.GetDirectoryName(targetProps)!);
            File.Copy(sourceProps, targetProps, overwrite: true);
        }

        foreach (var script in new[] { "init.sh", "init.ps1" })
        {
            var source = Path.Combine(layout.Root, script);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(repository.RootPath, script));
            }
        }

        if (!string.IsNullOrWhiteSpace(sdkVersionOverride))
        {
            PatchSdkVersion(repository, sdkVersionOverride);
        }

        var sha = repository.Commit("scaffold source");
        return Task.FromResult(new ScaffoldSource(repository, sha));
    }

    /// <summary>
    /// Modifies one scaffold file, refreshes its lock hash when the file is
    /// managed, and commits the change so update tests can target a new SHA.
    /// </summary>
    /// <param name="relativePath">A path relative to the source repository root.</param>
    /// <param name="transform">The content transform.</param>
    /// <returns>The new commit SHA.</returns>
    public string CommitChange(string relativePath, Func<string, string> transform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(transform);

        var fullPath = Path.Combine(Repository.RootPath, relativePath);
        var content = File.ReadAllText(fullPath);
        File.WriteAllText(fullPath, transform(content));

        var lockPath = Path.Combine(ScaffoldBuildPath, "plutonium.lock");
        var lockRelativePath = ToLockPath(relativePath);
        if (File.Exists(lockPath) && lockRelativePath is not null)
        {
            var lockFile = PlutoniumLockFile.Load(lockPath);
            lockFile.ReplaceHash(lockRelativePath, Hashing.Sha256File(fullPath));
            lockFile.Save();
        }

        HeadSha = Repository.Commit("scaffold change: " + relativePath);
        return HeadSha;
    }

    /// <summary>Reads a file from the source repository.</summary>
    /// <param name="relativePath">A path relative to the source repository root.</param>
    /// <returns>The file content.</returns>
    public string ReadFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return File.ReadAllText(Path.Combine(Repository.RootPath, relativePath));
    }

    /// <summary>
    /// Publishes a new PlutoniumBuild package version in the source repository
    /// so update tests can prove the consumer manifest is bumped.
    /// </summary>
    /// <param name="version">The new package version.</param>
    /// <returns>The new commit SHA.</returns>
    public string CommitPackageVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var propsPath = Path.Combine(Repository.RootPath, "src", "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var document = System.Xml.Linq.XDocument.Load(propsPath);
            var versionElement = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Version");
            if (versionElement is not null)
            {
                versionElement.Value = version;
                document.Save(propsPath);
            }
        }

        var projectPath = Path.Combine(ScaffoldBuildPath, "build.csproj");
        if (File.Exists(projectPath))
        {
            var document = System.Xml.Linq.XDocument.Load(projectPath);
            var reference = document.Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "PackageReference" &&
                    string.Equals(
                        element.Attribute("Include")?.Value,
                        "PlutoniumBuild",
                        StringComparison.OrdinalIgnoreCase));
            if (reference is not null)
            {
                reference.SetAttributeValue("Version", version);
                document.Save(projectPath);
            }
        }

        var lockPath = Path.Combine(ScaffoldBuildPath, "plutonium.lock");
        if (File.Exists(lockPath))
        {
            var lockFile = PlutoniumLockFile.Load(lockPath);
            lockFile.SetValue("package_version", version);
            if (File.Exists(projectPath))
            {
                lockFile.ReplaceHash("build/build.csproj", Hashing.Sha256File(projectPath));
            }

            lockFile.Save();
        }

        HeadSha = Repository.Commit("package version " + version);
        return HeadSha;
    }

    private static void PatchSdkVersion(GitRepository repository, string sdkVersion)
    {
        var globalJsonPath = Path.Combine(repository.RootPath, "scaffold", "build", "global.json");
        if (File.Exists(globalJsonPath))
        {
            var node = JsonNode.Parse(File.ReadAllText(globalJsonPath)) as JsonObject ??
                       throw new InvalidOperationException("build/global.json is not a JSON object.");
            var sdk = node["sdk"] as JsonObject ??
                      throw new InvalidOperationException("build/global.json has no sdk object.");
            sdk["version"] = sdkVersion;
            File.WriteAllText(globalJsonPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }) + Environment.NewLine);
        }

        var lockPath = Path.Combine(repository.RootPath, "scaffold", "build", "plutonium.lock");
        if (!File.Exists(lockPath))
        {
            return;
        }

        var lockFile = PlutoniumLockFile.Load(lockPath);
        lockFile.SetValue("sdk_version", sdkVersion);
        if (File.Exists(globalJsonPath))
        {
            lockFile.ReplaceHash("build/global.json", Hashing.Sha256File(globalJsonPath));
        }

        lockFile.Save();
    }

    private static string? ToLockPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        const string buildPrefix = "scaffold/build/";
        const string rootPrefix = "scaffold/root/";
        if (normalized.StartsWith(buildPrefix, StringComparison.Ordinal))
        {
            return "build/" + normalized[buildPrefix.Length..];
        }

        if (normalized.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return normalized[rootPrefix.Length..];
        }

        return null;
    }
}
