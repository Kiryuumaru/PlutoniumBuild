using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins updater behavior: user-owned files survive, managed package pins move,
/// default-ref resolution installs the requested commit, and repeating an
/// update is a no-op.
/// </summary>
public sealed class UpdateTests
{
    private static readonly string[] MalformedRefs =
    [
        "-refs/heads/main",
        "refs/heads/ma in",
        "refs/heads/ma\tin",
        "refs/heads/ma:in",
        "refs/heads/ma+in",
        "refs/heads/ma^in",
        "refs/heads/ma~in",
        "refs/heads/ma?in",
        "refs/heads/ma*in",
        "refs/heads/ma[in",
        @"refs/heads/ma\in",
        "refs/heads/ma@{in",
        "refs/heads/ma..in",
        "refs/heads/main.lock",
        "refs/heads/main.lock/child",
        "refs/heads/main.",
        "refs/heads/main/",
        "/refs/heads/main",
        "refs/pull/1/head",
        "refs/remotes/origin/main",
    ];

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

    /// <summary>Update without a ref installs the moved tip of the default main branch.</summary>
    [Fact]
    public async Task Update_DefaultsToMain()
    {
        using var workspace = TempWorkspace.Create("update-default-main");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var initialCommit = source.HeadSha;

        var updatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# default main update marker\n");

        var update = await ScaffoldTestContext.RunUpdateAsync(target, source);
        ScaffoldTestContext.AssertSucceeded(update, "update from the default main branch");

        Assert.Contains(
            "ref refs/heads/main (default)",
            update.StandardOutput + Environment.NewLine + update.StandardError,
            StringComparison.Ordinal);
        ScaffoldTestContext.AssertScaffoldCommit(target, updatedCommit);
        Assert.Contains(
            "default main update marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        var journal = ScaffoldTestContext.ReadUpdateJournal(target);
        Assert.Equal(initialCommit, journal["from_commit"]);
        Assert.Equal(updatedCommit, journal["to_commit"]);
    }

    /// <summary>Repeating a default-ref update at the same main tip is a no-op.</summary>
    [Fact]
    public async Task Update_IsIdempotentAtDefaultMain()
    {
        using var workspace = TempWorkspace.Create("update-default-idempotent");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# default main idempotent marker\n");
        var first = await ScaffoldTestContext.RunUpdateAsync(target, source);
        ScaffoldTestContext.AssertSucceeded(first, "first update from the default main branch");

        var afterFirst = DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj");
        var second = await ScaffoldTestContext.RunUpdateAsync(target, source);
        ScaffoldTestContext.AssertSucceeded(second, "second update from the default main branch");
        Assert.Contains(
            "nothing to update",
            second.StandardOutput + Environment.NewLine + second.StandardError,
            StringComparison.OrdinalIgnoreCase);

        var differences = afterFirst.Differences(DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj"));
        Assert.True(
            differences.Count == 0,
            "Repeated default-ref update changed the tree: " + string.Join(", ", differences));
    }

    /// <summary>Update peels lightweight and annotated tags to the tagged commit.</summary>
    [Fact]
    public async Task Update_AcceptsTagRef()
    {
        using var workspace = TempWorkspace.Create("update-tag-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var lightTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-light");
        var annotatedTarget = ScaffoldTestContext.CreateConsumerRepository(
            workspace,
            name: "consumer-annotated");

        var lightInit = await ScaffoldTestContext.RunInitAsync(source, lightTarget);
        ScaffoldTestContext.AssertSucceeded(lightInit, "lightweight tag init");
        var annotatedInit = await ScaffoldTestContext.RunInitAsync(source, annotatedTarget);
        ScaffoldTestContext.AssertSucceeded(annotatedInit, "annotated tag init");
        var initialCommit = source.HeadSha;

        var lightCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# lightweight update tag marker\n");
        source.CreateLightweightTag("v-light");

        var annotatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# annotated update tag marker\n");
        source.CreateAnnotatedTag("v-annotated", "annotated update release");

        var annotatedObject = source.ResolveObject("refs/tags/v-annotated");
        Assert.NotEqual(annotatedCommit, annotatedObject);

        var light = await ScaffoldTestContext.RunUpdateAsync(
            lightTarget,
            source,
            refName: "refs/tags/v-light");
        ScaffoldTestContext.AssertSucceeded(light, "update from a lightweight tag");
        ScaffoldTestContext.AssertScaffoldCommit(lightTarget, lightCommit);
        var lightBuild = await File.ReadAllTextAsync(
            Path.Combine(lightTarget.RootPath, "build", "build.sh"));
        Assert.Contains("lightweight update tag marker", lightBuild, StringComparison.Ordinal);
        Assert.DoesNotContain("annotated update tag marker", lightBuild, StringComparison.Ordinal);
        var lightJournal = ScaffoldTestContext.ReadUpdateJournal(lightTarget);
        Assert.Equal(initialCommit, lightJournal["from_commit"]);
        Assert.Equal(lightCommit, lightJournal["to_commit"]);

        var annotated = await ScaffoldTestContext.RunUpdateAsync(
            annotatedTarget,
            source,
            refName: "refs/tags/v-annotated");
        ScaffoldTestContext.AssertSucceeded(annotated, "update from an annotated tag");
        ScaffoldTestContext.AssertScaffoldCommit(annotatedTarget, annotatedCommit);
        Assert.NotEqual(
            annotatedObject,
            ScaffoldTestContext.ReadScaffoldCommit(annotatedTarget));
        Assert.Contains(
            "annotated update tag marker",
            await File.ReadAllTextAsync(Path.Combine(annotatedTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        var annotatedJournal = ScaffoldTestContext.ReadUpdateJournal(annotatedTarget);
        Assert.Equal(initialCommit, annotatedJournal["from_commit"]);
        Assert.Equal(annotatedCommit, annotatedJournal["to_commit"]);
    }

    /// <summary>Update accepts a bare branch name and resolves it to the branch tip.</summary>
    [Fact]
    public async Task Update_AcceptsBareBranchRef()
    {
        using var workspace = TempWorkspace.Create("update-bare-branch-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var initialCommit = source.HeadSha;

        source.Repository.CreateBranch("feature");
        var expected = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare update branch marker\n");

        var update = await ScaffoldTestContext.RunUpdateAsync(target, source, refName: "feature");
        ScaffoldTestContext.AssertSucceeded(update, "update with --ref feature");

        ScaffoldTestContext.AssertScaffoldCommit(target, expected);
        Assert.Contains(
            "bare update branch marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        var journal = ScaffoldTestContext.ReadUpdateJournal(target);
        Assert.Equal(initialCommit, journal["from_commit"]);
        Assert.Equal(expected, journal["to_commit"]);
    }

    /// <summary>Update accepts bare lightweight and annotated tag names and peels tag objects.</summary>
    [Fact]
    public async Task Update_AcceptsBareTagRef()
    {
        using var workspace = TempWorkspace.Create("update-bare-tag-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var lightTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-light");
        var annotatedTarget = ScaffoldTestContext.CreateConsumerRepository(
            workspace,
            name: "consumer-annotated");

        var lightInit = await ScaffoldTestContext.RunInitAsync(source, lightTarget);
        ScaffoldTestContext.AssertSucceeded(lightInit, "lightweight tag init");
        var annotatedInit = await ScaffoldTestContext.RunInitAsync(source, annotatedTarget);
        ScaffoldTestContext.AssertSucceeded(annotatedInit, "annotated tag init");
        var initialCommit = source.HeadSha;

        var lightCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare lightweight update tag marker\n");
        source.CreateLightweightTag("v-light");

        var annotatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare annotated update tag marker\n");
        source.CreateAnnotatedTag("v-annotated", "bare annotated update release");

        var annotatedObject = source.ResolveObject("refs/tags/v-annotated");
        Assert.NotEqual(annotatedCommit, annotatedObject);

        var light = await ScaffoldTestContext.RunUpdateAsync(
            lightTarget,
            source,
            refName: "v-light");
        ScaffoldTestContext.AssertSucceeded(light, "update with a bare lightweight tag name");
        ScaffoldTestContext.AssertScaffoldCommit(lightTarget, lightCommit);
        Assert.Contains(
            "bare lightweight update tag marker",
            await File.ReadAllTextAsync(Path.Combine(lightTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        Assert.Equal(lightCommit, ScaffoldTestContext.ReadUpdateJournal(lightTarget)["to_commit"]);

        var annotated = await ScaffoldTestContext.RunUpdateAsync(
            annotatedTarget,
            source,
            refName: "v-annotated");
        ScaffoldTestContext.AssertSucceeded(annotated, "update with a bare annotated tag name");
        ScaffoldTestContext.AssertScaffoldCommit(annotatedTarget, annotatedCommit);
        Assert.NotEqual(annotatedObject, ScaffoldTestContext.ReadScaffoldCommit(annotatedTarget));
        Assert.Contains(
            "bare annotated update tag marker",
            await File.ReadAllTextAsync(Path.Combine(annotatedTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        Assert.Equal(initialCommit, ScaffoldTestContext.ReadUpdateJournal(annotatedTarget)["from_commit"]);
        Assert.Equal(annotatedCommit, ScaffoldTestContext.ReadUpdateJournal(annotatedTarget)["to_commit"]);
    }

    /// <summary>A bare name matching both a branch and a tag is ambiguous and names both refs.</summary>
    [Fact]
    public async Task Update_RejectsAmbiguousBareRef()
    {
        using var workspace = TempWorkspace.Create("update-ambiguous-bare-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        var originalHash = Hashing.Sha256File(lockPath);

        source.CreateLightweightTag("shared");
        source.Repository.CreateBranch("shared");

        var result = await ScaffoldTestContext.RunUpdateAsync(target, source, refName: "shared");
        ScaffoldTestContext.AssertFailed(result, "update with an ambiguous bare ref");
        Assert.Equal(2, result.ExitCode);
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;
        Assert.Contains("ambiguous", output, StringComparison.Ordinal);
        Assert.Contains("refs/heads/shared", output, StringComparison.Ordinal);
        Assert.Contains("refs/tags/shared", output, StringComparison.Ordinal);
        Assert.Contains("pass refs/heads/shared or refs/tags/shared", output, StringComparison.Ordinal);
        Assert.Equal(originalHash, Hashing.Sha256File(lockPath));
        Assert.False(Directory.Exists(Path.Combine(target.RootPath, "build", "temp", "update")));
    }

    /// <summary>A bare name that matches neither a branch nor a tag is not found.</summary>
    [Fact]
    public async Task Update_UnknownBareRefNotFound()
    {
        using var workspace = TempWorkspace.Create("update-unknown-bare-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        var originalHash = Hashing.Sha256File(lockPath);

        var result = await ScaffoldTestContext.RunUpdateAsync(
            target,
            source,
            refName: "does-not-exist");
        ScaffoldTestContext.AssertFailed(result, "update with an unknown bare ref");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "was not found",
            result.StandardOutput + Environment.NewLine + result.StandardError,
            StringComparison.Ordinal);
        Assert.Equal(originalHash, Hashing.Sha256File(lockPath));
        Assert.False(Directory.Exists(Path.Combine(target.RootPath, "build", "temp", "update")));
    }

    /// <summary>Fully qualified refs disambiguate a name that is both a branch and a tag.</summary>
    [Fact]
    public async Task Update_QualifiedRefsDisambiguate()
    {
        using var workspace = TempWorkspace.Create("update-qualified-disambiguate");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var branchTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-branch");
        var tagTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-tag");

        var branchInit = await ScaffoldTestContext.RunInitAsync(source, branchTarget);
        ScaffoldTestContext.AssertSucceeded(branchInit, "branch init");
        var tagInit = await ScaffoldTestContext.RunInitAsync(source, tagTarget);
        ScaffoldTestContext.AssertSucceeded(tagInit, "tag init");

        var tagCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# qualified update tag marker\n");
        source.CreateLightweightTag("shared");

        var branchCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# qualified update branch marker\n");
        source.Repository.CreateBranch("shared");
        Assert.NotEqual(tagCommit, branchCommit);

        var branch = await ScaffoldTestContext.RunUpdateAsync(
            branchTarget,
            source,
            refName: "refs/heads/shared");
        ScaffoldTestContext.AssertSucceeded(branch, "update with a qualified branch ref");
        ScaffoldTestContext.AssertScaffoldCommit(branchTarget, branchCommit);
        Assert.Contains(
            "qualified update branch marker",
            await File.ReadAllTextAsync(Path.Combine(branchTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        var tag = await ScaffoldTestContext.RunUpdateAsync(
            tagTarget,
            source,
            refName: "refs/tags/shared");
        ScaffoldTestContext.AssertSucceeded(tag, "update with a qualified tag ref");
        ScaffoldTestContext.AssertScaffoldCommit(tagTarget, tagCommit);
        Assert.Contains(
            "qualified update tag marker",
            await File.ReadAllTextAsync(Path.Combine(tagTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Update accepts a full commit SHA through <c>--ref</c>.</summary>
    [Fact]
    public async Task Update_AcceptsFullShaRef()
    {
        using var workspace = TempWorkspace.Create("update-full-sha-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var initialCommit = source.HeadSha;

        var commit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# full sha update marker\n");
        var update = await ScaffoldTestContext.RunUpdateAsync(
            target,
            source,
            refName: commit.ToUpperInvariant());
        ScaffoldTestContext.AssertSucceeded(update, "update with --ref <sha>");

        ScaffoldTestContext.AssertScaffoldCommit(target, commit);
        Assert.Contains(
            "full sha update marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        var journal = ScaffoldTestContext.ReadUpdateJournal(target);
        Assert.Equal(initialCommit, journal["from_commit"]);
        Assert.Equal(commit, journal["to_commit"]);
    }

    /// <summary>A no-arg update preserves the installed source and source_url.</summary>
    [Fact]
    public async Task Update_PreservesSourceIdentity()
    {
        using var workspace = TempWorkspace.Create("update-source-identity");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var installedLock = ScaffoldTestContext.ReadLock(target);
        var originalSource = ScaffoldTestContext.NormalizeSourceValue(
            installedLock.GetValue("source") ?? string.Empty);
        var originalSourceUrl = ScaffoldTestContext.NormalizeSourceValue(
            installedLock.GetValue("source_url") ?? string.Empty);

        var updatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# no-arg source identity marker\n");

        var update = await ScaffoldTestContext.RunUpdateAsync(
            target,
            source,
            sha: updatedCommit,
            includeSource: false);
        ScaffoldTestContext.AssertSucceeded(update, "no-arg update");

        ScaffoldTestContext.AssertScaffoldCommit(target, updatedCommit);
        var updatedLock = ScaffoldTestContext.ReadLock(target);
        Assert.Equal(
            originalSource,
            ScaffoldTestContext.NormalizeSourceValue(updatedLock.GetValue("source") ?? string.Empty));
        Assert.Equal(
            originalSourceUrl,
            ScaffoldTestContext.NormalizeSourceValue(updatedLock.GetValue("source_url") ?? string.Empty));
        Assert.Contains(
            "no-arg source identity marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        Assert.Equal(updatedCommit, ScaffoldTestContext.ReadUpdateJournal(target)["to_commit"]);
    }

    /// <summary>An explicit --source re-homes both lock fields, and a later no-arg update follows it.</summary>
    [Fact]
    public async Task Update_ExplicitSourceRehomesSourceIdentity()
    {
        using var workspace = TempWorkspace.Create("update-source-rehome");
        using var forkWorkspace = TempWorkspace.Create("update-source-rehome-fork");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var fork = await ScaffoldSource.CreateAsync(forkWorkspace, layout, defaultBranch: "trunk");
        var forkCommit = fork.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# explicit source rehome marker\n");

        var update = await ScaffoldTestContext.RunUpdateAsync(
            target,
            fork,
            sha: forkCommit,
            includeSource: true);
        ScaffoldTestContext.AssertSucceeded(update, "update with an explicit source");

        ScaffoldTestContext.AssertScaffoldCommit(target, forkCommit);
        var rehomedLock = ScaffoldTestContext.ReadLock(target);
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(fork.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(rehomedLock.GetValue("source") ?? string.Empty));
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(fork.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(rehomedLock.GetValue("source_url") ?? string.Empty));
        Assert.Contains(
            "explicit source rehome marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        var followCommit = fork.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# rehomed source follow marker\n");
        var follow = await ScaffoldTestContext.RunUpdateAsync(
            target,
            fork,
            sha: followCommit,
            includeSource: false);
        ScaffoldTestContext.AssertSucceeded(follow, "no-arg update from the re-homed source");

        ScaffoldTestContext.AssertScaffoldCommit(target, followCommit);
        Assert.Contains(
            "rehomed source follow marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
        var followedLock = ScaffoldTestContext.ReadLock(target);
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(fork.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(followedLock.GetValue("source") ?? string.Empty));
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(fork.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(followedLock.GetValue("source_url") ?? string.Empty));
    }

    /// <summary>Update rejects a poisoned installed lock before resolving or fetching.</summary>
    [Fact]
    public async Task Update_RejectsPoisonedInstalledLock()
    {
        var layout = RepositoryLayout.Locate();
        foreach (var poison in Enum.GetValues<ScaffoldTestContext.LockPoison>())
        {
            using var workspace = TempWorkspace.Create("update-poisoned-installed-lock-" + poison);
            var source = await ScaffoldSource.CreateAsync(workspace, layout);
            var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

            var init = await ScaffoldTestContext.RunInitAsync(source, target);
            ScaffoldTestContext.AssertSucceeded(init, "init");

            var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
            ScaffoldTestContext.WriteLockPoison(lockPath, poison);
            var poisonedHash = Hashing.Sha256File(lockPath);
            var marker = workspace.PathFor("git-invoked");
            var toolchain = ScaffoldTestContext.CreateGitInvocationProbe(workspace, marker);

            var result = await ScaffoldTestContext.RunScriptAsync(
                Path.Combine(target.RootPath, "build", "update.sh"),
                [
                    "--source",
                    source.SourcePath,
                    "--target",
                    target.RootPath,
                    "--no-smoke",
                ],
                target.RootPath,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["PATH"] = toolchain.BuildPath(),
                });

            ScaffoldTestContext.AssertFailed(
                result,
                "update with a poisoned installed lock (" + poison + ")");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                ScaffoldTestContext.LockPoisonMessage(poison),
                result.StandardOutput + Environment.NewLine + result.StandardError,
                StringComparison.Ordinal);
            Assert.False(
                File.Exists(marker),
                "Update invoked git before rejecting a poisoned installed lock (" + poison + ").");
            Assert.Equal(poisonedHash, Hashing.Sha256File(lockPath));
            Assert.False(Directory.Exists(Path.Combine(target.RootPath, "build", "temp", "update")));
        }
    }

    /// <summary>Update rejects a poisoned source lock before installing anything.</summary>
    [Fact]
    public async Task Update_RejectsPoisonedSourceLock()
    {
        var layout = RepositoryLayout.Locate();
        foreach (var poison in Enum.GetValues<ScaffoldTestContext.LockPoison>())
        {
            using var workspace = TempWorkspace.Create("update-poisoned-source-lock-" + poison);
            var source = await ScaffoldSource.CreateAsync(workspace, layout);
            var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

            var init = await ScaffoldTestContext.RunInitAsync(source, target);
            ScaffoldTestContext.AssertSucceeded(init, "init");
            var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
            var installedHash = Hashing.Sha256File(lockPath);
            var poisonedCommit = source.CommitChange(
                "scaffold/build/plutonium.lock",
                text => ScaffoldTestContext.ApplyLockPoison(text, poison));

            var result = await ScaffoldTestContext.RunUpdateAsync(
                target,
                source,
                sha: poisonedCommit);
            ScaffoldTestContext.AssertFailed(
                result,
                "update with a poisoned source lock (" + poison + ")");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                ScaffoldTestContext.LockPoisonMessage(poison),
                result.StandardOutput + Environment.NewLine + result.StandardError,
                StringComparison.Ordinal);
            Assert.Equal(installedHash, Hashing.Sha256File(lockPath));
            Assert.False(Directory.Exists(Path.Combine(target.RootPath, "build", "temp", "update")));
        }
    }

    /// <summary>Update rejects every malformed ref shape before touching the lock.</summary>
    [Fact]
    public async Task Update_RejectsMalformedRef()
    {
        using var workspace = TempWorkspace.Create("update-malformed-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        var originalCommit = ScaffoldTestContext.ReadScaffoldCommit(target);
        var originalHash = Hashing.Sha256File(lockPath);

        foreach (var value in MalformedRefs)
        {
            var result = await ScaffoldTestContext.RunUpdateAsync(target, source, refName: value);
            ScaffoldTestContext.AssertFailed(result, "update with malformed ref '" + value + "'");
            Assert.Equal(2, result.ExitCode);
            Assert.Contains(
                "--ref must be",
                result.StandardOutput + Environment.NewLine + result.StandardError,
                StringComparison.Ordinal);
            Assert.Equal(originalCommit, ScaffoldTestContext.ReadScaffoldCommit(target));
            Assert.Equal(originalHash, Hashing.Sha256File(lockPath));
        }
    }

    /// <summary>Update rejects an option-shaped ref without invoking git.</summary>
    [Fact]
    public async Task Update_RejectsOptionInjection()
    {
        using var workspace = TempWorkspace.Create("update-option-injection");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        var originalHash = Hashing.Sha256File(lockPath);
        var marker = workspace.PathFor("git-invoked");
        var toolchain = ScaffoldTestContext.CreateGitInvocationProbe(workspace, marker);

        var result = await ScaffoldTestContext.RunScriptAsync(
            Path.Combine(target.RootPath, "build", "update.sh"),
            [
                "--source",
                source.SourcePath,
                "--target",
                target.RootPath,
                "--ref",
                "--upload-pack=" + ScaffoldTestContext.ToBashPath(marker),
                "--no-smoke",
            ],
            target.RootPath,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = toolchain.BuildPath(),
            });

        ScaffoldTestContext.AssertFailed(result, "update with an option-shaped ref");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(
            "--ref must be",
            result.StandardOutput + Environment.NewLine + result.StandardError,
            StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "Update invoked git before rejecting the option-shaped ref.");
        Assert.Equal(originalHash, Hashing.Sha256File(lockPath));
    }

    /// <summary>Update refuses <c>--sha</c> and <c>--ref</c> together.</summary>
    [Fact]
    public async Task Update_RejectsShaAndRefTogether()
    {
        using var workspace = TempWorkspace.Create("update-sha-and-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var lockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        var originalHash = Hashing.Sha256File(lockPath);

        var result = await ScaffoldTestContext.RunScriptAsync(
            Path.Combine(target.RootPath, "build", "update.sh"),
            [
                "--source",
                source.SourcePath,
                "--target",
                target.RootPath,
                "--sha",
                source.HeadSha,
                "--ref",
                "main",
                "--no-smoke",
            ],
            target.RootPath);

        ScaffoldTestContext.AssertFailed(result, "update with --sha and --ref");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(
            "mutually exclusive",
            result.StandardOutput + Environment.NewLine + result.StandardError,
            StringComparison.Ordinal);
        Assert.Equal(originalHash, Hashing.Sha256File(lockPath));
    }

    /// <summary>Update passes the source and commit after the git option terminator.</summary>
    [Fact]
    public async Task Update_PassesRefspecAfterOptionTerminator()
    {
        using var workspace = TempWorkspace.Create("update-option-terminator");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var updatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# option terminator update marker\n");

        var realGit = await ScaffoldTestContext.ResolveGitPathAsync(workspace);
        var logPath = workspace.PathFor("git.log");
        var toolchain = ScaffoldTestContext.CreateLoggingGitShim(workspace, realGit, logPath);

        var result = await ScaffoldTestContext.RunScriptAsync(
            Path.Combine(target.RootPath, "build", "update.sh"),
            [
                "--source",
                source.SourcePath,
                "--target",
                target.RootPath,
                "--ref",
                "main",
                "--no-smoke",
            ],
            target.RootPath,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = toolchain.BuildPath(),
            });

        ScaffoldTestContext.AssertSucceeded(result, "update through a logging git shim");
        ScaffoldTestContext.AssertScaffoldCommit(target, updatedCommit);
        Assert.True(File.Exists(logPath), "The logging git shim did not record an invocation.");
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains(
            "ls-remote -- " + ScaffoldTestContext.ToBashPath(source.SourcePath),
            log,
            StringComparison.Ordinal);
        Assert.Contains(
            "fetch --depth 1 --no-tags origin -- " + updatedCommit,
            log,
            StringComparison.Ordinal);
    }
}
