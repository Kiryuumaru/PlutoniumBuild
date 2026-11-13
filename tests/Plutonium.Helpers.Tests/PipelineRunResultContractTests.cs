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
/// Run-result contract: sanitized plain and host result variable names, POST_SETUP
/// job injection of every raw host result plus an always-run condition, and the
/// GitHub/Azure normalization that turns host-specific failure results into the
/// single <c>error</c> value post-setup consumes.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PipelineRunResultContractTests : IDisposable
{
    private readonly Dictionary<string, string?> _backup = new(StringComparer.Ordinal);

    /// <summary>Clears the run-result variables used by these tests and backs up prior values.</summary>
    public PipelineRunResultContractTests()
    {
        foreach (var name in RunResultVariableNames())
        {
            _backup[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var pair in _backup)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    /// <summary>Verifies plain result names uppercase and sanitize the entry id.</summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="expected">The expected variable name.</param>
    [Theory]
    [InlineData("my-job", "PLUTONIUM_RUN_RESULT_MY_JOB")]
    [InlineData("my_job", "PLUTONIUM_RUN_RESULT_MY_JOB")]
    [InlineData("Job1", "PLUTONIUM_RUN_RESULT_JOB1")]
    [InlineData("publish.linux", "PLUTONIUM_RUN_RESULT_PUBLISH_LINUX")]
    [InlineData("_job", "PLUTONIUM_RUN_RESULT_JOB")]
    public void RunResultVariableName_UppercasesAndSanitizes(string entryId, string expected)
    {
        Assert.Equal(expected, PlutoniumPipelineContract.RunResultVariableName(entryId));
        Assert.Equal(
            "PLUTONIUM_RUN_RESULT_GITHUB_" + expected["PLUTONIUM_RUN_RESULT_".Length..],
            PlutoniumPipelineContract.HostRunResultVariableName("github", entryId));
    }

    /// <summary>Verifies a name with no letter or digit is rejected.</summary>
    [Fact]
    public void RunResultVariableName_RejectsNamesWithoutAlphanumerics()
    {
        Assert.Throws<ArgumentException>(() => PlutoniumPipelineContract.RunResultVariableName("---"));
        Assert.Throws<ArgumentException>(() => PlutoniumPipelineContract.HostRunResultVariableName("GITHUB", " "));
    }

    /// <summary>Verifies the GitHub POST_SETUP job injects every raw result and runs even after a failure.</summary>
    [Fact]
    public async Task GitHubPostSetupJob_InjectsRawResultsAndRunsOnFailure()
    {
        var entries = CreateEntries();

        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.GitHub, entries));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "POST_SETUP");
        Assert.Equal("success() || failure() || always()", job.IfCondition);
        foreach (var entryId in entries.RunEntries.Keys)
        {
            Assert.Equal(
                "${{ needs." + entryId + ".result }}",
                job.Env[PlutoniumPipelineContract.HostRunResultVariableName("GITHUB", entryId)]);
            Assert.False(job.Env.ContainsKey(PlutoniumPipelineContract.RunResultVariableName(entryId)));
        }

        Assert.Contains("needs.PRE_SETUP.outputs.", job.Env[PlutoniumPipelineContract.PayloadVariableName], StringComparison.Ordinal);
    }

    /// <summary>Verifies the Azure POST_SETUP stage injects every raw stage result and runs even after a failure.</summary>
    [Fact]
    public async Task AzurePostSetupJob_InjectsRawResultsAndRunsOnFailure()
    {
        var entries = CreateEntries();

        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.Azure, entries));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "POST_SETUP");
        Assert.Equal("or(succeeded(), failed())", job.IfCondition);
        Assert.Equal(
            "$[ stageDependencies.MY_JOB.MY_JOB.result ]",
            job.Env[PlutoniumPipelineContract.HostRunResultVariableName("AZURE", "my-job")]);
        Assert.Equal(
            "$[ stageDependencies.MY_PUBLISH.MY_PUBLISH.result ]",
            job.Env[PlutoniumPipelineContract.HostRunResultVariableName("AZURE", "my-publish")]);
        foreach (var entryId in entries.RunEntries.Keys)
        {
            Assert.False(job.Env.ContainsKey(PlutoniumPipelineContract.RunResultVariableName(entryId)));
        }

        Assert.Equal(
            "$[ stageDependencies.PRE_SETUP.PRE_SETUP.outputs['PreSetup.PRE_SETUP_PLUTONIUM_PRE_SETUP'] ]",
            job.Env[PlutoniumPipelineContract.PayloadVariableName]);

        var entryJob = result.Document.Jobs.Single(candidate => candidate.Id == "my-job");
        Assert.Equal(
            "$[ stageDependencies.PRE_SETUP.PRE_SETUP.outputs['PreSetup.PRE_SETUP_PLUTONIUM_PRE_SETUP'] ]",
            entryJob.Env[PlutoniumPipelineContract.PayloadVariableName]);
        var condition = "$[ eq(stageDependencies.PRE_SETUP.PRE_SETUP.outputs['PreSetup.MY_JOB_CONDITION'], 'true') ]";
        Assert.Contains(
            entryJob.Steps,
            step => step.Contains("  condition: " + condition, StringComparison.Ordinal));
    }

    /// <summary>Verifies GitHub normalization maps failure and cancellation to <c>error</c>.</summary>
    /// <param name="raw">The raw GitHub job result.</param>
    /// <param name="expected">The normalized plain result.</param>
    [Theory]
    [InlineData("success", "success")]
    [InlineData("failure", "error")]
    [InlineData("cancelled", "error")]
    [InlineData("skipped", "skipped")]
    [InlineData("", "")]
    public async Task GitHubPreparePostSetup_NormalizesHostResults(string raw, string expected)
    {
        var entries = CreateEntries();
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.HostRunResultVariableName("GITHUB", "my-job"), raw);

        await new PlutoniumGitHubPipeline().PreparePostSetupAsync(
            CreateContext(PlutoniumPipelineType.GitHub, entries));

        AssertNormalizedResult(
            expected,
            Environment.GetEnvironmentVariable(PlutoniumPipelineContract.RunResultVariableName("my-job")));
    }

    /// <summary>Verifies Azure normalization maps every documented stage result.</summary>
    /// <param name="raw">The raw Azure stage result.</param>
    /// <param name="expected">The normalized plain result.</param>
    [Theory]
    [InlineData("Succeeded", "success")]
    [InlineData("SucceededWithIssues", "error")]
    [InlineData("Skipped", "skipped")]
    [InlineData("Failed", "error")]
    [InlineData("Canceled", "error")]
    [InlineData("", "")]
    public async Task AzurePreparePostSetup_NormalizesHostResults(string raw, string expected)
    {
        var entries = CreateEntries();
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.HostRunResultVariableName("AZURE", "my-job"), raw);

        await new PlutoniumAzurePipeline().PreparePostSetupAsync(
            CreateContext(PlutoniumPipelineType.Azure, entries));

        AssertNormalizedResult(
            expected,
            Environment.GetEnvironmentVariable(PlutoniumPipelineContract.RunResultVariableName("my-job")));
    }

    private static void AssertNormalizedResult(string expected, string? actual)
    {
        if (expected.Length == 0)
        {
            // .NET 8 deletes an environment variable when its value is empty; both null and "" satisfy the empty contract.
            Assert.True(
                string.IsNullOrEmpty(actual),
                $"Expected an empty normalized result, but observed '{actual ?? "<null>"}'.");
            return;
        }

        Assert.Equal(expected, actual);
    }

    private static IEnumerable<string> RunResultVariableNames()
    {
        foreach (var entryId in new[] { "my-job", "my-publish" })
        {
            yield return PlutoniumPipelineContract.RunResultVariableName(entryId);
            yield return PlutoniumPipelineContract.HostRunResultVariableName("GITHUB", entryId);
            yield return PlutoniumPipelineContract.HostRunResultVariableName("AZURE", entryId);
        }
    }

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
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger), logger, new PlutoniumSecretMasker(), new PlutoniumHost(logger));
        var unique = Guid.NewGuid().ToString("N");
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-run-result-repo-" + unique)),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-run-result-temp-" + unique)),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-run-result-out-" + unique)),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-run-result-cache-" + unique)),
            "https://example.test/org/repo.git");
        return new PlutoniumPipelineContext(new object(), options, services, pipelineType) { Entries = entries };
    }

    private sealed class NullSink : IPlutoniumLogSink
    {
        public void Write(PlutoniumLogLevel level, string? scope, string message)
        {
        }
    }
}
