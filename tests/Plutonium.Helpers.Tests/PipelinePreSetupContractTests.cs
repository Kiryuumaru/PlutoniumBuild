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
/// Pre-setup contract across pipelines: CONDITION plus BUILD_ID and HAS_RELEASE live outputs
/// with a payload reference, union needs on PRE_SETUP, always-exported BUILD_ID, and a false
/// CONDITION for entries without a setup. Probed via public workflow and finalize entry points.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PipelinePreSetupContractTests
{
    /// <summary>
    /// Verifies the GitHub PRE_SETUP outputs carry the payload, CONDITION, BUILD_ID, and
    /// HAS_RELEASE references without static runs-on, run-script, cache, or checkout keys.
    /// </summary>
    [Fact]
    public async Task GitHubPreSetupOutputs_HaveConditionBuildIdHasReleaseAndPayloadRef()
    {
        var entries = CreateEntries();
        var context = CreateContext(PlutoniumPipelineType.GitHub, entries);

        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context);

        var outputs = result.Document.Jobs[0].Outputs;
        Assert.Equal("PRE_SETUP", result.Document.Jobs[0].Id);
        Assert.Contains(
            "${{ steps.PRE_SETUP_RUN.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName) + " }}",
            outputs[PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName)],
            StringComparison.Ordinal);
        Assert.Contains(
            "${{ steps.PRE_SETUP_RUN.outputs." + PlutoniumPipelineContract.ConditionOutputName("my-job") + " }}",
            outputs[PlutoniumPipelineContract.ConditionOutputName("my-job")],
            StringComparison.Ordinal);
        Assert.True(outputs.ContainsKey(PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID")));
        Assert.True(outputs.ContainsKey(PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE")));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("RUNS_ON", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("RUN_SCRIPT", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("CACHE", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("CHECKOUT", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the Azure PRE_SETUP outputs carry the payload, CONDITION, BUILD_ID, and
    /// HAS_RELEASE references without static pool, run-script, cache, or checkout keys.
    /// </summary>
    [Fact]
    public async Task AzurePreSetupOutputs_HaveConditionBuildIdHasReleaseAndPayloadRef()
    {
        var entries = CreateEntries();
        var context = CreateContext(PlutoniumPipelineType.Azure, entries);

        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(context);

        var outputs = result.Document.Jobs[0].Outputs;
        Assert.Equal("PRE_SETUP", result.Document.Jobs[0].Id);
        Assert.Contains(
            PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName),
            outputs[PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName)],
            StringComparison.Ordinal);
        Assert.Contains(
            PlutoniumPipelineContract.ConditionOutputName("my-job"),
            outputs[PlutoniumPipelineContract.ConditionOutputName("my-job")],
            StringComparison.Ordinal);
        Assert.True(outputs.ContainsKey(PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID")));
        Assert.True(outputs.ContainsKey(PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE")));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("RUNS_ON", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("RUN_SCRIPT", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("CACHE", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs.Keys, key => key.Contains("CHECKOUT", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies every GitHub entry and post-setup job needs PRE_SETUP first.
    /// </summary>
    [Fact]
    public async Task GitHubEntryJobs_RequirePreSetup()
    {
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries());

        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context);

        foreach (var job in result.Document.Jobs.Where(job => !job.Id.Equals("PRE_SETUP", StringComparison.Ordinal)))
        {
            Assert.Contains("PRE_SETUP", job.Needs);
        }
    }

    /// <summary>
    /// Verifies every Azure entry and post-setup job needs PRE_SETUP first.
    /// </summary>
    [Fact]
    public async Task AzureEntryJobs_RequirePreSetup()
    {
        var context = CreateContext(PlutoniumPipelineType.Azure, CreateEntries());

        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(context);

        foreach (var job in result.Document.Jobs.Where(job => !job.Id.Equals("PRE_SETUP", StringComparison.Ordinal)))
        {
            Assert.Contains("PRE_SETUP", job.Needs);
        }
    }

    /// <summary>
    /// Verifies finalize exports the BUILD_ID value and the matching HAS_RELEASE flag.
    /// </summary>
    /// <param name="azure">True for the Azure pipeline; otherwise the GitHub pipeline.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalize_ExportsBuildIdValue(bool azure)
    {
        var context = CreateContext(azure ? PlutoniumPipelineType.Azure : PlutoniumPipelineType.GitHub, CreateEntries());
        var preSetup = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            TriggerType = PlutoniumTriggerType.Commit,
            Environment = "main",
            BuildId = "20240101120000.abc1234",
            HasRelease = true,
            BuildEntries = ["my-job"],
        };
        var buildIdName = PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID");
        var hasReleaseName = PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE");

        try
        {
            await Pipeline(azure).FinalizePreSetupAsync(context, preSetup);

            Assert.Equal("20240101120000.abc1234", Environment.GetEnvironmentVariable(buildIdName));
            Assert.Equal("true", Environment.GetEnvironmentVariable(hasReleaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(buildIdName, null);
            Environment.SetEnvironmentVariable(hasReleaseName, null);
            ClearSharedExports();
        }
    }

    /// <summary>
    /// Verifies a null BUILD_ID is always exported as an empty value through the CI channel
    /// downstream jobs consume: the GitHub Actions output file or the Azure Pipelines
    /// <c>##vso[task.setvariable]</c> line. The process environment keeps the empty value
    /// only on .NET 9 and later.
    /// </summary>
    /// <param name="azure">True for the Azure pipeline; otherwise the GitHub pipeline.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalize_ExportsEmptyBuildIdWhenNull(bool azure)
    {
        var context = CreateContext(azure ? PlutoniumPipelineType.Azure : PlutoniumPipelineType.GitHub, CreateEntries());
        var preSetup = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            TriggerType = PlutoniumTriggerType.Commit,
            Environment = "main",
            BuildId = null!,
            HasRelease = false,
            BuildEntries = ["my-job"],
        };
        var buildIdName = PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID");
        var githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        var githubOutput = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        var tfBuild = Environment.GetEnvironmentVariable("TF_BUILD");
        var outputFile = Path.Combine(
            Path.GetTempPath(), "plutonium-pipeline-output-" + Guid.NewGuid().ToString("N"));
        var originalOut = Console.Out;
        var stdout = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", azure ? null : "true");
            Environment.SetEnvironmentVariable("TF_BUILD", azure ? "True" : null);
            Environment.SetEnvironmentVariable("GITHUB_OUTPUT", outputFile);
            if (azure)
            {
                Console.SetOut(stdout);
            }

            await Pipeline(azure).FinalizePreSetupAsync(context, preSetup);

            if (azure)
            {
                var lines = stdout.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                Assert.Contains($"##vso[task.setvariable variable={buildIdName};isOutput=true]", lines);
            }
            else
            {
                Assert.True(File.Exists(outputFile), "GitHub Actions output file was not written.");
                Assert.Contains($"{buildIdName}=", File.ReadAllLines(outputFile));
            }

            // Verified semantics (Microsoft Learn breaking change, .NET 9 "Support for empty
            // environment variables"; true .NET 8.0.32 on Linux observed a deleted variable):
            // before .NET 9, SetEnvironmentVariable(name, string.Empty) deletes the variable on
            // all supported platforms; from .NET 9 it keeps an empty value, while null still
            // deletes. The channel assertion above stays runtime-independent.
            var exportedValue = Environment.GetEnvironmentVariable(buildIdName);
            if (Environment.Version.Major >= 9)
            {
                Assert.Equal(string.Empty, exportedValue);
            }
            else
            {
                Assert.Null(exportedValue);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", githubActions);
            Environment.SetEnvironmentVariable("GITHUB_OUTPUT", githubOutput);
            Environment.SetEnvironmentVariable("TF_BUILD", tfBuild);
            Environment.SetEnvironmentVariable(buildIdName, null);
            ClearSharedExports();
            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
        }
    }

    /// <summary>
    /// Verifies an entry without a setup exports a false CONDITION while a setup with a true
    /// condition exports true.
    /// </summary>
    /// <param name="azure">True for the Azure pipeline; otherwise the GitHub pipeline.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalize_ExportsFalseConditionWhenSetupMissing(bool azure)
    {
        var context = CreateContext(azure ? PlutoniumPipelineType.Azure : PlutoniumPipelineType.GitHub, CreateEntries());
        var preSetup = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            TriggerType = PlutoniumTriggerType.Commit,
            Environment = "main",
            BuildId = "20240101120000.abc1234",
            BuildEntries = ["my-job", "my-publish"],
            EntrySetupMap = new Dictionary<string, PlutoniumEntrySetup>(StringComparer.Ordinal)
            {
                ["my-job"] = new PlutoniumEntrySetup { Id = "my-job", Condition = true },
            },
        };
        var presentName = PlutoniumPipelineContract.ConditionOutputName("my-job");
        var missingName = PlutoniumPipelineContract.ConditionOutputName("my-publish");

        try
        {
            await Pipeline(azure).FinalizePreSetupAsync(context, preSetup);

            Assert.Equal("true", Environment.GetEnvironmentVariable(presentName));
            Assert.Equal("false", Environment.GetEnvironmentVariable(missingName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(presentName, null);
            Environment.SetEnvironmentVariable(missingName, null);
            ClearSharedExports();
        }
    }

    private static IPlutoniumPipeline Pipeline(bool azure) =>
        azure ? new PlutoniumAzurePipeline() : new PlutoniumGitHubPipeline();

    private static PlutoniumAllEntries CreateEntries()
    {
        var buildEntry = new BuildEntryDefinition();
        buildEntry.Id = "my-job";
        buildEntry.AppIds = [new PlutoniumAppId("my-app")];
        var publishEntry = new PublishEntryDefinition();
        publishEntry.Id = "my-publish";
        publishEntry.AppIds = [new PlutoniumAppId("my-app")];
        return new PlutoniumAllEntries(
            new Dictionary<string, PlutoniumAppEntries>(StringComparer.Ordinal),
            new WorkflowConfigEntryDefinition(),
            new Dictionary<string, ITestEntryDefinition>(StringComparer.Ordinal),
            new Dictionary<string, IBuildEntryDefinition>(StringComparer.Ordinal) { ["my-job"] = buildEntry },
            new Dictionary<string, IPublishEntryDefinition>(StringComparer.Ordinal) { ["my-publish"] = publishEntry },
            new Dictionary<string, IRunEntryDefinition>(StringComparer.Ordinal)
            {
                ["my-job"] = buildEntry,
                ["my-publish"] = publishEntry,
            });
    }

    private static PlutoniumPipelineContext CreateContext(PlutoniumPipelineType pipelineType, PlutoniumAllEntries entries)
    {
        var logger = new PlutoniumLogger([new NullSink()]);
        var masker = new PlutoniumSecretMasker();
        var host = new PlutoniumHost(logger);
        var runner = new PlutoniumProcessRunner(logger);
        var services = PlutoniumBuildServices.Create(runner, logger, masker, host);
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath("/tmp/plutonium-repo"),
            new PlutoniumPath("/tmp/plutonium-temp"),
            new PlutoniumPath("/tmp/plutonium-out"),
            new PlutoniumPath("/tmp/plutonium-cache"),
            "https://example.test/org/repo.git");
        return new PlutoniumPipelineContext(new object(), options, services, pipelineType) { Entries = entries };
    }

    private static void ClearSharedExports()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName), null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID"), null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE"), null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.ConditionOutputName("my-job"), null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.ConditionOutputName("my-publish"), null);
    }

    private sealed class NullSink : IPlutoniumLogSink
    {
        public void Write(PlutoniumLogLevel level, string? scope, string message)
        {
        }
    }
}
