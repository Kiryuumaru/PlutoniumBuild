using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Plutonium.Testing;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Plutonium.Packaging.Tests;

/// <summary>
/// Pins the repository's own CI and publish workflow policy: the exact check
/// names and trigger scope, the scaffold checks, the computed matrix and its
/// consumers, the Post Setup result gate, read-only default permissions,
/// immutable action refs, the manual publish confirmation, the published-version
/// probe, and the step-scoped API-key push.
/// </summary>
public sealed class RepositoryWorkflowPolicyTests
{
    private const string CheckoutAction = "actions/checkout@11d5960a326750d5838078e36cf38b85af677262";

    private static readonly string[] RequiredCiJobs = ["pre-setup", "scaffold-smoke", "test"];

    private static readonly string[] BashSmokeScripts =
    [
        "bash scaffold/smoke/source-credentials.sh",
        "bash scaffold/smoke/lock-contract.sh",
        "bash scaffold/smoke/preserve-drift.sh",
        "bash scaffold/smoke/update-rollback.sh",
    ];

    /// <summary>
    /// Verifies CI keeps the exact required check names, runs only for pushes to
    /// <c>main</c> and pull requests, forbids event-bypass triggers, and keeps the
    /// read-only default permissions.
    /// </summary>
    [Fact]
    public void CiWorkflow_PinsCheckNamesTriggersAndPermissions()
    {
        var workflow = WorkflowDocument.Load("ci.yml");

        Assert.Equal(
            ["pull_request", "push"],
            Ordered(workflow.TriggerNames));
        var push = workflow.Trigger("push");
        Assert.Equal(["branches"], workflow.Keys(push));
        Assert.Equal(["main"], ReadStringList(Get(push, "branches")));
        Assert.True(workflow.IsTriggerEmpty("pull_request"), "pull_request must stay unfiltered.");
        Assert.DoesNotContain("pull_request_target", workflow.TriggerNames);
        Assert.DoesNotContain("workflow_run", workflow.TriggerNames);
        Assert.DoesNotContain("repository_dispatch", workflow.TriggerNames);

        Assert.Equal(
            ["post-setup", "pre-setup", "scaffold-smoke", "test"],
            Ordered(workflow.JobIds));
        Assert.Equal("Pre Setup", workflow.JobName("pre-setup"));
        Assert.Equal("Scaffold smoke (${{ matrix.os }})", workflow.JobName("scaffold-smoke"));
        Assert.Equal("Build and test (${{ matrix.os }})", workflow.JobName("test"));
        Assert.Equal("Post Setup", workflow.JobName("post-setup"));

        AssertReadOnlyDefaultPermissions(workflow);
        foreach (var jobId in RequiredCiJobs)
        {
            Assert.False(
                workflow.Has("if", workflow.Job(jobId)),
                "Only Post Setup may carry an if condition; '" + jobId + "' must not.");
        }

        var concurrency = workflow.Mapping(workflow.Root, "concurrency");
        Assert.Equal("ci-${{ github.ref }}", Scalar(Get(concurrency, "group")));
        Assert.Equal("true", Scalar(Get(concurrency, "cancel-in-progress")));
    }

    /// <summary>
    /// Verifies Pre Setup runs the lock verification, each bash smoke once, and
    /// the PowerShell parse gate, and that no workflow-wide secret is referenced.
    /// </summary>
    [Fact]
    public void CiWorkflow_RunsEachScaffoldCheckOnce()
    {
        var workflow = WorkflowDocument.Load("ci.yml");

        Assert.Equal(
            ["Check out", "Verify scaffold lock", "Run bash scaffold smoke", "Parse PowerShell scripts", "Compute matrix"],
            workflow.StepNames("pre-setup"));
        Assert.Equal(
            "bash scaffold/lock.sh verify",
            workflow.StepRun("pre-setup", "Verify scaffold lock").Trim());
        Assert.Equal(
            BashSmokeScripts,
            RunLines(workflow.StepRun("pre-setup", "Run bash scaffold smoke")));

        var parse = workflow.Step("pre-setup", "Parse PowerShell scripts");
        Assert.Equal("pwsh", Scalar(Get(parse, "shell")));
        var parseRun = Scalar(Get(parse, "run"));
        Assert.Contains("[System.Management.Automation.Language.Parser]::ParseFile", parseRun, StringComparison.Ordinal);
        Assert.Contains("if ($failures -gt 0) { exit 1 }", parseRun, StringComparison.Ordinal);

        Assert.Single(
            workflow.AllRunScripts(),
            run => run.Trim() == "bash scaffold/lock.sh verify");
        Assert.DoesNotContain(
            workflow.AllScalars(),
            value => value.Contains("secrets.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the matrix literal has exactly the two expected entries and that
    /// Pre Setup computes it once while both consumer jobs read it through
    /// <c>fromJSON</c>.
    /// </summary>
    [Fact]
    public void CiWorkflow_ComputesOneMatrixAndWiresConsumers()
    {
        var workflow = WorkflowDocument.Load("ci.yml");
        var matrixRun = workflow.StepRun("pre-setup", "Compute matrix");

        var match = Regex.Match(
            matrixRun,
            @"matrix='(?<json>\{.*\})'",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Compute matrix did not assign a single-quoted JSON literal.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        Assert.Equal(
            ["os"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        var operatingSystems = document.RootElement.GetProperty("os");
        Assert.Equal(JsonValueKind.Array, operatingSystems.ValueKind);
        Assert.Equal(
            ["ubuntu-latest", "windows-latest"],
            operatingSystems.EnumerateArray()
                .Select(value =>
                {
                    Assert.Equal(JsonValueKind.String, value.ValueKind);
                    return value.GetString() ?? string.Empty;
                })
                .ToList());

        Assert.Equal("matrix", Scalar(Get(workflow.Step("pre-setup", "Compute matrix"), "id")));
        Assert.Contains("echo \"matrix=$matrix\" >> \"$GITHUB_OUTPUT\"", matrixRun, StringComparison.Ordinal);
        var outputs = workflow.Mapping(workflow.Job("pre-setup"), "outputs");
        Assert.Equal(
            ["matrix"],
            workflow.Keys(outputs));
        Assert.Equal("${{ steps.matrix.outputs.matrix }}", Scalar(Get(outputs, "matrix")));

        AssertMatrixConsumer(workflow, "scaffold-smoke");
        AssertMatrixConsumer(workflow, "test");
        Assert.Equal(
            ["Check out", "Run PowerShell scaffold smoke (Ubuntu)", "Run PowerShell scaffold smoke (Windows)"],
            workflow.StepNames("scaffold-smoke"));
        AssertStaticStep(
            workflow,
            "scaffold-smoke",
            "Run PowerShell scaffold smoke (Ubuntu)",
            "runner.os == 'Linux'",
            "pwsh",
            "./scaffold/smoke/powershell-smoke.ps1 -SkipBuild");
        AssertStaticStep(
            workflow,
            "scaffold-smoke",
            "Run PowerShell scaffold smoke (Windows)",
            "runner.os == 'Windows'",
            "powershell",
            "./scaffold/smoke/powershell-smoke.ps1");
        Assert.Equal(
            ["Check out", "Build and test (Ubuntu)", "Build and test (Windows)"],
            workflow.StepNames("test"));
        AssertStaticStep(
            workflow,
            "test",
            "Build and test (Ubuntu)",
            "runner.os == 'Linux'",
            "bash",
            "./build.sh");
        AssertStaticStep(
            workflow,
            "test",
            "Build and test (Windows)",
            "runner.os == 'Windows'",
            "cmd",
            @".\build.cmd");
    }

    /// <summary>
    /// Verifies Post Setup waits for every other job, always runs, and receives
    /// each required job result instead of inferring success.
    /// </summary>
    [Fact]
    public void CiWorkflow_PostSetupNeedsEveryOtherJobAndGatesOnResults()
    {
        var workflow = WorkflowDocument.Load("ci.yml");
        var postSetup = workflow.Job("post-setup");

        var needs = ReadStringList(Get(postSetup, "needs"));
        Assert.Equal(Ordered(RequiredCiJobs), Ordered(needs));
        Assert.Equal(needs.Count, needs.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("post-setup", needs);
        foreach (var jobId in RequiredCiJobs)
        {
            Assert.DoesNotContain(
                "post-setup",
                ReadOptionalStringList(workflow.Job(jobId), "needs"));
        }

        Assert.Equal("always()", workflow.JobCondition("post-setup"));
        var gate = workflow.Step("post-setup", "Verify required job results");
        var environment = workflow.StringMap(workflow.Mapping(gate, "env"));
        Assert.Equal(3, environment.Count);
        Assert.Equal("${{ needs.pre-setup.result }}", environment["PRE_SETUP_RESULT"]);
        Assert.Equal("${{ needs.scaffold-smoke.result }}", environment["SCAFFOLD_SMOKE_RESULT"]);
        Assert.Equal("${{ needs.test.result }}", environment["TEST_RESULT"]);
    }

    /// <summary>
    /// Executes the Post Setup gate script with every required job successful and
    /// verifies it exits cleanly without reporting an error.
    /// </summary>
    [Fact]
    public async Task CiPostSetup_AllowsAllRequiredJobsToSucceed()
    {
        using var workspace = TempWorkspace.Create("post-setup-success");
        var workflow = WorkflowDocument.Load("ci.yml");
        var script = WriteRunScript(
            workspace,
            "post-setup.sh",
            workflow.StepRun("post-setup", "Verify required job results"));

        var result = await RunPostSetupAsync(script, workspace.Root, "success", "success", "success");

        Assert.True(result.Succeeded, "Post Setup rejected successful results." + Environment.NewLine + result.Describe());
        Assert.Contains("All required jobs succeeded.", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("::error::", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Executes the Post Setup gate script once per required job and non-success
    /// result, verifying each combination fails with the job-specific error.
    /// </summary>
    /// <param name="jobId">The required job id.</param>
    /// <param name="variable">The environment variable carrying the job result.</param>
    /// <param name="status">The non-success job result.</param>
    [Theory]
    [InlineData("pre-setup", "PRE_SETUP_RESULT", "failure")]
    [InlineData("pre-setup", "PRE_SETUP_RESULT", "cancelled")]
    [InlineData("pre-setup", "PRE_SETUP_RESULT", "skipped")]
    [InlineData("scaffold-smoke", "SCAFFOLD_SMOKE_RESULT", "failure")]
    [InlineData("scaffold-smoke", "SCAFFOLD_SMOKE_RESULT", "cancelled")]
    [InlineData("scaffold-smoke", "SCAFFOLD_SMOKE_RESULT", "skipped")]
    [InlineData("test", "TEST_RESULT", "failure")]
    [InlineData("test", "TEST_RESULT", "cancelled")]
    [InlineData("test", "TEST_RESULT", "skipped")]
    public async Task CiPostSetup_FailsWhenAnyRequiredJobIsNotSuccessful(
        string jobId,
        string variable,
        string status)
    {
        using var workspace = TempWorkspace.Create("post-setup-" + status);
        var workflow = WorkflowDocument.Load("ci.yml");
        var script = WriteRunScript(
            workspace,
            "post-setup.sh",
            workflow.StepRun("post-setup", "Verify required job results"));
        var results = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PRE_SETUP_RESULT"] = "success",
            ["SCAFFOLD_SMOKE_RESULT"] = "success",
            ["TEST_RESULT"] = "success",
        };
        results[variable] = status;

        var result = await RunPostSetupAsync(
            script,
            workspace.Root,
            results["PRE_SETUP_RESULT"],
            results["SCAFFOLD_SMOKE_RESULT"],
            results["TEST_RESULT"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "::error::Required job '" + jobId + "' finished with '" + status + "'.",
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies publish is a dispatch-only workflow with a required, defaulted
    /// boolean confirmation and a job condition that also requires <c>main</c>.
    /// </summary>
    [Fact]
    public void PublishWorkflow_IsDispatchOnlyBehindConfirmGate()
    {
        var workflow = WorkflowDocument.Load("publish.yml");

        Assert.Equal(["workflow_dispatch"], workflow.TriggerNames);
        var dispatch = workflow.Trigger("workflow_dispatch");
        Assert.Equal(["inputs"], workflow.Keys(dispatch));
        var inputs = workflow.Mapping(dispatch, "inputs");
        Assert.Equal(["confirm"], workflow.Keys(inputs));
        var confirm = workflow.Mapping(inputs, "confirm");
        Assert.Equal("true", Scalar(Get(confirm, "required")));
        Assert.Equal("false", Scalar(Get(confirm, "default")));
        Assert.Equal("boolean", Scalar(Get(confirm, "type")));
        Assert.False(
            string.IsNullOrWhiteSpace(Scalar(Get(confirm, "description"))),
            "The confirm input needs a description.");

        Assert.Equal(["publish"], workflow.JobIds);
        Assert.Equal("Publish to nuget.org", workflow.JobName("publish"));
        Assert.Equal(
            "inputs.confirm == true && github.ref == 'refs/heads/main'",
            workflow.JobCondition("publish"));
        Assert.False(
            workflow.Has("needs", workflow.Job("publish")),
            "The publish job must not wait on a job that can bypass the confirm gate.");

        AssertReadOnlyDefaultPermissions(workflow);
        var concurrency = workflow.Mapping(workflow.Root, "concurrency");
        Assert.Equal("publish-nuget", Scalar(Get(concurrency, "group")));
        Assert.Equal("false", Scalar(Get(concurrency, "cancel-in-progress")));

        Assert.False(workflow.Has("env", workflow.Root), "Publish must not declare workflow-wide env.");
        Assert.False(workflow.Has("env", workflow.Job("publish")), "Publish must not declare job-wide env.");
        foreach (var mapping in workflow.AllMappings())
        {
            Assert.False(
                workflow.Has("environment", mapping),
                "Publish must not use a deployment environment.");
        }

        var scalars = workflow.AllScalars().ToList();
        foreach (var forbidden in new[]
                 {
                     "id-token",
                     "NuGet/login",
                     "NUGET_USER",
                     "nuget.pkg.github.com",
                     "GITHUB_TOKEN",
                     "gh release",
                     "git tag",
                     "packages: write",
                 })
        {
            Assert.DoesNotContain(
                scalars,
                value => value.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Verifies the resolved package version is read once and shared by the
    /// version-exists guard and the push, that the guard runs first, and that the
    /// API-key secret is scoped to the push step.
    /// </summary>
    [Fact]
    public void PublishWorkflow_ScopesApiKeyToPushAndReusesResolvedVersion()
    {
        var workflow = WorkflowDocument.Load("publish.yml");
        var stepNames = workflow.StepNames("publish").ToList();
        var versionIndex = stepNames.IndexOf("Read package version");
        var packIndex = stepNames.IndexOf("Pack");
        var guardIndex = stepNames.IndexOf("Refuse to republish an existing version");
        var pushIndex = stepNames.IndexOf("Push");
        Assert.True(versionIndex >= 0, "Read package version step is missing.");
        Assert.True(packIndex >= 0 && versionIndex > packIndex, "The package must be packed before its version is read.");
        Assert.True(guardIndex > versionIndex, "The version guard must run after the version is read.");
        Assert.True(pushIndex > guardIndex, "The version guard must run before the push.");

        const string packageProject = "src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj";
        var version = workflow.Step("publish", "Read package version");
        Assert.Equal("version", Scalar(Get(version, "id")));
        var versionRun = Scalar(Get(version, "run"));
        Assert.Contains("-getProperty:PackageVersion", versionRun, StringComparison.Ordinal);
        Assert.Contains(packageProject, versionRun, StringComparison.Ordinal);
        Assert.Contains("echo \"version=$version\" >> \"$GITHUB_OUTPUT\"", versionRun, StringComparison.Ordinal);
        var packRun = workflow.StepRun("publish", "Pack");
        Assert.Contains(packageProject, packRun, StringComparison.Ordinal);
        Assert.Contains("--output artifacts", packRun, StringComparison.Ordinal);

        var guard = workflow.Step("publish", "Refuse to republish an existing version");
        var push = workflow.Step("publish", "Push");
        var guardEnvironment = workflow.StringMap(workflow.Mapping(guard, "env"));
        var pushEnvironment = workflow.StringMap(workflow.Mapping(push, "env"));
        Assert.Single(guardEnvironment);
        Assert.Equal("${{ steps.version.outputs.version }}", guardEnvironment["VERSION"]);
        Assert.Equal("${{ steps.version.outputs.version }}", pushEnvironment["VERSION"]);
        Assert.Equal(2, pushEnvironment.Count);
        Assert.Equal(
            "${{ secrets.NUGET_AUTH_TOKEN }}",
            pushEnvironment["NUGET_AUTH_TOKEN"]);

        Assert.Equal(
            ["Refuse to republish an existing version", "Push"],
            workflow.StepNames("publish").Where(name =>
                name is "Refuse to republish an existing version" or "Push"));

        var guardRun = Scalar(Get(guard, "run"));
        Assert.Contains(
            "url=\"https://api.nuget.org/v3-flatcontainer/plutoniumbuild/${VERSION}/plutoniumbuild.${VERSION}.nupkg\"",
            guardRun,
            StringComparison.Ordinal);
        Assert.Contains("404)", guardRun, StringComparison.Ordinal);
        Assert.Contains("200)", guardRun, StringComparison.Ordinal);
        Assert.Contains("exit 1", guardRun, StringComparison.Ordinal);

        var pushRun = Scalar(Get(push, "run"));
        Assert.Contains(
            "\"$DOTNET_EXE\" nuget push \"artifacts/PlutoniumBuild.${VERSION}.nupkg\"",
            pushRun,
            StringComparison.Ordinal);
        Assert.Contains("--api-key \"$NUGET_AUTH_TOKEN\"", pushRun, StringComparison.Ordinal);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", pushRun, StringComparison.Ordinal);
        Assert.Contains("--no-symbols", pushRun, StringComparison.Ordinal);

        var secrets = workflow.AllScalars()
            .Where(value => value.Contains("secrets.", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["${{ secrets.NUGET_AUTH_TOKEN }}"], secrets);
    }

    /// <summary>
    /// Executes the publish version guard against a fake nuget.org: 404 allows the
    /// push, while 200 and 500 fail before any publish can run.
    /// </summary>
    /// <param name="httpCode">The fake nuget.org response code.</param>
    /// <param name="expectedExitCode">The expected guard exit code.</param>
    /// <param name="expectedOutput">The expected diagnostic.</param>
    [Theory]
    [InlineData(404, 0, "Version 9.9.9 is not published yet.")]
    [InlineData(200, 1, "::error::Version 9.9.9 already exists on nuget.org; bump the version before publishing.")]
    [InlineData(500, 1, "::error::Unexpected nuget.org response (500)")]
    public async Task PublishVersionGuard_AllowsOnlyAnUnpublishedVersion(
        int httpCode,
        int expectedExitCode,
        string expectedOutput)
    {
        using var workspace = TempWorkspace.Create("publish-guard-" + httpCode.ToString(CultureInfo.InvariantCulture));
        var workflow = WorkflowDocument.Load("publish.yml");
        var toolchain = FakeToolchain.Create(workspace);
        var curlLog = workspace.PathFor("curl-url.log");
        toolchain.AddShim(
            "curl",
            """
            #!/usr/bin/env bash
            url=""
            for argument in "$@"; do
              url="$argument"
            done
            printf '%s\n' "$url" >> "$FAKE_CURL_URL_LOG"
            printf '%s' "$FAKE_HTTP_CODE"
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FAKE_CURL_URL_LOG"] = curlLog,
            });
        var script = WriteRunScript(
            workspace,
            "version-guard.sh",
            workflow.StepRun("publish", "Refuse to republish an existing version"));

        var result = await ProcessHarness.RunBashAsync(
            script,
            [],
            workspace.Root,
            toolchain.BuildEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["VERSION"] = "9.9.9",
                ["FAKE_HTTP_CODE"] = httpCode.ToString(CultureInfo.InvariantCulture),
            }));

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Contains(expectedOutput, result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(
            "https://api.nuget.org/v3-flatcontainer/plutoniumbuild/9.9.9/plutoniumbuild.9.9.9.nupkg",
            File.ReadAllText(curlLog).Trim());
    }

    /// <summary>
    /// Executes the push script with no token and verifies it fails before it can
    /// invoke the dotnet CLI.
    /// </summary>
    [Fact]
    public async Task PublishPush_RefusesMissingTokenBeforeInvokingDotNet()
    {
        using var workspace = TempWorkspace.Create("publish-empty-token");
        var workflow = WorkflowDocument.Load("publish.yml");
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet("10.0.402", 0);
        var script = WriteRunScript(
            workspace,
            "push.sh",
            workflow.StepRun("publish", "Push"));

        var result = await ProcessHarness.RunBashAsync(
            script,
            [],
            workspace.Root,
            toolchain.BuildEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["VERSION"] = "9.9.9",
                ["DOTNET_EXE"] = "dotnet",
                ["NUGET_AUTH_TOKEN"] = null,
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "::error::NUGET_AUTH_TOKEN is not configured.",
            result.StandardOutput,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, toolchain.ReadLog());
    }

    /// <summary>
    /// Executes the push script with a token and verifies the exact resolved
    /// package path, API key, source, and symbol suppression reach the CLI.
    /// </summary>
    [Fact]
    public async Task PublishPush_SendsExactPackageWithApiKey()
    {
        using var workspace = TempWorkspace.Create("publish-push");
        var workflow = WorkflowDocument.Load("publish.yml");
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet("10.0.402", 0);
        var script = WriteRunScript(
            workspace,
            "push.sh",
            workflow.StepRun("publish", "Push"));

        var result = await ProcessHarness.RunBashAsync(
            script,
            [],
            workspace.Root,
            toolchain.BuildEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["VERSION"] = "9.9.9",
                ["DOTNET_EXE"] = "dotnet",
                ["NUGET_AUTH_TOKEN"] = "test-token",
            }));

        Assert.True(result.Succeeded, "Push script failed." + Environment.NewLine + result.Describe());
        Assert.Equal(
            "dotnet nuget push artifacts/PlutoniumBuild.9.9.9.nupkg --api-key test-token --source https://api.nuget.org/v3/index.json --no-symbols\n",
            toolchain.ReadLog().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies both workflows pin the exact checkout commit, disable persisted
    /// credentials, and use no other action reference.
    /// </summary>
    [Fact]
    public void RepositoryWorkflows_PinOnlyKnownActions()
    {
        var ci = WorkflowDocument.Load("ci.yml");
        var publish = WorkflowDocument.Load("publish.yml");

        Assert.Equal(3, ci.AllUses().Count);
        Assert.Single(publish.AllUses());
        Assert.All(ci.AllUses(), reference => Assert.Equal(CheckoutAction, reference));
        Assert.All(publish.AllUses(), reference => Assert.Equal(CheckoutAction, reference));

        AssertCheckouts(ci, 3);
        AssertCheckouts(publish, 1, expectedRef: "refs/heads/main");
    }

    /// <summary>
    /// Verifies every workflow keeps <c>shell</c> static and free of expression
    /// contexts, and that scalar job controls never inject contexts. A dynamic
    /// shell is resolved before the job starts on GitHub Actions, so a matrix
    /// expression there fails the workflow at load time.
    /// </summary>
    [Fact]
    public void RepositoryWorkflows_UseStaticShellAndScalarJobControls()
    {
        var workflowDirectory = Path.Combine(
            RepositoryLayout.Locate().Root,
            ".github",
            "workflows");
        var paths = Directory
            .EnumerateFiles(workflowDirectory, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(workflowDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(paths);

        foreach (var path in paths)
        {
            var workflow = WorkflowDocument.LoadPath(path);
            foreach (var key in new[] { "shell", "continue-on-error", "timeout-minutes", "working-directory" })
            {
                foreach (var mapping in workflow.AllMappings())
                {
                    if (!workflow.Has(key, mapping))
                    {
                        continue;
                    }

                    var value = Assert.IsType<YamlScalarNode>(Get(mapping, key));
                    Assert.DoesNotContain(
                        "${{",
                        value.Value ?? string.Empty,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    private static void AssertMatrixConsumer(WorkflowDocument workflow, string jobId)
    {
        var job = workflow.Job(jobId);
        Assert.Equal("${{ matrix.os }}", Scalar(Get(job, "runs-on")));
        var strategy = workflow.Mapping(job, "strategy");
        Assert.Equal("${{ fromJSON(needs.pre-setup.outputs.matrix) }}", Scalar(Get(strategy, "matrix")));
        Assert.Equal("false", Scalar(Get(strategy, "fail-fast")));
        Assert.Equal(["pre-setup"], ReadStringList(Get(job, "needs")));
    }

    private static void AssertStaticStep(
        WorkflowDocument workflow,
        string jobId,
        string stepName,
        string condition,
        string shell,
        string run)
    {
        var step = workflow.Step(jobId, stepName);
        Assert.Equal(condition, NormalizeExpression(Scalar(Get(step, "if"))));
        Assert.Equal(shell, Scalar(Get(step, "shell")));
        Assert.Equal(run, Scalar(Get(step, "run")));
    }

    private static void AssertCheckouts(
        WorkflowDocument workflow,
        int expectedCount,
        string? expectedRef = null)
    {
        var checkouts = workflow.CheckoutSteps();
        Assert.Equal(expectedCount, checkouts.Count);
        foreach (var checkout in checkouts)
        {
            Assert.Equal("false", workflow.StringMap(workflow.Mapping(checkout, "with"))["persist-credentials"]);
            if (expectedRef is not null)
            {
                Assert.Equal(expectedRef, workflow.StringMap(workflow.Mapping(checkout, "with"))["ref"]);
            }
        }
    }

    private static void AssertReadOnlyDefaultPermissions(WorkflowDocument workflow)
    {
        var permissions = workflow.StringMap(workflow.Mapping(workflow.Root, "permissions"));
        Assert.Equal(["contents"], workflow.Keys(workflow.Mapping(workflow.Root, "permissions")));
        Assert.Equal("read", permissions["contents"]);
        foreach (var jobId in workflow.JobIds)
        {
            Assert.False(
                workflow.Has("permissions", workflow.Job(jobId)),
                "Job '" + jobId + "' must not override the read-only default permissions.");
        }
    }

    private static async Task<ProcessResult> RunPostSetupAsync(
        string scriptPath,
        string workingDirectory,
        string preSetup,
        string scaffoldSmoke,
        string test)
    {
        return await ProcessHarness.RunBashAsync(
            scriptPath,
            [],
            workingDirectory,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PRE_SETUP_RESULT"] = preSetup,
                ["SCAFFOLD_SMOKE_RESULT"] = scaffoldSmoke,
                ["TEST_RESULT"] = test,
            });
    }

    private static string WriteRunScript(TempWorkspace workspace, string name, string run)
    {
        var path = workspace.PathFor(name);
        File.WriteAllText(path, run.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static IReadOnlyList<string> RunLines(string run) =>
        run.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Ordered(IEnumerable<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static YamlNode Get(YamlMappingNode mapping, string key)
    {
        if (TryGet(mapping, key, out var value))
        {
            return value;
        }

        throw new Xunit.Sdk.XunitException("Missing YAML key '" + key + "'.");
    }

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string Scalar(YamlNode node) =>
        Assert.IsType<YamlScalarNode>(node).Value ?? string.Empty;

    private static IReadOnlyList<string> ReadStringList(YamlNode node) =>
        node is YamlSequenceNode sequence
            ? sequence.Children.Select(Scalar).ToList()
            : [Scalar(node)];

    private static IReadOnlyList<string> ReadOptionalStringList(YamlMappingNode mapping, string key) =>
        TryGet(mapping, key, out var value) ? ReadStringList(value) : [];

    private static string NormalizeExpression(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.StartsWith("${{", StringComparison.Ordinal) &&
            trimmed.EndsWith("}}", StringComparison.Ordinal))
        {
            trimmed = trimmed[3..^2].Trim();
        }

        return Regex.Replace(trimmed, @"\s+", " ", RegexOptions.CultureInvariant);
    }

    private sealed class WorkflowDocument
    {
        private WorkflowDocument(YamlMappingNode root)
        {
            Root = root;
        }

        public YamlMappingNode Root { get; }

        public IReadOnlyList<string> JobIds => Keys(Mapping(Root, "jobs"));

        public IReadOnlyList<string> TriggerNames => Keys(Mapping(Root, "on"));

        public IReadOnlyList<string> AllRunScripts() => CollectMappings(Root)
            .Where(mapping => TryGet(mapping, "run", out _))
            .Select(mapping => Scalar(Get(mapping, "run")))
            .ToList();

        public IReadOnlyList<string> AllUses() => CollectMappings(Root)
            .Where(mapping => TryGet(mapping, "uses", out _))
            .Select(mapping => Scalar(Get(mapping, "uses")))
            .ToList();

        public IReadOnlyList<YamlMappingNode> CheckoutSteps() => CollectMappings(Root)
            .Where(mapping => TryGet(mapping, "uses", out var uses) && Scalar(uses) == CheckoutAction)
            .ToList();

        public IEnumerable<string> AllScalars() => CollectScalars(Root);

        public IEnumerable<YamlMappingNode> AllMappings() => CollectMappings(Root);

        public static WorkflowDocument Load(string fileName) =>
            LoadPath(Path.Combine(
                RepositoryLayout.Locate().Root,
                ".github",
                "workflows",
                fileName));

        public static WorkflowDocument LoadPath(string path)
        {
            var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);
            Assert.Single(stream.Documents);
            return new WorkflowDocument(
                Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode));
        }

        public YamlMappingNode Job(string jobId) => Mapping(Mapping(Root, "jobs"), jobId);

        public string JobName(string jobId) => Scalar(Get(Job(jobId), "name"));

        public string JobCondition(string jobId) =>
            NormalizeExpression(Scalar(Get(Job(jobId), "if")));

        public YamlMappingNode Trigger(string name) => Mapping(Mapping(Root, "on"), name);

        public bool IsTriggerEmpty(string name)
        {
            var node = Get(Mapping(Root, "on"), name);
            return node switch
            {
                YamlScalarNode scalar => string.IsNullOrEmpty(scalar.Value),
                YamlMappingNode mapping => mapping.Children.Count == 0,
                _ => false,
            };
        }

        public IReadOnlyList<string> StepNames(string jobId) =>
            Steps(jobId).Select(step => Scalar(Get(step, "name"))).ToList();

        public YamlMappingNode Step(string jobId, string stepName) =>
            Steps(jobId).Single(step =>
                TryGet(step, "name", out var name) && Scalar(name) == stepName);

        public string StepRun(string jobId, string stepName) =>
            Scalar(Get(Step(jobId, stepName), "run"));

        public IReadOnlyList<YamlMappingNode> Steps(string jobId) =>
            Assert.IsType<YamlSequenceNode>(Get(Job(jobId), "steps"))
                .Children.Select(child => Assert.IsType<YamlMappingNode>(child))
                .ToList();

        public YamlMappingNode Mapping(YamlMappingNode mapping, string key) =>
            Assert.IsType<YamlMappingNode>(Get(mapping, key));

        public IReadOnlyList<string> Keys(YamlMappingNode mapping) =>
            mapping.Children.Keys.Select(Scalar).ToList();

        public IReadOnlyDictionary<string, string> StringMap(YamlMappingNode mapping) =>
            mapping.Children.ToDictionary(
                pair => Scalar(pair.Key),
                pair => Scalar(pair.Value),
                StringComparer.Ordinal);

        public bool Has(string key, YamlMappingNode mapping) => TryGet(mapping, key, out _);

        private static IEnumerable<string> CollectScalars(YamlNode node)
        {
            switch (node)
            {
                case YamlScalarNode scalar:
                    yield return scalar.Value ?? string.Empty;
                    break;
                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        foreach (var value in CollectScalars(child))
                        {
                            yield return value;
                        }
                    }

                    break;
                case YamlMappingNode mapping:
                    foreach (var pair in mapping.Children)
                    {
                        foreach (var value in CollectScalars(pair.Key))
                        {
                            yield return value;
                        }

                        foreach (var value in CollectScalars(pair.Value))
                        {
                            yield return value;
                        }
                    }

                    break;
            }
        }

        private static IEnumerable<YamlMappingNode> CollectMappings(YamlNode node)
        {
            switch (node)
            {
                case YamlMappingNode mapping:
                    yield return mapping;
                    foreach (var pair in mapping.Children)
                    {
                        foreach (var child in CollectMappings(pair.Value))
                        {
                            yield return child;
                        }
                    }

                    break;
                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        foreach (var nested in CollectMappings(child))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }
    }
}
