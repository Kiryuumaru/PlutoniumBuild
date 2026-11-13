using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins updater behavior: user-owned files survive, managed package pins move,
/// and repeating an update is a no-op.
/// </summary>
public sealed class UpdateTests
{
    /// <summary>Update preserves build.cs and user props while bumping the package pin.</summary>
    [Fact]
    public async Task Update_PreservesUserFilesAndBumpsPackageVersion()
    {
        using var workspace = TempWorkspace.Create("update-preserve");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var buildFile = Path.Combine(target.RootPath, "build", "build.cs");
        var propsFile = Path.Combine(target.RootPath, "build", "Directory.Build.props");
        var targetsFile = Path.Combine(target.RootPath, "build", "Directory.Build.targets");
        await File.AppendAllTextAsync(buildFile, "\n// consumer entry customization\n");
        await File.AppendAllTextAsync(propsFile, "\n<!-- consumer property customization -->\n");
        await File.AppendAllTextAsync(targetsFile, "\n<!-- consumer target customization -->\n");

        var solutionPath = Path.Combine(target.RootPath, "Consumer.sln");
        var sentinelPath = Path.Combine(target.RootPath, "sentinel.txt");
        var solutionBefore = await File.ReadAllBytesAsync(solutionPath);
        var sentinelBefore = await File.ReadAllBytesAsync(sentinelPath);

        source.CommitPackageVersion("0.2.0");
        source.CommitChange("scaffold/build/build.sh", text => text + "\n# updated managed script\n");
        var newSha = source.HeadSha;
        var update = await ScaffoldTestContext.RunUpdateAsync(target, source, newSha);
        ScaffoldTestContext.AssertSucceeded(update, "update");

        Assert.Contains(
            "updated managed script",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        Assert.Contains(
            "consumer entry customization",
            await File.ReadAllTextAsync(buildFile),
            StringComparison.Ordinal);
        Assert.Contains(
            "consumer property customization",
            await File.ReadAllTextAsync(propsFile),
            StringComparison.Ordinal);
        Assert.Contains(
            "consumer target customization",
            await File.ReadAllTextAsync(targetsFile),
            StringComparison.Ordinal);

        Assert.Equal(
            "0.2.0",
            ScaffoldTestContext.PackageReferenceVersion(Path.Combine(target.RootPath, "build", "build.csproj")));
        var lockFile = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        Assert.Equal("0.2.0", lockFile.GetValue("package_version"));
        Assert.Equal(newSha, lockFile.GetValue("scaffold_commit"));

        Assert.Equal(solutionBefore, await File.ReadAllBytesAsync(solutionPath));
        Assert.Equal(sentinelBefore, await File.ReadAllBytesAsync(sentinelPath));

    }

    /// <summary>Update refreshes the managed build guide while preserving build.cs.</summary>
    [Fact]
    public async Task Update_RefreshesManagedBuildReadme()
    {
        using var workspace = TempWorkspace.Create("update-build-readme");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var buildFile = Path.Combine(target.RootPath, "build", "build.cs");
        await File.AppendAllTextAsync(buildFile, "\n// consumer entry customization\n");

        source.CommitChange(
            "scaffold/build/README.md",
            text => text + "\n<!-- updated managed build guide -->\n");
        var newSha = source.HeadSha;
        var update = await ScaffoldTestContext.RunUpdateAsync(target, source, newSha);
        ScaffoldTestContext.AssertSucceeded(update, "update");

        var sourceReadme = Path.Combine(source.ScaffoldBuildPath, "README.md");
        var installedReadme = Path.Combine(target.RootPath, "build", "README.md");
        Assert.True(File.Exists(installedReadme), "Update removed build/README.md.");
        Assert.Equal(
            await File.ReadAllBytesAsync(sourceReadme),
            await File.ReadAllBytesAsync(installedReadme));
        Assert.Contains(
            "updated managed build guide",
            await File.ReadAllTextAsync(installedReadme),
            StringComparison.Ordinal);
        Assert.Contains(
            "consumer entry customization",
            await File.ReadAllTextAsync(buildFile),
            StringComparison.Ordinal);

        var lockFile = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        Assert.Equal(
            Hashing.Sha256File(sourceReadme),
            lockFile.GetHash("build/README.md"));
        Assert.Equal(newSha, lockFile.GetValue("scaffold_commit"));

    }

    /// <summary>Running the same update twice produces an identical tree.</summary>
    [Fact]
    public async Task Update_IsIdempotent()
    {
        using var workspace = TempWorkspace.Create("update-idempotent");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var newSha = source.CommitPackageVersion("0.2.0");
        var first = await ScaffoldTestContext.RunUpdateAsync(target, source, newSha);
        ScaffoldTestContext.AssertSucceeded(first, "first update");

        var afterFirst = DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj");
        var second = await ScaffoldTestContext.RunUpdateAsync(target, source, newSha);
        ScaffoldTestContext.AssertSucceeded(second, "second update");

        var differences = afterFirst.Differences(DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj"));
        Assert.True(differences.Count == 0, "Repeated update changed the tree: " + string.Join(", ", differences));

    }

    /// <summary>Update refuses to run without a full commit SHA.</summary>
    [Fact]
    public async Task Update_RequiresFullCommitSha()
    {
        using var workspace = TempWorkspace.Create("update-no-sha");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var lockFile = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        var originalCommit = lockFile.GetValue("scaffold_commit");
        var originalVersion = ScaffoldTestContext.PackageReferenceVersion(
            Path.Combine(target.RootPath, "build", "build.csproj"));

        var missing = await ScaffoldTestContext.RunUpdateAsync(target, source);
        ScaffoldTestContext.AssertFailed(missing, "update without --sha");
        Assert.Contains(
            "sha",
            missing.StandardOutput + missing.StandardError,
            StringComparison.OrdinalIgnoreCase);

        var shortSha = await ScaffoldTestContext.RunUpdateAsync(
            target,
            source,
            source.HeadSha[..12]);
        ScaffoldTestContext.AssertFailed(shortSha, "update with a short --sha");

        var after = PlutoniumLockFile.Load(Path.Combine(target.RootPath, "build", "plutonium.lock"));
        Assert.Equal(originalCommit, after.GetValue("scaffold_commit"));
        Assert.Equal(
            originalVersion,
            ScaffoldTestContext.PackageReferenceVersion(Path.Combine(target.RootPath, "build", "build.csproj")));

    }
}
