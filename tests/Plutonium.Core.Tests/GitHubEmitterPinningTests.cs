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

namespace Plutonium.Core.Tests;

/// <summary>
/// Verifies the GitHub emitter pins every third-party action to an immutable
/// 40-hex commit with a version comment and refuses workflow file names that
/// resolve outside <c>.github/workflows</c>.
/// </summary>
public sealed class GitHubEmitterPinningTests
{
    /// <summary>
    /// Verifies every emitted action reference is pinned to a 40-hex commit with a
    /// version comment, with no mutable <c>@vN</c> refs.
    /// </summary>
    [Fact]
    public void ActionSteps_PinEveryActionToCommitSha()
    {
        var steps = new[]
        {
            PlutoniumGitHubEmitter.CheckoutStep("0", fetchTags: true, submodules: "recursive"),
            PlutoniumGitHubEmitter.CacheStep("cache", "key", []),
            PlutoniumGitHubEmitter.UploadArtifactStep("artifact", "path"),
            PlutoniumGitHubEmitter.DownloadArtifactStep("artifact", "path"),
        };

        foreach (var step in steps)
        {
            var uses = step.Split('\n')[0];
            Assert.Matches(@"^- uses: actions/[a-z-]+@[0-9a-f]{40} # v\d+\.\d+\.\d+$", uses);
            Assert.DoesNotContain("@v4", uses, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies the checkout step emits the persisted-credentials flag so entry jobs
    /// can disable the checkout token.
    /// </summary>
    /// <param name="persistCredentials">Whether credentials must persist.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckoutStep_RendersPersistCredentials(bool persistCredentials)
    {
        var step = PlutoniumGitHubEmitter.CheckoutStep(
            "0",
            fetchTags: false,
            submodules: "false",
            persistCredentials: persistCredentials);

        Assert.Contains(
            $"persist-credentials: {(persistCredentials ? "true" : "false")}",
            step,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a workflow file name that resolves outside the workflows directory is
    /// rejected before any directory or file is created.
    /// </summary>
    /// <param name="fileName">The unsafe workflow file name.</param>
    [Theory]
    [InlineData("../escape")]
    [InlineData("../../escape")]
    public void WriteWorkflow_RejectsNamesOutsideWorkflows(string fileName)
    {
        var root = new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-emitter-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root.Value);
        try
        {
            var document = new PlutoniumWorkflowDocument(
                "Pipe",
                "push:",
                [new PlutoniumWorkflowJob("J", "ubuntu-latest", ["- run: echo"])]);

            Assert.Throws<ArgumentException>(() => PlutoniumGitHubEmitter.WriteWorkflow(root, fileName, document));
            Assert.False(Directory.Exists(Path.Combine(root.Value, ".github")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root.Value)!, "escape.yml")));
        }
        finally
        {
            Directory.Delete(root.Value, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an absolute workflow file name is rejected before any file is written.
    /// </summary>
    [Fact]
    public void WriteWorkflow_RejectsAbsoluteName()
    {
        var root = new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-emitter-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root.Value);
        try
        {
            var document = new PlutoniumWorkflowDocument(
                "Pipe",
                "push:",
                [new PlutoniumWorkflowJob("J", "ubuntu-latest", ["- run: echo"])]);
            var absolute = Path.Combine(Path.GetTempPath(), "plutonium-escape-" + Guid.NewGuid().ToString("N"));

            Assert.Throws<ArgumentException>(() => PlutoniumGitHubEmitter.WriteWorkflow(root, absolute, document));
            Assert.False(Directory.Exists(Path.Combine(root.Value, ".github")));
            Assert.False(File.Exists(absolute + ".yml"));
        }
        finally
        {
            Directory.Delete(root.Value, recursive: true);
        }
    }
}
