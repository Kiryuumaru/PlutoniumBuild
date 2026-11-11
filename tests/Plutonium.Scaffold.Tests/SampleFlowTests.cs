using System.Text.RegularExpressions;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins the sample Test/Build/Publish flow shipped with the scaffold: entries
/// call terminal tools and do not assume the consumer repository is C#.
/// </summary>
public sealed class SampleFlowTests
{
    /// <summary>The scaffold build definition declares the sample lifecycle through terminal calls.</summary>
    [Fact]
    public void SampleFlow_DeclaresTerminalLifecycle()
    {
        var layout = RepositoryLayout.Locate();
        var buildDefinition = Path.Combine(layout.ScaffoldBuildDirectory, "build.cs");
        var text = ScaffoldTestContext.ReadText(buildDefinition);

        foreach (var verb in new[] { "Test", "Build", "Publish" })
        {
            Assert.True(
                Regex.IsMatch(text, "\\b" + verb + "\\b", RegexOptions.CultureInvariant),
                "The sample build definition does not declare " + verb + ".");
        }

        Assert.Contains("Terminal", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-C# consumer can run Test, Build, and Publish through the package,
    /// and a stale update journal under <c>build/temp</c> is not compiled into
    /// the build app.
    /// </summary>
    [Fact]
    public async Task SampleFlow_RunsTestBuildPublish()
    {
        using var workspace = TempWorkspace.Create("sample-flow");
        var layout = RepositoryLayout.Locate();
        var dotnet = DotNetCli.Locate(layout);
        var installedSdk = await dotnet.GetInstalledSdkVersionAsync(workspace.Root);
        var source = await ScaffoldSource.CreateAsync(workspace, layout, installedSdk);

        var feedBuilder = new LocalFeedBuilder(layout, dotnet);
        var feed = await feedBuilder.PackAsync(workspace, layout.ReadBuilderVersion());
        Assert.True(feed.Succeeded, feed.Failure);
        var staging = feedBuilder.StagePackageClosure(feed, "ApplicationBuilderHelpers", "4.1.160");
        Assert.True(staging.Succeeded, staging.Failure);

        var target = ScaffoldTestContext.CreateConsumerRepository(workspace, withSolution: false);
        target.WriteFile("package.json", "{\n  \"name\": \"sample\",\n  \"private\": true\n}\n");
        target.Commit("sample consumer");

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");

        // An update journals the previous scaffold files under
        // build/temp/update/<stamp>/files; the build project must not compile
        // that journal copy alongside the live build definition.
        var journal = Path.Combine(
            target.RootPath,
            "build",
            "temp",
            "update",
            "20260101T000000Z",
            "files",
            "build");
        Directory.CreateDirectory(journal);
        File.Copy(
            Path.Combine(target.RootPath, "build", "build.cs"),
            Path.Combine(journal, "build.cs"));

        File.WriteAllText(
            Path.Combine(target.RootPath, "NuGet.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="FEED" />
              </packageSources>
            </configuration>
            """.Replace("FEED", feed.Directory, StringComparison.Ordinal));

        var home = workspace.PathFor("sample-home");
        Directory.CreateDirectory(home);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = Path.GetDirectoryName(dotnet.ExecutablePath) + Path.PathSeparator + "/usr/bin" + Path.PathSeparator + "/bin",
            ["HOME"] = home,
            ["DOTNET_CLI_HOME"] = home,
            ["NUGET_PACKAGES"] = workspace.PathFor("sample-packages"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_NOLOGO"] = "1",
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["ALL_PROXY"] = "http://127.0.0.1:9",
            ["NO_PROXY"] = string.Empty,
        };

        foreach (var verb in new[] { "Test", "Build", "Publish" })
        {
            var run = await ProcessHarness.RunBashAsync(
                Path.Combine(target.RootPath, "build.sh"),
                [verb],
                target.RootPath,
                environment,
                TimeSpan.FromMinutes(15));

            Assert.True(run.Succeeded, verb + " failed." + Environment.NewLine + run.Describe());
            Assert.True(
                run.StandardOutput.Contains(verb, StringComparison.OrdinalIgnoreCase) ||
                run.StandardError.Contains(verb, StringComparison.OrdinalIgnoreCase),
                verb + " did not appear to execute." + Environment.NewLine + run.Describe());
        }

        Assert.DoesNotContain(
            Directory.GetFiles(target.RootPath, "*.csproj", SearchOption.AllDirectories),
            path => !path.StartsWith(Path.Combine(target.RootPath, "build"), StringComparison.Ordinal));

    }
}
