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
/// Pipeline invocation contract: the composed shell command lines emitted into
/// generated workflow files, the entry selector job variable, and entry-run
/// payload reconstruction plus failure diagnostics. Emitted lines are pinned as
/// exact strings so a regression to the bare non-shell invocations fails.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PipelineInvocationContractTests : IDisposable
{
    private readonly string? _entryVariableBackup;
    private readonly string? _payloadVariableBackup;

    /// <summary>Backs up and clears the pipeline environment variables used by entry-run tests.</summary>
    public PipelineInvocationContractTests()
    {
        _entryVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName);
        _payloadVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, _entryVariableBackup);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, _payloadVariableBackup);
    }

    /// <summary>Verifies the emitted command tokens are the shell-group kebab invocations.</summary>
    [Fact]
    public void CommandTokens_AreShellKebabInvocations()
    {
        Assert.Equal("shell pipeline-pre-setup", PlutoniumPipelineInvocation.PreSetupCommand);
        Assert.Equal("shell pipeline-post-setup", PlutoniumPipelineInvocation.PostSetupCommand);
        Assert.Equal("shell pipeline-entry-run", PlutoniumPipelineInvocation.EntryRunCommand);
    }

    /// <summary>Verifies the pre-setup run line appends the pre-setup command to the runner script.</summary>
    [Fact]
    public void ComposePreSetup_AppendsPreSetupCommand()
    {
        var line = PlutoniumPipelineInvocation.ComposePreSetup("chmod +x ./build.sh && ./build.sh");

        Assert.Equal("chmod +x ./build.sh && ./build.sh shell pipeline-pre-setup", line);
    }

    /// <summary>Verifies the post-setup run line appends the post-setup command to the runner script.</summary>
    [Fact]
    public void ComposePostSetup_AppendsPostSetupCommand()
    {
        var line = PlutoniumPipelineInvocation.ComposePostSetup("./build.cmd");

        Assert.Equal("./build.cmd shell pipeline-post-setup", line);
    }

    /// <summary>Verifies the entry-run line appends the entry-run command and carries no entry id.</summary>
    [Fact]
    public void ComposeEntryRun_AppendsEntryRunCommandWithoutEntryId()
    {
        var line = PlutoniumPipelineInvocation.ComposeEntryRun("chmod +x ./build.sh && ./build.sh", "my-job");

        Assert.Equal("chmod +x ./build.sh && ./build.sh shell pipeline-entry-run", line);
        Assert.DoesNotContain("my-job", line, StringComparison.Ordinal);
    }

    /// <summary>Verifies blank run scripts are rejected instead of composing a dangling command.</summary>
    /// <param name="runScript">The blank runner script.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_RejectsBlankRunScript(string runScript)
    {
        Assert.Throws<ArgumentException>(() => PlutoniumPipelineInvocation.ComposePreSetup(runScript));
    }

    /// <summary>Verifies generated-workflow-safe entry ids are accepted.</summary>
    /// <param name="entryId">The entry id.</param>
    [Theory]
    [InlineData("my-job")]
    [InlineData("my_job")]
    [InlineData("_job")]
    [InlineData("Job1")]
    [InlineData("job-1_test")]
    public void ValidateEntryId_AcceptsWorkflowSafeIds(string entryId)
    {
        Assert.Null(Record.Exception(() => PlutoniumPipelineInvocation.ValidateEntryId(entryId)));
    }

    /// <summary>Verifies ids that would break a GitHub job id or YAML step are rejected.</summary>
    /// <param name="entryId">The unsafe entry id.</param>
    [Theory]
    [InlineData("1job")]
    [InlineData("-job")]
    [InlineData("my job")]
    [InlineData("my.job")]
    [InlineData("my/job")]
    [InlineData("my:job")]
    [InlineData("my$job")]
    public void ValidateEntryId_RejectsUnsafeIds(string entryId)
    {
        Assert.Throws<InvalidOperationException>(() => PlutoniumPipelineInvocation.ValidateEntryId(entryId));
    }

    /// <summary>Verifies the entry-run composer refuses an unsafe entry id.</summary>
    [Fact]
    public void ComposeEntryRun_RejectsUnsafeEntryId()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => PlutoniumPipelineInvocation.ComposeEntryRun("./build.cmd", "bad id"));

        Assert.Contains("Invalid entry id", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies every runner script composes with the entry-run command.</summary>
    /// <param name="osName">The runner OS name.</param>
    /// <param name="expectedScript">The runner script the OS renders for both pipelines.</param>
    [Theory]
    [InlineData("ubuntu-latest", "chmod +x ./build.sh && ./build.sh")]
    [InlineData("ubuntu-22.04", "chmod +x ./build.sh && ./build.sh")]
    [InlineData("ubuntu-20.04", "chmod +x ./build.sh && ./build.sh")]
    [InlineData("windows-latest", "./build.cmd")]
    [InlineData("windows-2022", "./build.cmd")]
    [InlineData("windows-2019", "./build.cmd")]
    public void Composer_AppendsEntryRunCommandForEveryRunnerScript(string osName, string expectedScript)
    {
        var runnerOs = CreateRunnerOS(osName);

        Assert.Equal(expectedScript, runnerOs.GetRunScript(PlutoniumPipelineType.GitHub));
        Assert.Equal(expectedScript, runnerOs.GetRunScript(PlutoniumPipelineType.Azure));
        Assert.Equal(expectedScript + " shell pipeline-entry-run", PlutoniumPipelineInvocation.ComposeEntryRun(expectedScript, "my-job"));
    }

    /// <summary>Verifies the GitHub pre-setup step runs the composed pre-setup invocation.</summary>
    [Fact]
    public async Task GitHubPreSetupStep_UsesComposedPreSetupInvocation()
    {
        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), new RecordingSink()));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "PRE_SETUP");
        Assert.Equal(
            "chmod +x ./build.sh && ./build.sh shell pipeline-pre-setup",
            SingleRunLine(job));
    }

    /// <summary>Verifies the GitHub post-setup step runs the composed post-setup invocation.</summary>
    [Fact]
    public async Task GitHubPostSetupStep_UsesComposedPostSetupInvocation()
    {
        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), new RecordingSink()));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "POST_SETUP");
        Assert.Equal(
            "chmod +x ./build.sh && ./build.sh shell pipeline-post-setup",
            SingleRunLine(job));
    }

    /// <summary>Verifies every GitHub entry job runs the composed entry-run line and selects its entry by job variable.</summary>
    [Fact]
    public async Task GitHubEntryJobs_UseComposedEntryRunAndSelectEntryFromEnvironment()
    {
        var result = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), new RecordingSink()));

        foreach (var entryId in new[] { "my-job", "my-publish" })
        {
            var job = result.Document.Jobs.Single(candidate => candidate.Id == entryId);
            var runLine = SingleRunLine(job);
            Assert.Equal("chmod +x ./build.sh && ./build.sh shell pipeline-entry-run", runLine);
            Assert.DoesNotContain(entryId, runLine, StringComparison.Ordinal);
            Assert.Equal(entryId, job.Env[PlutoniumPipelineContract.EntryVariableName]);
        }
    }

    /// <summary>Verifies the Azure pre-setup step runs the composed pre-setup invocation.</summary>
    [Fact]
    public async Task AzurePreSetupStep_UsesComposedPreSetupInvocation()
    {
        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.Azure, CreateEntries(), new RecordingSink()));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "PRE_SETUP");
        Assert.Equal(
            "chmod +x ./build.sh && ./build.sh shell pipeline-pre-setup",
            SingleRunLine(job));
    }

    /// <summary>Verifies the Azure post-setup step runs the composed post-setup invocation.</summary>
    [Fact]
    public async Task AzurePostSetupStep_UsesComposedPostSetupInvocation()
    {
        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.Azure, CreateEntries(), new RecordingSink()));

        var job = result.Document.Jobs.Single(candidate => candidate.Id == "POST_SETUP");
        Assert.Equal(
            "chmod +x ./build.sh && ./build.sh shell pipeline-post-setup",
            SingleRunLine(job));
    }

    /// <summary>Verifies every Azure entry job runs the composed entry-run line and selects its entry by job variable.</summary>
    [Fact]
    public async Task AzureEntryJobs_UseComposedEntryRunAndSelectEntryFromEnvironment()
    {
        var result = await new PlutoniumAzurePipeline().BuildWorkflowAsync(
            CreateContext(PlutoniumPipelineType.Azure, CreateEntries(), new RecordingSink()));

        foreach (var entryId in new[] { "my-job", "my-publish" })
        {
            var job = result.Document.Jobs.Single(candidate => candidate.Id == entryId);
            var runLine = SingleRunLine(job);
            Assert.Equal("chmod +x ./build.sh && ./build.sh shell pipeline-entry-run", runLine);
            Assert.DoesNotContain(entryId, runLine, StringComparison.Ordinal);
            Assert.Equal(entryId, job.Env[PlutoniumPipelineContract.EntryVariableName]);
        }
    }

    /// <summary>Verifies a missing entry selector fails with the regeneration diagnostic.</summary>
    [Fact]
    public async Task EntryRun_MissingEntrySelectorFailsWithDiagnostic()
    {
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context));

        Assert.Contains("PLUTONIUM_ENTRY is missing or empty", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the workflow files", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("PLUTONIUM_ENTRY is missing or empty", StringComparison.Ordinal));
    }

    /// <summary>Verifies a missing or malformed payload fails with the regeneration diagnostic.</summary>
    /// <param name="payload">The raw payload environment value, or null for a missing value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("bm90IGpzb24=")]
    public async Task EntryRun_MissingOrMalformedPayloadFailsWithDiagnostic(string? payload)
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "my-job");
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, payload);
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context));

        Assert.Contains("payload for entry 'my-job' is missing or malformed", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the workflow files", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("is missing or malformed", StringComparison.Ordinal));
    }

    /// <summary>Verifies an undiscovered entry id fails with the regeneration diagnostic.</summary>
    [Fact]
    public async Task EntryRun_UnknownEntryIdFailsWithDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "ghost-entry");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            new PlutoniumPipelinePreSetup().ToPayload());
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context));

        Assert.Contains("Entry id 'ghost-entry' is not a discovered run entry", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the workflow files", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a payload without a setup for the selected entry fails with the regeneration diagnostic.</summary>
    [Fact]
    public async Task EntryRun_MissingEntrySetupFailsWithDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "my-job");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            new PlutoniumPipelinePreSetup().ToPayload());
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context));

        Assert.Contains("has no setup for entry 'my-job'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the workflow files", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a payload without the app outcome fails with the regeneration diagnostic.</summary>
    [Fact]
    public async Task EntryRun_MissingAppOutcomeFailsWithDiagnostic()
    {
        var payload = CreatePayloadWithSetup();
        payload.AppRunEntryMap.Clear();
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "my-job");
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, payload.ToPayload());
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(), sink);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context));

        Assert.Contains("has no app outcome for entry 'my-job'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the workflow files", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a false condition skips the entry with success while the payload reconstructs
    /// the app context: bump run type, version and build id from the app outcome, and empty
    /// release notes because the payload transports none.
    /// </summary>
    [Fact]
    public async Task EntryRun_FalseConditionSkipsAndReconstructsAppContext()
    {
        IRunContext? captured = null;
        var buildEntry = CreateBuildEntry(condition: runContext =>
        {
            captured = runContext;
            return false;
        });
        var sink = new RecordingSink();
        var context = CreateContext(PlutoniumPipelineType.GitHub, CreateEntries(buildEntry), sink);
        context.PreSetup = CreatePayloadWithSetup(PlutoniumRunType.Bump);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "my-job");

        await PlutoniumBuildVerbs.RunPipelineEntryRunAsync(context);

        var runContext = Assert.IsType<PlutoniumRunContext>(captured);
        Assert.Equal(PlutoniumPipelineType.GitHub, runContext.PipelineType);
        var app = Assert.Single(runContext.Apps).Value;
        Assert.Equal(new PlutoniumAppId("my-app"), app.AppId);
        Assert.Equal(PlutoniumRunType.Bump, app.RunType);
        Assert.Equal("main", app.AppVersion.Environment);
        Assert.Equal("1.2.3", app.AppVersion.Version);
        Assert.Equal("1.2.3", app.AppVersion.BuildId);
        var bump = Assert.IsType<PlutoniumBumpReleaseVersion>(app.BumpVersion);
        Assert.Equal(string.Empty, bump.ReleaseNotes);
        Assert.Null(app.PullRequestVersion);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Information && entry.Message == "Running pipeline entry 'my-job'.");
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Information && entry.Message == "Entry 'my-job' skipped (condition false).");
    }

    /// <summary>
    /// Verifies GitHub workflow generation rejects an invalid entry id at the generation
    /// boundary, names the id, and writes no workflow file under the repository root.
    /// </summary>
    /// <param name="entryId">The invalid entry id.</param>
    [Theory]
    [InlineData("bad id")]
    [InlineData("bad\nid")]
    public async Task GitHubWorkflowGeneration_InvalidEntryIdThrowsAndWritesNoFile(string entryId)
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var context = CreateContext(
                PlutoniumPipelineType.GitHub,
                CreateEntries(CreateBuildEntry(entryId)),
                new RecordingSink(),
                repositoryRoot);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context));

            Assert.Contains($"Invalid entry id '{entryId}'", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(repositoryRoot));
            Assert.False(Directory.Exists(Path.Combine(repositoryRoot, ".github")));
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    /// <summary>
    /// Verifies Azure workflow generation rejects an invalid entry id at the generation
    /// boundary, names the id, and writes no <c>azure-pipelines.yml</c>.
    /// </summary>
    /// <param name="entryId">The invalid entry id.</param>
    [Theory]
    [InlineData("bad id")]
    [InlineData("bad\nid")]
    public async Task AzureWorkflowGeneration_InvalidEntryIdThrowsAndWritesNoFile(string entryId)
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var context = CreateContext(
                PlutoniumPipelineType.Azure,
                CreateEntries(CreateBuildEntry(entryId)),
                new RecordingSink(),
                repositoryRoot);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumBuildVerbs.RunAzureWorkflowAsync(context));

            Assert.Contains($"Invalid entry id '{entryId}'", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(repositoryRoot));
            Assert.False(File.Exists(Path.Combine(repositoryRoot, "azure-pipelines.yml")));
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    /// <summary>
    /// Verifies GitHub workflow generation with a valid entry id writes the emitted
    /// workflow file under <c>.github/workflows</c> in the repository root.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflowGeneration_ValidEntryIdWritesWorkflowFile()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var context = CreateContext(
                PlutoniumPipelineType.GitHub,
                CreateEntries(),
                new RecordingSink(),
                repositoryRoot);

            var written = await PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context);

            var writtenPath = Path.GetFullPath(written.Value);
            var workflowsDirectory = Path.GetFullPath(Path.Combine(repositoryRoot, ".github", "workflows"))
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(workflowsDirectory, writtenPath, StringComparison.Ordinal);
            Assert.EndsWith(".yml", writtenPath, StringComparison.Ordinal);
            Assert.True(File.Exists(writtenPath));
            Assert.Contains("shell pipeline-entry-run", File.ReadAllText(writtenPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    /// <summary>
    /// Verifies Azure workflow generation with a valid entry id writes
    /// <c>azure-pipelines.yml</c> under the repository root.
    /// </summary>
    [Fact]
    public async Task AzureWorkflowGeneration_ValidEntryIdWritesWorkflowFile()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var context = CreateContext(
                PlutoniumPipelineType.Azure,
                CreateEntries(),
                new RecordingSink(),
                repositoryRoot);

            var written = await PlutoniumBuildVerbs.RunAzureWorkflowAsync(context);

            var expectedPath = Path.GetFullPath(Path.Combine(repositoryRoot, "azure-pipelines.yml"));
            Assert.Equal(expectedPath, Path.GetFullPath(written.Value));
            Assert.True(File.Exists(expectedPath));
            Assert.Contains("shell pipeline-entry-run", File.ReadAllText(expectedPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    private static PlutoniumRunnerOS CreateRunnerOS(string name) => name switch
    {
        "ubuntu-latest" => PlutoniumRunnerOS.UbuntuLatest,
        "ubuntu-22.04" => PlutoniumRunnerOS.Ubuntu2204,
        "ubuntu-20.04" => PlutoniumRunnerOS.Ubuntu2004,
        "windows-latest" => PlutoniumRunnerOS.WindowsLatest,
        "windows-2022" => PlutoniumRunnerOS.Windows2022,
        "windows-2019" => PlutoniumRunnerOS.Windows2019,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown runner OS."),
    };

    private static BuildEntryDefinition CreateBuildEntry(
        string id = "my-job",
        Func<IRunContext, bool>? condition = null)
    {
        var buildEntry = new BuildEntryDefinition();
        buildEntry.Id = id;
        buildEntry.AppIds = [new PlutoniumAppId("my-app")];
        if (condition is not null)
        {
            buildEntry.Condition(condition);
        }

        return buildEntry;
    }

    private static PlutoniumAllEntries CreateEntries(BuildEntryDefinition? buildEntry = null)
    {
        var build = buildEntry ?? CreateBuildEntry();
        var publishEntry = new PublishEntryDefinition();
        publishEntry.Id = "my-publish";
        publishEntry.AppIds = [new PlutoniumAppId("my-app")];
        return new PlutoniumAllEntries(
            new Dictionary<string, PlutoniumAppEntries>(StringComparer.Ordinal),
            new WorkflowConfigEntryDefinition(),
            new Dictionary<string, ITestEntryDefinition>(StringComparer.Ordinal),
            new Dictionary<string, IBuildEntryDefinition>(StringComparer.Ordinal) { [build.Id] = build },
            new Dictionary<string, IPublishEntryDefinition>(StringComparer.Ordinal) { ["my-publish"] = publishEntry },
            new Dictionary<string, IRunEntryDefinition>(StringComparer.Ordinal)
            {
                [build.Id] = build,
                ["my-publish"] = publishEntry,
            });
    }

    private static PlutoniumPipelinePreSetup CreatePayloadWithSetup(PlutoniumRunType runType = PlutoniumRunType.Commit)
    {
        var payload = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            Environment = "main",
            BuildId = "20240101120000.abc1234",
        };
        payload.EntrySetupMap["my-job"] = new PlutoniumEntrySetup
        {
            Id = "my-job",
            Condition = true,
            RunTypes = { ["my-app"] = runType },
        };
        payload.AppRunEntryMap["my-app"] = new PlutoniumAppRunEntry
        {
            AppId = "my-app",
            Environment = "main",
            Version = "1.2.3",
            OldVersion = "1.2.2",
            HasRelease = true,
        };
        return payload;
    }

    private static PlutoniumPipelineContext CreateContext(
        PlutoniumPipelineType pipelineType,
        PlutoniumAllEntries entries,
        RecordingSink sink,
        string? repositoryRoot = null)
    {
        var logger = new PlutoniumLogger([sink]);
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger), logger, new PlutoniumSecretMasker(), new PlutoniumHost(logger));
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(repositoryRoot ?? "/tmp/plutonium-repo"),
            new PlutoniumPath("/tmp/plutonium-temp"),
            new PlutoniumPath("/tmp/plutonium-out"),
            new PlutoniumPath("/tmp/plutonium-cache"),
            "https://example.test/org/repo.git");
        return new PlutoniumPipelineContext(new object(), options, services, pipelineType) { Entries = entries };
    }

    private static string CreateTemporaryRepositoryRoot() =>
        Path.Combine(Path.GetTempPath(), "plutonium-invocation-" + Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRepositoryRoot(string repositoryRoot)
    {
        if (Directory.Exists(repositoryRoot))
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static string SingleRunLine(PlutoniumWorkflowJob job)
    {
        var lines = job.Steps
            .SelectMany(step => step.Split('\n'))
            .Select(line => line.Trim())
            .ToList();
        var commandIndex = lines.FindIndex(line => line is "run: |" or "script: |" or "- script: |");
        Assert.InRange(commandIndex, 0, lines.Count - 2);
        return lines[commandIndex + 1];
    }

    private sealed class RecordingSink : IPlutoniumLogSink
    {
        public List<(PlutoniumLogLevel Level, string Message)> Entries { get; } = [];

        public void Write(PlutoniumLogLevel level, string? scope, string message) =>
            Entries.Add((level, message));
    }
}
