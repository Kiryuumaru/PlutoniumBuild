using System.Text.RegularExpressions;
using Xunit;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Tests;

/// <summary>
/// Generated GitHub workflow security contract: read-only default permissions,
/// write permission and the pipeline token only on the tag/release jobs, declared
/// secrets scoped to the entry jobs that need them, pinned action refs, and a safe
/// workflow file name.
/// </summary>
public sealed class WorkflowSecurityContractTests
{
    /// <summary>
    /// Verifies no token or declared secret is exported workflow-wide, the top-level
    /// permission is read-only, only pre/post-setup can write, and entry jobs carry
    /// their declared secrets without the token or persisted credentials.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflow_UsesReadOnlyDefaultAndJobScopedSecrets()
    {
        var entries = CreateEntries("Plutonium Build");
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new UnscopedSecretBuild(), entries, root);
            var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context);

            Assert.Empty(result.Document.Env);
            Assert.Equal("contents: read", result.SecondBlock);

            var preSetup = Job(result, "PRE_SETUP");
            var postSetup = Job(result, "POST_SETUP");
            Assert.Equal("write", preSetup.Permissions["contents"]);
            Assert.Equal("write", postSetup.Permissions["contents"]);
            Assert.Equal("${{ secrets.GITHUB_TOKEN }}", preSetup.Env["GITHUB_TOKEN"]);
            Assert.Equal("${{ secrets.GITHUB_TOKEN }}", postSetup.Env["GITHUB_TOKEN"]);
            Assert.Equal("${{ secrets.MY_TOKEN }}", preSetup.Env["PLUTONIUM_MY_TOKEN"]);
            Assert.Equal("${{ secrets.MY_TOKEN }}", postSetup.Env["PLUTONIUM_MY_TOKEN"]);
            Assert.Contains("persist-credentials: true", preSetup.Steps[0], StringComparison.Ordinal);
            Assert.Contains("persist-credentials: true", postSetup.Steps[0], StringComparison.Ordinal);

            foreach (var entryId in new[] { "my-test", "my-job", "my-publish" })
            {
                var job = Job(result, entryId);
                Assert.Empty(job.Permissions);
                Assert.False(job.Env.ContainsKey("GITHUB_TOKEN"));
                Assert.Equal("${{ secrets.MY_TOKEN }}", job.Env["PLUTONIUM_MY_TOKEN"]);
                Assert.Contains("persist-credentials: false", job.Steps[0], StringComparison.Ordinal);
            }

            var yaml = PlutoniumGitHubEmitter.Render(result.Document, result.FirstBlock, result.SecondBlock);
            Assert.DoesNotContain("env:\n  GITHUB_TOKEN", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("env:\n  PLUTONIUM_MY_TOKEN", yaml, StringComparison.Ordinal);
            Assert.Contains("permissions:\n  contents: read", yaml, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies a secret scoped to the publish entry is absent from build and test jobs.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflow_ScopesPublishSecretToPublishEntry()
    {
        var entries = CreateEntries("Plutonium Build");
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new PublishScopedSecretBuild(), entries, root);
            var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context);

            Assert.Equal("${{ secrets.NUGET_KEY }}", Job(result, "my-publish").Env["PLUTONIUM_NUGET_KEY"]);
            Assert.False(Job(result, "my-job").Env.ContainsKey("PLUTONIUM_NUGET_KEY"));
            Assert.False(Job(result, "my-test").Env.ContainsKey("PLUTONIUM_NUGET_KEY"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies a secret scoped to an undiscovered entry fails generation instead of
    /// silently disappearing from every job.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflow_RejectsSecretScopeForUnknownEntry()
    {
        var entries = CreateEntries("Plutonium Build");
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new UnknownScopedSecretBuild(), entries, root);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PlutoniumGitHubPipeline().BuildWorkflowAsync(context));

            Assert.Contains("Secret 'NUGET_KEY'", failure.Message, StringComparison.Ordinal);
            Assert.Contains("unknown entry 'ghost'", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies the emitted workflow pins every action to a 40-hex commit with a
    /// version comment and contains no mutable <c>@vN</c> action refs.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflow_PinsActionsToCommitShas()
    {
        var entries = CreateEntries("Plutonium Build");
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new UnscopedSecretBuild(), entries, root);
            var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context);
            var yaml = PlutoniumGitHubEmitter.Render(result.Document, result.FirstBlock, result.SecondBlock);

            Assert.Contains("uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0", yaml, StringComparison.Ordinal);
            Assert.False(
                Regex.IsMatch(yaml, @"uses:\s+\S+@v\d", RegexOptions.CultureInvariant),
                "Emitted workflow contains a mutable action version ref.");
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies workflow names that could escape or nest the workflows directory are
    /// rejected before any file is written.
    /// </summary>
    /// <param name="name">The unsafe workflow name.</param>
    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("a.b")]
    [InlineData("nested/name")]
    [InlineData("windows\\name")]
    public async Task GitHubWorkflowName_RejectsUnsafeNames(string name)
    {
        var entries = CreateEntries(name);
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new object(), entries, root);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context));

            Assert.Contains("not a safe workflow file name", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
            Assert.False(File.Exists(Path.Combine(root, "escape.yml")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape.yml")));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies an absolute workflow name is rejected before any file is written.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflowName_RejectsAbsoluteName()
    {
        var entries = CreateEntries(Path.Combine(Path.GetTempPath(), "plutonium-escape"));
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new object(), entries, root);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context));

            Assert.Contains("not a safe workflow file name", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    /// <summary>
    /// Verifies a normal workflow name is lowercased and space-normalized, and the
    /// written path stays under <c>.github/workflows</c>.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflowName_NormalizesSpaceAndWritesUnderWorkflows()
    {
        var entries = CreateEntries("Plutonium Build");
        var root = CreateTemporaryRoot();
        try
        {
            var context = CreateContext(new object(), entries, root);

            var written = await PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context);

            var expected = Path.GetFullPath(Path.Combine(root, ".github", "workflows", "plutonium-build.yml"));
            Assert.Equal(expected, Path.GetFullPath(written.Value));
            Assert.True(File.Exists(expected));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static PlutoniumWorkflowJob Job(PlutoniumWorkflowBuildResult result, string id) =>
        result.Document.Jobs.Single(job => job.Id == id);

    private static PlutoniumPipelineContext CreateContext(object build, PlutoniumAllEntries entries, string root)
    {
        var logger = new PlutoniumLogger([new NullSink()]);
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger), logger, new PlutoniumSecretMasker(), new PlutoniumHost(logger));
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(root),
            new PlutoniumPath(Path.Combine(root, "temp")),
            new PlutoniumPath(Path.Combine(root, "out")),
            new PlutoniumPath(Path.Combine(root, "cache")),
            "https://example.test/org/repo.git");
        return new PlutoniumPipelineContext(build, options, services, PlutoniumPipelineType.GitHub) { Entries = entries };
    }

    private static PlutoniumAllEntries CreateEntries(string workflowName)
    {
        var config = new WorkflowConfigEntryDefinition();
        config.Name(workflowName);
        var test = new TestEntryDefinition();
        test.Id = "my-test";
        test.AppIds = [new PlutoniumAppId("my-app")];
        test.ExecuteBeforeBuild(true);
        var build = new BuildEntryDefinition();
        build.Id = "my-job";
        build.AppIds = [new PlutoniumAppId("my-app")];
        var publish = new PublishEntryDefinition();
        publish.Id = "my-publish";
        publish.AppIds = [new PlutoniumAppId("my-app")];
        return new PlutoniumAllEntries(
            new Dictionary<string, PlutoniumAppEntries>(StringComparer.Ordinal),
            config,
            new Dictionary<string, ITestEntryDefinition>(StringComparer.Ordinal) { ["my-test"] = test },
            new Dictionary<string, IBuildEntryDefinition>(StringComparer.Ordinal) { ["my-job"] = build },
            new Dictionary<string, IPublishEntryDefinition>(StringComparer.Ordinal) { ["my-publish"] = publish },
            new Dictionary<string, IRunEntryDefinition>(StringComparer.Ordinal)
            {
                ["my-test"] = test,
                ["my-job"] = build,
                ["my-publish"] = publish,
            });
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "plutonium-workflow-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class UnscopedSecretBuild
    {
        [PlutoniumSecretVariable("MY_TOKEN")]
        public string? Token { get; set; }
    }

    private sealed class PublishScopedSecretBuild
    {
        [PlutoniumSecretVariable("NUGET_KEY", EntryIds = new[] { "my-publish" })]
        public string? Key { get; set; }
    }

    private sealed class UnknownScopedSecretBuild
    {
        [PlutoniumSecretVariable("NUGET_KEY", EntryIds = new[] { "ghost" })]
        public string? Key { get; set; }
    }

    private sealed class NullSink : IPlutoniumLogSink
    {
        public void Write(PlutoniumLogLevel level, string? scope, string message)
        {
        }
    }
}
