using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins init refusal behavior: no overwrite, no write-through symlinks, and a
/// full source commit SHA.
/// </summary>
public sealed class InitSafetyTests
{
    /// <summary>A second init leaves the installed scaffold and consumer edits untouched.</summary>
    [Fact]
    public async Task Init_RefusesToOverwriteAnExistingScaffold()
    {
        using var workspace = TempWorkspace.Create("init-overwrite");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var first = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(first, "initial init");

        var buildFile = Path.Combine(target.RootPath, "build", "build.cs");
        await File.AppendAllTextAsync(buildFile, "\n// consumer customization\n");

        var before = DirectorySnapshot.Capture(target.RootPath, ".git", ".test-home");
        var second = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertFailed(second, "second init");

        var output = CombinedOutput(second);
        Assert.True(
            output.Contains("already", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("exists", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("initialized", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("refus", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("modified", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("dirty", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("custom", StringComparison.OrdinalIgnoreCase),
            "Secondary init should explain that the scaffold already exists." + Environment.NewLine + second.Describe());
        Assert.Contains("consumer customization", await File.ReadAllTextAsync(buildFile));

        var differences = before.Differences(DirectorySnapshot.Capture(target.RootPath, ".git", ".test-home"));
        Assert.True(differences.Count == 0, "Second init changed the tree: " + string.Join(", ", differences));

    }

    /// <summary>A symlinked build directory is refused and the link target is untouched.</summary>
    [Fact]
    public async Task Init_RefusesSymlinkedBuildDirectory()
    {
        using var workspace = TempWorkspace.Create("init-symlink-dir");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var outside = workspace.PathFor("outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "sentinel.txt"), "outside\n");
        var buildPath = Path.Combine(target.RootPath, "build");
        Directory.CreateSymbolicLink(buildPath, outside);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertFailed(result, "init over a symlinked build directory");

        Assert.Equal("outside\n", await File.ReadAllTextAsync(
            Path.Combine(outside, "sentinel.txt")));
        Assert.False(File.Exists(Path.Combine(outside, "build.slnx")));
        Assert.NotNull(new DirectoryInfo(buildPath).LinkTarget);

    }

    /// <summary>A symlinked root wrapper is refused and the link target is untouched.</summary>
    [Fact]
    public async Task Init_RefusesSymlinkedRootWrapper()
    {
        using var workspace = TempWorkspace.Create("init-symlink-file");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var victim = Path.Combine(workspace.Root, "victim.sh");
        await File.WriteAllTextAsync(victim, "#!/usr/bin/env bash\n# victim\n");
        var wrapperPath = Path.Combine(target.RootPath, "build.sh");
        File.CreateSymbolicLink(wrapperPath, victim);

        var result = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertFailed(result, "init over a symlinked root wrapper");

        Assert.Equal(
            "#!/usr/bin/env bash\n# victim\n",
            await File.ReadAllTextAsync(victim));
        Assert.NotNull(File.ResolveLinkTarget(wrapperPath, returnFinalTarget: false));

    }

    /// <summary>Init refuses to run without the full commit SHA.</summary>
    [Fact]
    public async Task Init_RequiresFullCommitSha()
    {
        using var workspace = TempWorkspace.Create("init-no-sha");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var missing = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath],
            target.RootPath);
        ScaffoldTestContext.AssertFailed(missing, "init without --sha");
        Assert.Contains("sha", CombinedOutput(missing), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));

        var shortSha = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--sha", source.HeadSha[..12]],
            target.RootPath);
        ScaffoldTestContext.AssertFailed(shortSha, "init with a short --sha");
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));

    }

    /// <summary>Init fails cleanly when the pinned commit does not exist.</summary>
    [Fact]
    public async Task Init_RejectsUnknownCommit()
    {
        using var workspace = TempWorkspace.Create("init-unknown-sha");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--sha", new string('0', 40)],
            target.RootPath);

        ScaffoldTestContext.AssertFailed(result, "init with an unknown commit");
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));

    }

    private static string CombinedOutput(ProcessResult result) =>
        result.StandardOutput + Environment.NewLine + result.StandardError;
}
