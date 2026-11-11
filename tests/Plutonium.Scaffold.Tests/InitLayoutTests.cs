using System.Text.Json;
using System.Xml.Linq;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins the layout and manifest a consumer receives from the init script.
/// </summary>
public sealed class InitLayoutTests
{
    /// <summary>The source ships both shell and PowerShell init entry points.</summary>
    [Fact]
    public void Init_ShipsBothEntryScripts()
    {
        var layout = RepositoryLayout.Locate();
        Assert.True(File.Exists(layout.InitScriptPath), "Missing init.sh at " + layout.InitScriptPath);
        Assert.True(
            File.Exists(layout.InitPowerShellPath),
            "Missing init.ps1 at " + layout.InitPowerShellPath);
    }

    /// <summary>The scaffold installs every managed file and the user-owned build definition.</summary>
    [Fact]
    public async Task Init_InstallsScaffoldLayout()
    {
        using var workspace = TempWorkspace.Create("init-layout");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        foreach (var relativePath in ScaffoldTestContext.RequiredFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(target.RootPath, relativePath)),
                "Init did not install " + relativePath);
        }

        var solutionPath = Path.Combine(target.RootPath, "build", "build.slnx");
        var solution = ScaffoldTestContext.ReadXml(solutionPath);
        Assert.Equal("Solution", solution.Root?.Name.LocalName);
        Assert.Contains(
            "build.csproj",
            ScaffoldTestContext.ReadText(solutionPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(ScaffoldTestContext.ReadText(
            Path.Combine(target.RootPath, "build", "build.cs"))));

        var gitignore = ScaffoldTestContext.ReadText(Path.Combine(target.RootPath, "build", ".gitignore"));
        Assert.Contains("temp", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bin", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obj", gitignore, StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>The SDK pin and lock manifest record the source commit and every managed hash.</summary>
    [Fact]
    public async Task Init_PinsSdkAndWritesLockManifest()
    {
        using var workspace = TempWorkspace.Create("init-lock");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        var globalJsonPath = Path.Combine(target.RootPath, "build", "global.json");
        using var globalJson = JsonDocument.Parse(ScaffoldTestContext.ReadText(globalJsonPath));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.402", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        var lockFile = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        Assert.False(string.IsNullOrWhiteSpace(lockFile.GetValue("format")));
        var recordedSource = ScaffoldTestContext.NormalizeSourceValue(
            lockFile.GetValue("source") ?? string.Empty);
        Assert.Contains(
            recordedSource,
            new[]
            {
                ScaffoldTestContext.NormalizeSourceValue(source.SourcePath),
                ScaffoldTestContext.NormalizeSourceValue(source.Repository.FileUrl),
            },
            StringComparer.Ordinal);
        Assert.Equal(source.HeadSha, lockFile.GetValue("scaffold_commit"));
        Assert.Equal(layout.ReadBuilderVersion(), lockFile.GetValue("package_version"));
        Assert.Equal("10.0.402", lockFile.GetValue("sdk_version"));
        Assert.True(
            lockFile.CountHashedLines("install") >= 1,
            "The lock must pin an installer checksum.");
        Assert.True(
            lockFile.CountHashedLines("archive") + lockFile.CountHashedLines("sdk") >= 1,
            "The lock must pin an SDK download checksum.");

        foreach (var relativePath in ScaffoldTestContext.ManagedFiles)
        {
            var expected = Hashing.Sha256File(Path.Combine(target.RootPath, relativePath));
            var actual = lockFile.GetHash(relativePath);
            Assert.True(actual is not null, "The lock has no hash for " + relativePath);
            Assert.Equal(expected, actual);
        }

        Assert.Contains(
            lockFile.Lines,
            line => line.Contains("build/Directory.Build.props", StringComparison.Ordinal));
        Assert.Contains(
            lockFile.Lines,
            line => line.Contains("build/Directory.Build.targets", StringComparison.Ordinal));
        Assert.Contains(
            lockFile.Lines,
            line => line.Contains("build/build.cs", StringComparison.Ordinal));

    }

    /// <summary>Init never edits or adds projects to the consumer solution.</summary>
    [Fact]
    public async Task Init_LeavesUserSolutionAndSentinelUntouched()
    {
        using var workspace = TempWorkspace.Create("init-solution");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var solutionPath = Path.Combine(target.RootPath, "Consumer.sln");
        var sentinelPath = Path.Combine(target.RootPath, "sentinel.txt");
        var solutionBefore = await File.ReadAllBytesAsync(solutionPath);
        var sentinelBefore = await File.ReadAllBytesAsync(sentinelPath);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        Assert.Equal(solutionBefore, await File.ReadAllBytesAsync(solutionPath));
        Assert.Equal(sentinelBefore, await File.ReadAllBytesAsync(sentinelPath));

        var solutionText = await File.ReadAllTextAsync(solutionPath);
        Assert.DoesNotContain("build.slnx", solutionText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("build.csproj", solutionText, StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>The installed shell entry points are executable and forward into build/.</summary>
    [Fact]
    public async Task Init_InstallsExecutableWrappers()
    {
        using var workspace = TempWorkspace.Create("init-wrappers");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        AssertExecutable(Path.Combine(target.RootPath, "build.sh"));
        AssertExecutable(Path.Combine(target.RootPath, "build", "build.sh"));
        AssertExecutable(Path.Combine(target.RootPath, "build", "update.sh"));

        Assert.Contains(
            "build/build.sh",
            ScaffoldTestContext.ReadText(Path.Combine(target.RootPath, "build.sh")),
            StringComparison.Ordinal);
        Assert.Contains(
            "build/build.ps1",
            ScaffoldTestContext.ReadText(Path.Combine(target.RootPath, "build.ps1")).Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "build/build.cmd",
            ScaffoldTestContext.ReadText(Path.Combine(target.RootPath, "build.cmd")).Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>The build project is executable, targets net10.0, and consumes the package.</summary>
    [Fact]
    public async Task Init_BuildProjectConsumesThePackage()
    {
        using var workspace = TempWorkspace.Create("init-project");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        var projectPath = Path.Combine(target.RootPath, "build", "build.csproj");
        var project = ScaffoldTestContext.ReadXml(projectPath);
        Assert.Equal("Exe", DescendantValue(project.Root!, "OutputType"), ignoreCase: true);
        Assert.Equal("net10.0", DescendantValue(project.Root!, "TargetFramework"));

        var reference = project.Descendants().Single(element =>
            element.Name.LocalName == "PackageReference" &&
            string.Equals(
                element.Attribute("Include")?.Value,
                "PlutoniumBuild",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(layout.ReadBuilderVersion(), reference.Attribute("Version")?.Value);
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "ProjectReference");

    }

    /// <summary>Init creates the user-owned build property files without hashing them as managed.</summary>
    [Fact]
    public async Task Init_CreatesUserOwnedPropertyFiles()
    {
        using var workspace = TempWorkspace.Create("init-props");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(result, "init");

        var propsPath = Path.Combine(target.RootPath, "build", "Directory.Build.props");
        var targetsPath = Path.Combine(target.RootPath, "build", "Directory.Build.targets");
        Assert.True(File.Exists(propsPath), "Missing user-owned Directory.Build.props.");
        Assert.True(File.Exists(targetsPath), "Missing user-owned Directory.Build.targets.");

        var lockFile = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        Assert.Null(lockFile.GetHash("build/Directory.Build.props"));
        Assert.Null(lockFile.GetHash("build/Directory.Build.targets"));

    }

    private static string? DescendantValue(XElement parent, string name, bool ignoreCase = false) =>
        parent.Descendants()
            .FirstOrDefault(element => string.Equals(
                element.Name.LocalName,
                name,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            ?.Value
            .Trim();

    private static void AssertExecutable(string path)
    {
        Assert.True(File.Exists(path), "Expected executable not found: " + path);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        Assert.True(
            (mode & UnixFileMode.UserExecute) != 0,
            "File is not executable: " + path + " (" + mode + ")");
    }
}
