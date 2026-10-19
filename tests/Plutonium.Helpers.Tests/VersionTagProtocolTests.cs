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
/// Tag protocol for AppId-keyed SemVer releases: plain, passed, and failed version tags.
/// </summary>
public sealed class VersionTagProtocolTests
{
    /// <summary>
    /// Verifies a plain version tag joins the lowercase AppId, a slash, and the version.
    /// </summary>
    [Fact]
    public void PlainVersionTag_FormatsAppSlashVersion()
    {
        var tag = PlutoniumVersionTags.VersionTag(new PlutoniumAppId("my-app"), PlutoniumSemVersion.Parse("1.2.3"));

        Assert.Equal("my-app/1.2.3", tag);
    }

    /// <summary>
    /// Verifies the passed marker suffix appends to the plain tag.
    /// </summary>
    [Fact]
    public void PassedVersionTag_AppendsPassedSuffix()
    {
        var tag = PlutoniumVersionTags.VersionTag(
            new PlutoniumAppId("my-app"),
            PlutoniumSemVersion.Parse("1.2.3"),
            PlutoniumVersionTags.PassedSuffix);

        Assert.Equal("my-app/1.2.3-passed", tag);
    }

    /// <summary>
    /// Verifies the failed marker suffix appends to the plain tag.
    /// </summary>
    [Fact]
    public void FailedVersionTag_AppendsFailedSuffix()
    {
        var tag = PlutoniumVersionTags.VersionTag(
            new PlutoniumAppId("my-app"),
            PlutoniumSemVersion.Parse("1.2.3"),
            PlutoniumVersionTags.FailedSuffix);

        Assert.Equal("my-app/1.2.3-failed", tag);
    }

    /// <summary>
    /// Verifies prerelease labels survive the tag round-trip unchanged.
    /// </summary>
    [Fact]
    public void PlainVersionTag_KeepsPrereleaseLabel()
    {
        var tag = PlutoniumVersionTags.VersionTag(new PlutoniumAppId("my-app"), PlutoniumSemVersion.Parse("2.0.0-main.1"));

        Assert.Equal("my-app/2.0.0-main.1", tag);
    }
}
