using Plutonium.Testing;
using Xunit;

namespace Plutonium.Packaging.Tests;

/// <summary>
/// Proves a clean consumer can restore, build, and run from a local feed with
/// no network access and no project references.
/// </summary>
[Collection("packaging")]
public sealed class OfflineConsumerTests
{
    private readonly PackagingFixture _fixture;

    /// <summary>Initializes the test class with the shared pack.</summary>
    /// <param name="fixture">The shared packaging fixture.</param>
    public OfflineConsumerTests(PackagingFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The packaged assemblies restore and run from the feed alone.</summary>
    [Fact]
    public async Task Consumer_RestoresBuildsAndRunsFromLocalFeed()
    {
        Assert.True(_fixture.Feed.Succeeded, _fixture.Feed.Failure);
        Assert.True(_fixture.Staging.Succeeded, _fixture.Staging.Failure);

        var consumerDirectory = _fixture.Workspace.PathFor("consumer");
        var packagesDirectory = _fixture.Workspace.PathFor("consumer-packages");
        var homeDirectory = _fixture.Workspace.PathFor("consumer-home");
        Directory.CreateDirectory(consumerDirectory);
        Directory.CreateDirectory(homeDirectory);

        var projectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>Consumer</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="PlutoniumBuild" Version="VERSION" />
              </ItemGroup>
            </Project>
            """.Replace("VERSION", _fixture.Version, StringComparison.Ordinal));

        File.WriteAllText(
            Path.Combine(consumerDirectory, "Program.cs"),
            """
            using Plutonium.Core.Runner;
            using Plutonium.Helpers.Versioning;
            using Plutonium.Shell;

            Console.WriteLine(typeof(PlutoniumRunner).Assembly.GetName().Name);
            Console.WriteLine(typeof(PlutoniumVersions).Assembly.GetName().Name);
            Console.WriteLine(typeof(PlutoniumShellExtensions).Assembly.GetName().Name);
            """);

        var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.config");
        File.WriteAllText(
            nugetConfigPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="FEED" />
              </packageSources>
            </configuration>
            """.Replace("FEED", _fixture.Feed.Directory, StringComparison.Ordinal));

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = homeDirectory,
            ["DOTNET_CLI_HOME"] = homeDirectory,
            ["NUGET_PACKAGES"] = packagesDirectory,
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["ALL_PROXY"] = "http://127.0.0.1:9",
            ["NO_PROXY"] = string.Empty,
        };

        var restore = await _fixture.DotNet.RunAsync(
            [
                "restore",
                projectPath,
                "--configfile",
                nugetConfigPath,
                "--packages",
                packagesDirectory,
                "--no-cache",
                "-nologo",
                "-v:minimal",
            ],
            consumerDirectory,
            environment,
            TimeSpan.FromMinutes(10));

        Assert.True(restore.Succeeded, "Offline restore failed." + Environment.NewLine + restore.Describe());

        var build = await _fixture.DotNet.RunAsync(
            ["build", projectPath, "-c", "Release", "--no-restore", "-nologo", "-v:minimal"],
            consumerDirectory,
            environment,
            TimeSpan.FromMinutes(10));

        Assert.True(build.Succeeded, "Offline build failed." + Environment.NewLine + build.Describe());

        var outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net10.0");
        Assert.True(Directory.Exists(outputDirectory), "Consumer output directory not found: " + outputDirectory);
        foreach (var assembly in new[]
                 {
                     "Plutonium.Core.dll",
                     "Plutonium.Helpers.dll",
                     "Plutonium.Shell.dll",
                     "ApplicationBuilderHelpers.dll",
                 })
        {
            Assert.True(
                File.Exists(Path.Combine(outputDirectory, assembly)),
                "Consumer output is missing " + assembly + ".");
        }

        var run = await _fixture.DotNet.RunAsync(
            [Path.Combine(outputDirectory, "Consumer.dll")],
            consumerDirectory,
            environment,
            TimeSpan.FromMinutes(2));

        Assert.True(run.Succeeded, "Consumer run failed." + Environment.NewLine + run.Describe());
        Assert.Contains("Plutonium.Core", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Plutonium.Helpers", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Plutonium.Shell", run.StandardOutput, StringComparison.Ordinal);
    }
}
