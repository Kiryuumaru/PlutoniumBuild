using System.Text.Json;
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
/// <c>versions.json</c> contract: raw generation takes each environment's latest
/// without build metadata, typed reads reject unknown or missing apps and non-strict
/// versions, verification fails only when the file trails the reconciled latest, and
/// reconciliation merges an ahead file version as a bump only in JSON-file mode.
/// </summary>
public sealed class VersionsFileContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "plutonium-versions-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Verifies the generated map keeps only the latest version per environment without metadata.</summary>
    [Fact]
    public void GenerateRaw_TakesLatestPerEnvironmentWithoutMetadata()
    {
        var latestByApp = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>>>(StringComparer.Ordinal)
        {
            ["app-a"] = new Dictionary<string, IReadOnlyList<PlutoniumSemVersion>>(StringComparer.Ordinal)
            {
                ["main"] = [Parse("1.2.3+build.1"), Parse("1.3.0+build.42")],
                ["dev"] = [],
            },
            ["app-b"] = new Dictionary<string, IReadOnlyList<PlutoniumSemVersion>>(StringComparer.Ordinal)
            {
                ["main"] = [Parse("2.0.0-dev.3+build.7")],
            },
        };

        var raw = PlutoniumVersionsFile.GenerateRaw(latestByApp);

        Assert.Equal("1.3.0", raw["app-a"]["main"]);
        Assert.False(raw["app-a"].ContainsKey("dev"));
        Assert.Equal("2.0.0-dev.3", raw["app-b"]["main"]);
    }

    /// <summary>Verifies the written flat JSON document reads back as typed versions.</summary>
    [Fact]
    public void WriteRawThenRead_RoundTripsTypedDocument()
    {
        var path = Path.Combine(_root, "versions.json");
        var raw = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["app-a"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["main"] = "1.3.0",
                ["Dev"] = "1.4.0-rc.2",
            },
            ["app-b"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "2.0.0" },
        };

        PlutoniumVersionsFile.WriteRaw(path, raw);
        var typed = PlutoniumVersionsFile.Read(path, ["app-a", "app-b"]);

        Assert.Equal("1.3.0", typed.Versions["app-a"].EnvironmentVersions["main"].ToString());
        Assert.Equal("1.4.0-rc.2", typed.Versions["app-a"].EnvironmentVersions["dev"].ToString());
        Assert.Equal("2.0.0", typed.Versions["app-b"].EnvironmentVersions["main"].ToString());
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("1.3.0", document.RootElement.GetProperty("app-a").GetProperty("main").GetString());
        Assert.Equal("2.0.0", document.RootElement.GetProperty("app-b").GetProperty("main").GetString());
    }

    /// <summary>Verifies a corrupt or missing file reads as null instead of throwing.</summary>
    [Fact]
    public void TryReadRaw_ReturnsNullForMissingAndCorruptFiles()
    {
        var path = Path.Combine(_root, "versions.json");

        Assert.Null(PlutoniumVersionsFile.TryReadRaw(path));

        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ not json");
        Assert.Null(PlutoniumVersionsFile.TryReadRaw(path));

        File.WriteAllText(path, "{\"app-a\":{\"main\":\"1.0.0\"}}");
        Assert.Equal("1.0.0", PlutoniumVersionsFile.TryReadRaw(path)!["app-a"]["main"]);
    }

    /// <summary>Verifies the typed read rejects an app that discovery does not know.</summary>
    [Fact]
    public void Read_RejectsUnknownApp()
    {
        var path = Path.Combine(_root, "versions.json");
        PlutoniumVersionsFile.WriteRaw(path, new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["app-a"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.0.0" },
            ["app-ghost"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.0.0" },
        });

        var failure = Assert.Throws<InvalidOperationException>(
            () => PlutoniumVersionsFile.Read(path, ["app-a"]));

        Assert.Contains("app-ghost", failure.Message, StringComparison.Ordinal);
        Assert.Contains("has no entry", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a missing app and an unknown app are rejected while reading one app.</summary>
    /// <param name="appId">The requested app id.</param>
    /// <param name="expected">The expected diagnostic fragment.</param>
    [Theory]
    [InlineData("app-b", "does not exist")]
    [InlineData("app-ghost", "has no entry")]
    public void ReadApp_RejectsMissingAndUnknownApps(string appId, string expected)
    {
        var raw = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["app-a"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.0.0" },
            ["app-ghost"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.0.0" },
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => PlutoniumVersionsFile.ReadApp(raw, appId, ["app-a", "app-b"]));

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a file version behind the reconciled latest fails verification.</summary>
    [Fact]
    public void Verify_ThrowsWhenFileVersionTrailsLatest()
    {
        var envVersions = new Dictionary<string, IReadOnlyList<PlutoniumSemVersion>>(StringComparer.Ordinal)
        {
            ["main"] = [Parse("1.0.0"), Parse("1.1.0")],
        };
        var fileMap = new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal)
        {
            ["main"] = Parse("1.0.0"),
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => PlutoniumVersionsFile.Verify(envVersions, fileMap, "app-a", ["main"]));

        Assert.Contains("trails the latest 1.1.0", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a file version at or ahead of the reconciled latest passes, and absent environments are skipped.</summary>
    [Fact]
    public void Verify_AllowsAtOrAheadFileAndSkipsAbsentEnvironments()
    {
        var envVersions = new Dictionary<string, IReadOnlyList<PlutoniumSemVersion>>(StringComparer.Ordinal)
        {
            ["main"] = [Parse("1.1.0")],
        };
        var fileMap = new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal)
        {
            ["main"] = Parse("1.2.0"),
        };

        PlutoniumVersionsFile.Verify(envVersions, fileMap, "app-a", ["main"]);
        PlutoniumVersionsFile.Verify(envVersions, fileMap, "app-a", ["dev"]);
        PlutoniumVersionsFile.Verify(
            envVersions,
            new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal) { ["main"] = Parse("1.1.0") },
            "app-a",
            ["main"]);
    }

    /// <summary>Verifies an ahead file version merges into the environment group as a bump in JSON-file mode.</summary>
    [Fact]
    public void GetAllVersions_MergesAheadFileVersionAsBump()
    {
        var options = new PlutoniumVersionsOptions(
            ["0123456789abcdef\trefs/tags/app-a/1.0.0"],
            ["main"],
            "main",
            "head")
        {
            UseJsonFileVersioning = true,
            KnownAppIds = ["app-a"],
            VersionsFileRaw = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["app-a"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.1.0" },
            },
        };

        var all = PlutoniumVersions.GetAllVersions(new PlutoniumAppId("app-a"), options);

        Assert.Equal(new[] { "0.0.0", "1.0.0", "1.1.0" }, all.EnvVersionGrouped["main"].Select(version => version.ToString()));
        Assert.Contains(all.VersionBump, version => version.ToString() == "1.1.0");
        Assert.Equal("1.1.0", all.EnvVersionFileMap["main"].ToString());
    }

    /// <summary>Verifies the file version is ignored when JSON-file versioning is off.</summary>
    [Fact]
    public void GetAllVersions_IgnoresFileVersionWhenJsonVersioningDisabled()
    {
        var options = new PlutoniumVersionsOptions(
            ["0123456789abcdef\trefs/tags/app-a/1.0.0"],
            ["main"],
            "main",
            "head")
        {
            VersionsFileRaw = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["app-a"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = "1.1.0" },
            },
        };

        var all = PlutoniumVersions.GetAllVersions(new PlutoniumAppId("app-a"), options);

        Assert.DoesNotContain(all.EnvVersionGrouped["main"], version => version.ToString() == "1.1.0");
        Assert.Empty(all.VersionBump);
        Assert.Empty(all.EnvVersionFileMap);
    }

    /// <summary>Verifies the Bump verb refuses to run when JSON-file versioning is enabled.</summary>
    [Fact]
    public async Task Bump_IsRefusedWhenJsonFileVersioningIsEnabled()
    {
        var context = CreateBumpContext(new JsonVersionedBumpBuild());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumBuildVerbs.RunBumpAsync(context, watch: false));

        Assert.Equal("Bump is disabled if UseJsonFileVersioning is enabled.", failure.Message);
    }

    private static PlutoniumSemVersion Parse(string value) => PlutoniumSemVersion.Parse(value);

    private static PlutoniumPipelineContext CreateBumpContext(object build)
    {
        var logger = new PlutoniumLogger([new NullSink()]);
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger),
            logger,
            new PlutoniumSecretMasker(),
            new PlutoniumHost(logger));
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-bump-refused")),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-bump-temp")),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-bump-out")),
            new PlutoniumPath(Path.Combine(Path.GetTempPath(), "plutonium-bump-cache")),
            "https://example.test/org/repo.git");
        return new PlutoniumPipelineContext(build, options, services, PlutoniumPipelineType.Local);
    }

    private sealed class JsonVersionedBumpBuild
    {
        public WorkflowConfigEntry WorkflowConfig =>
            definition => definition.UseJsonFileVersioning(true);

        public BuildEntry App => definition => definition.AppId("app-a");
    }

    private sealed class NullSink : IPlutoniumLogSink
    {
        public void Write(PlutoniumLogLevel level, string? scope, string message)
        {
        }
    }
}
