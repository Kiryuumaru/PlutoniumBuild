using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins init refusal behavior: no overwrite, no write-through symlinks, and
/// default-ref resolution to a full source commit.
/// </summary>
public sealed class InitSafetyTests
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

    /// <summary>Init without a ref installs the tip of the default main branch.</summary>
    [Fact]
    public async Task Init_DefaultsToMain()
    {
        using var workspace = TempWorkspace.Create("init-default-main");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var expected = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# default main marker\n");

        var result = await ScaffoldTestContext.RunInitAsync(source, target, useDefaultRef: true);
        ScaffoldTestContext.AssertSucceeded(result, "init from the default main branch");

        Assert.Contains("ref refs/heads/main (default)", CombinedOutput(result), StringComparison.Ordinal);
        ScaffoldTestContext.AssertScaffoldCommit(target, expected);
        Assert.Contains(
            "default main marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init accepts the literal <c>main</c> ref and resolves it to the branch tip.</summary>
    [Fact]
    public async Task Init_AcceptsRefMain()
    {
        using var workspace = TempWorkspace.Create("init-ref-main");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var expected = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# explicit main marker\n");

        var result = await ScaffoldTestContext.RunInitAsync(source, target, refName: "main");
        ScaffoldTestContext.AssertSucceeded(result, "init with --ref main");

        ScaffoldTestContext.AssertScaffoldCommit(target, expected);
        Assert.Contains(
            "explicit main marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init accepts a fully qualified branch ref.</summary>
    [Fact]
    public async Task Init_AcceptsRefsHeadsMain()
    {
        using var workspace = TempWorkspace.Create("init-refs-heads-main");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var expected = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# refs heads marker\n");

        var result = await ScaffoldTestContext.RunInitAsync(
            source,
            target,
            refName: "refs/heads/main");
        ScaffoldTestContext.AssertSucceeded(result, "init with --ref refs/heads/main");

        ScaffoldTestContext.AssertScaffoldCommit(target, expected);
        Assert.Contains(
            "refs heads marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init peels lightweight and annotated tags to the tagged commit.</summary>
    [Fact]
    public async Task Init_AcceptsTagRef()
    {
        using var workspace = TempWorkspace.Create("init-tag-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var lightTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-light");
        var annotatedTarget = ScaffoldTestContext.CreateConsumerRepository(
            workspace,
            name: "consumer-annotated");

        var lightCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# lightweight tag marker\n");
        source.CreateLightweightTag("v-light");

        var annotatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# annotated tag marker\n");
        source.CreateAnnotatedTag("v-annotated", "annotated release");

        var annotatedObject = source.ResolveObject("refs/tags/v-annotated");
        Assert.NotEqual(annotatedCommit, annotatedObject);

        var light = await ScaffoldTestContext.RunInitAsync(
            source,
            lightTarget,
            refName: "refs/tags/v-light");
        ScaffoldTestContext.AssertSucceeded(light, "init from a lightweight tag");
        ScaffoldTestContext.AssertScaffoldCommit(lightTarget, lightCommit);
        var lightBuild = await File.ReadAllTextAsync(
            Path.Combine(lightTarget.RootPath, "build", "build.sh"));
        Assert.Contains("lightweight tag marker", lightBuild, StringComparison.Ordinal);
        Assert.DoesNotContain("annotated tag marker", lightBuild, StringComparison.Ordinal);

        var annotated = await ScaffoldTestContext.RunInitAsync(
            source,
            annotatedTarget,
            refName: "refs/tags/v-annotated");
        ScaffoldTestContext.AssertSucceeded(annotated, "init from an annotated tag");
        ScaffoldTestContext.AssertScaffoldCommit(annotatedTarget, annotatedCommit);
        Assert.NotEqual(
            annotatedObject,
            ScaffoldTestContext.ReadScaffoldCommit(annotatedTarget));
        Assert.Contains(
            "annotated tag marker",
            await File.ReadAllTextAsync(Path.Combine(annotatedTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init accepts a bare branch name and resolves it to the branch tip.</summary>
    [Fact]
    public async Task Init_AcceptsBareBranchRef()
    {
        using var workspace = TempWorkspace.Create("init-bare-branch-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        source.Repository.CreateBranch("feature");
        var expected = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare branch marker\n");

        var result = await ScaffoldTestContext.RunInitAsync(source, target, refName: "feature");
        ScaffoldTestContext.AssertSucceeded(result, "init with --ref feature");

        ScaffoldTestContext.AssertScaffoldCommit(target, expected);
        Assert.Contains(
            "bare branch marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init accepts bare lightweight and annotated tag names and peels tag objects.</summary>
    [Fact]
    public async Task Init_AcceptsBareTagRef()
    {
        using var workspace = TempWorkspace.Create("init-bare-tag-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var lightTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-light");
        var annotatedTarget = ScaffoldTestContext.CreateConsumerRepository(
            workspace,
            name: "consumer-annotated");

        var lightCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare lightweight tag marker\n");
        source.CreateLightweightTag("v-light");

        var annotatedCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# bare annotated tag marker\n");
        source.CreateAnnotatedTag("v-annotated", "bare annotated release");

        var annotatedObject = source.ResolveObject("refs/tags/v-annotated");
        Assert.NotEqual(annotatedCommit, annotatedObject);

        var light = await ScaffoldTestContext.RunInitAsync(source, lightTarget, refName: "v-light");
        ScaffoldTestContext.AssertSucceeded(light, "init with a bare lightweight tag name");
        ScaffoldTestContext.AssertScaffoldCommit(lightTarget, lightCommit);
        Assert.Contains(
            "bare lightweight tag marker",
            await File.ReadAllTextAsync(Path.Combine(lightTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        var annotated = await ScaffoldTestContext.RunInitAsync(
            source,
            annotatedTarget,
            refName: "v-annotated");
        ScaffoldTestContext.AssertSucceeded(annotated, "init with a bare annotated tag name");
        ScaffoldTestContext.AssertScaffoldCommit(annotatedTarget, annotatedCommit);
        Assert.NotEqual(annotatedObject, ScaffoldTestContext.ReadScaffoldCommit(annotatedTarget));
        Assert.Contains(
            "bare annotated tag marker",
            await File.ReadAllTextAsync(Path.Combine(annotatedTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>A bare name matching both a branch and a tag is ambiguous and names both refs.</summary>
    [Fact]
    public async Task Init_RejectsAmbiguousBareRef()
    {
        using var workspace = TempWorkspace.Create("init-ambiguous-bare-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        source.CreateLightweightTag("shared");
        source.Repository.CreateBranch("shared");

        var result = await ScaffoldTestContext.RunInitAsync(source, target, refName: "shared");
        ScaffoldTestContext.AssertFailed(result, "init with an ambiguous bare ref");
        Assert.Equal(2, result.ExitCode);
        var output = CombinedOutput(result);
        Assert.Contains("ambiguous", output, StringComparison.Ordinal);
        Assert.Contains("refs/heads/shared", output, StringComparison.Ordinal);
        Assert.Contains("refs/tags/shared", output, StringComparison.Ordinal);
        Assert.Contains("pass refs/heads/shared or refs/tags/shared", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));
    }

    /// <summary>A bare name that matches neither a branch nor a tag is not found.</summary>
    [Fact]
    public async Task Init_UnknownBareRefNotFound()
    {
        using var workspace = TempWorkspace.Create("init-unknown-bare-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunInitAsync(source, target, refName: "does-not-exist");
        ScaffoldTestContext.AssertFailed(result, "init with an unknown bare ref");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("was not found", CombinedOutput(result), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));
    }

    /// <summary>Fully qualified refs disambiguate a name that is both a branch and a tag.</summary>
    [Fact]
    public async Task Init_QualifiedRefsDisambiguate()
    {
        using var workspace = TempWorkspace.Create("init-qualified-disambiguate");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var branchTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-branch");
        var tagTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-tag");

        var tagCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# qualified tag marker\n");
        source.CreateLightweightTag("shared");

        var branchCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# qualified branch marker\n");
        source.Repository.CreateBranch("shared");
        Assert.NotEqual(tagCommit, branchCommit);

        var branch = await ScaffoldTestContext.RunInitAsync(
            source,
            branchTarget,
            refName: "refs/heads/shared");
        ScaffoldTestContext.AssertSucceeded(branch, "init with a qualified branch ref");
        ScaffoldTestContext.AssertScaffoldCommit(branchTarget, branchCommit);
        Assert.Contains(
            "qualified branch marker",
            await File.ReadAllTextAsync(Path.Combine(branchTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);

        var tag = await ScaffoldTestContext.RunInitAsync(
            source,
            tagTarget,
            refName: "refs/tags/shared");
        ScaffoldTestContext.AssertSucceeded(tag, "init with a qualified tag ref");
        ScaffoldTestContext.AssertScaffoldCommit(tagTarget, tagCommit);
        Assert.Contains(
            "qualified tag marker",
            await File.ReadAllTextAsync(Path.Combine(tagTarget.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init rejects a source lock with a poisoned reserved key before installing.</summary>
    [Fact]
    public async Task Init_RejectsPoisonedSourceLock()
    {
        var layout = RepositoryLayout.Locate();
        foreach (var poison in Enum.GetValues<ScaffoldTestContext.LockPoison>())
        {
            using var workspace = TempWorkspace.Create("init-poisoned-lock-" + poison);
            var source = await ScaffoldSource.CreateAsync(workspace, layout);
            var target = ScaffoldTestContext.CreateConsumerRepository(workspace);
            var poisonedCommit = source.CommitChange(
                "scaffold/build/plutonium.lock",
                text => ScaffoldTestContext.ApplyLockPoison(text, poison));
            var before = DirectorySnapshot.Capture(target.RootPath, ".git", ".test-home");

            var result = await ScaffoldTestContext.RunScriptAsync(
                source.InitScriptPath,
                [
                    "--source",
                    source.SourcePath,
                    "--sha",
                    poisonedCommit,
                    "--target",
                    target.RootPath,
                ],
                target.RootPath);

            ScaffoldTestContext.AssertFailed(result, "init with a poisoned source lock (" + poison + ")");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                ScaffoldTestContext.LockPoisonMessage(poison),
                CombinedOutput(result),
                StringComparison.Ordinal);
            Assert.False(
                Directory.Exists(Path.Combine(target.RootPath, "build")),
                "Init installed a scaffold from a poisoned source lock (" + poison + ").");
            var differences = before.Differences(
                DirectorySnapshot.Capture(target.RootPath, ".git", ".test-home"));
            Assert.True(
                differences.Count == 0,
                "Init changed the target for a poisoned source lock (" + poison + "): " +
                string.Join(", ", differences));
        }
    }

    /// <summary>Init accepts a full commit SHA through both <c>--ref</c> and <c>--sha</c>.</summary>
    [Fact]
    public async Task Init_AcceptsFullShaRef()
    {
        using var workspace = TempWorkspace.Create("init-full-sha-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var refTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-ref");
        var shaTarget = ScaffoldTestContext.CreateConsumerRepository(workspace, name: "consumer-sha");

        var commit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# full sha marker\n");
        var upper = commit.ToUpperInvariant();

        var viaRef = await ScaffoldTestContext.RunInitAsync(source, refTarget, refName: upper);
        ScaffoldTestContext.AssertSucceeded(viaRef, "init with --ref <sha>");
        ScaffoldTestContext.AssertScaffoldCommit(refTarget, commit);

        var viaSha = await ScaffoldTestContext.RunInitAsync(source, shaTarget, sha: upper);
        ScaffoldTestContext.AssertSucceeded(viaSha, "init with --sha <sha>");
        ScaffoldTestContext.AssertScaffoldCommit(shaTarget, commit);

        foreach (var target in new[] { refTarget, shaTarget })
        {
            Assert.Contains(
                "full sha marker",
                await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
                StringComparison.Ordinal);
        }
    }

    /// <summary>Init refuses <c>--sha</c> and <c>--ref</c> together.</summary>
    [Fact]
    public async Task Init_RejectsShaAndRefTogether()
    {
        using var workspace = TempWorkspace.Create("init-sha-and-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var result = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--sha", source.HeadSha, "--ref", "main"],
            target.RootPath);

        ScaffoldTestContext.AssertFailed(result, "init with --sha and --ref");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("mutually exclusive", CombinedOutput(result), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));
    }

    /// <summary>Init rejects every malformed ref shape before fetching.</summary>
    [Fact]
    public async Task Init_RejectsMalformedRef()
    {
        using var workspace = TempWorkspace.Create("init-malformed-ref");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        foreach (var value in MalformedRefs)
        {
            var result = await ScaffoldTestContext.RunInitAsync(source, target, refName: value);
            ScaffoldTestContext.AssertFailed(result, "init with malformed ref '" + value + "'");
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("--ref must be", CombinedOutput(result), StringComparison.Ordinal);
            Assert.False(
                File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")),
                "Init installed a scaffold for malformed ref '" + value + "'.");
        }
    }

    /// <summary>Init rejects an option-shaped ref without invoking git.</summary>
    [Fact]
    public async Task Init_RejectsOptionInjection()
    {
        using var workspace = TempWorkspace.Create("init-option-injection");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);
        var marker = workspace.PathFor("git-invoked");
        var toolchain = ScaffoldTestContext.CreateGitInvocationProbe(workspace, marker);

        var result = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            [
                "--source",
                source.SourcePath,
                "--ref",
                "--upload-pack=" + ScaffoldTestContext.ToBashPath(marker),
                "--target",
                target.RootPath,
            ],
            target.RootPath,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = toolchain.BuildPath(),
            });

        ScaffoldTestContext.AssertFailed(result, "init with an option-shaped ref");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--ref must be", CombinedOutput(result), StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "Init invoked git before rejecting the option-shaped ref.");
        Assert.False(File.Exists(Path.Combine(target.RootPath, "build", "build.slnx")));
    }

    /// <summary>The default ref prefers the main branch over a same-named shadow tag.</summary>
    [Fact]
    public async Task Init_DefaultUsesBranchOverShadowTag()
    {
        using var workspace = TempWorkspace.Create("init-shadow-tag");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var shadowCommit = source.HeadSha;
        source.CreateLightweightTag("main");
        var branchCommit = source.CommitChange(
            "scaffold/build/build.sh",
            text => text + "\n# branch main marker\n");
        Assert.NotEqual(shadowCommit, branchCommit);

        var result = await ScaffoldTestContext.RunInitAsync(source, target, useDefaultRef: true);
        ScaffoldTestContext.AssertSucceeded(result, "init with a shadow main tag");

        ScaffoldTestContext.AssertScaffoldCommit(target, branchCommit);
        Assert.Contains(
            "branch main marker",
            await File.ReadAllTextAsync(Path.Combine(target.RootPath, "build", "build.sh")),
            StringComparison.Ordinal);
    }

    /// <summary>Init passes the source and commit after the git option terminator.</summary>
    [Fact]
    public async Task Init_PassesRefspecAfterOptionTerminator()
    {
        using var workspace = TempWorkspace.Create("init-option-terminator");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);
        var realGit = await ScaffoldTestContext.ResolveGitPathAsync(workspace);
        var logPath = workspace.PathFor("git.log");
        var toolchain = ScaffoldTestContext.CreateLoggingGitShim(workspace, realGit, logPath);

        var result = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--ref", "main", "--target", target.RootPath],
            target.RootPath,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = toolchain.BuildPath(),
            });

        ScaffoldTestContext.AssertSucceeded(result, "init through a logging git shim");
        Assert.True(File.Exists(logPath), "The logging git shim did not record an invocation.");
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains(
            "ls-remote -- " + ScaffoldTestContext.ToBashPath(source.SourcePath),
            log,
            StringComparison.Ordinal);
        Assert.Contains(
            "fetch --depth 1 --no-tags origin -- " + source.HeadSha,
            log,
            StringComparison.Ordinal);
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
