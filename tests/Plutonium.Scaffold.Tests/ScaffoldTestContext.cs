using System.Xml.Linq;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>Shared setup for consumer repositories and scaffold script runs.</summary>
internal static class ScaffoldTestContext
{
    private static readonly Lazy<ScaffoldManifest> Manifest = new(LoadManifest);

    internal static IReadOnlyList<string> ManagedFiles => Manifest.Value.ManagedFiles;

    internal static IReadOnlyList<string> RequiredFiles => Manifest.Value.RequiredFiles;

    private const string SolutionText = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    internal static GitRepository CreateConsumerRepository(TempWorkspace workspace, bool withSolution = true)
    {
        var repository = GitRepository.Create(workspace, "consumer");
        repository.WriteFile("sentinel.txt", "do not touch\n");
        repository.WriteFile("README.md", "# Consumer\n");
        if (withSolution)
        {
            repository.WriteFile("Consumer.sln", SolutionText + "\n");
            repository.WriteFile(
                "src/App/App.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
        }

        repository.Commit("consumer baseline");
        return repository;
    }

    internal static async Task<ProcessResult> RunInitAsync(
        ScaffoldSource source,
        GitRepository target,
        params string[] extraArguments)
    {
        Assert.True(File.Exists(source.InitScriptPath), "Init script not found: " + source.InitScriptPath);
        var arguments = new List<string>
        {
            "--source",
            source.SourcePath,
            "--sha",
            source.HeadSha,
        };
        arguments.AddRange(extraArguments);
        return await RunScriptAsync(source.InitScriptPath, arguments, target.RootPath);
    }

    internal static async Task<ProcessResult> RunUpdateAsync(
        GitRepository target,
        ScaffoldSource source,
        string? sha = null,
        bool includeSource = true)
    {
        var updateScript = Path.Combine(target.RootPath, "build", "update.sh");
        Assert.True(File.Exists(updateScript), "Update script not found: " + updateScript);
        var arguments = new List<string>();
        if (includeSource)
        {
            arguments.Add("--source");
            arguments.Add(source.SourcePath);
        }

        if (sha is not null)
        {
            arguments.Add("--sha");
            arguments.Add(sha);
        }

        return await RunScriptAsync(updateScript, arguments, target.RootPath);
    }

    internal static Task<ProcessResult> RunScriptAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var home = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(workingDirectory))!,
            ".test-home");
        Directory.CreateDirectory(home);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };

        return ProcessHarness.RunBashAsync(
            scriptPath,
            arguments,
            workingDirectory,
            environment,
            TimeSpan.FromMinutes(10));
    }

    internal static void AssertSucceeded(ProcessResult result, string operation)
    {
        Assert.True(result.Succeeded, operation + " failed." + Environment.NewLine + result.Describe());
    }

    internal static void AssertFailed(ProcessResult result, string operation)
    {
        Assert.False(result.Succeeded, operation + " unexpectedly succeeded." + Environment.NewLine + result.Describe());
    }

    private static ScaffoldManifest LoadManifest()
    {
        var layout = RepositoryLayout.Locate();
        var lockPath = Path.Combine(layout.ScaffoldBuildDirectory, "plutonium.lock");
        Assert.True(File.Exists(lockPath), "Scaffold lock not found: " + lockPath);

        var managed = new List<string>();
        var preserved = new List<string>();
        foreach (var line in File.ReadLines(lockPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            if (fields[0] == "managed")
            {
                Assert.True(fields.Length == 3, "Malformed managed lock line: " + trimmed);
                Assert.True(
                    fields[2].Length == 64 && fields[2].All(char.IsAsciiHexDigit),
                    "Managed lock line does not carry a SHA-256 hash: " + trimmed);
                managed.Add(fields[1]);
            }
            else if (fields[0] == "preserve")
            {
                Assert.True(fields.Length == 2, "Malformed preserve lock line: " + trimmed);
                preserved.Add(fields[1]);
            }
        }

        Assert.NotEmpty(managed);
        Assert.NotEmpty(preserved);
        var required = managed
            .Concat(preserved)
            .Append("build/plutonium.lock")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return new ScaffoldManifest(
            managed.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            required);
    }

    internal static XDocument ReadXml(string path)
    {
        Assert.True(File.Exists(path), "Expected file not found: " + path);
        return XDocument.Load(path);
    }

    internal static string ReadText(string path)
    {
        Assert.True(File.Exists(path), "Expected file not found: " + path);
        return File.ReadAllText(path);
    }

    internal static string PackageReferenceVersion(string projectPath)
    {
        var document = ReadXml(projectPath);
        var reference = document.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals(
                    element.Attribute("Include")?.Value,
                    "PlutoniumBuild",
                    StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        return reference.Attribute("Version")?.Value
               ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value
               ?? string.Empty;
    }

    private sealed class ScaffoldManifest
    {
        public ScaffoldManifest(IReadOnlyList<string> managedFiles, IReadOnlyList<string> requiredFiles)
        {
            ManagedFiles = managedFiles;
            RequiredFiles = requiredFiles;
        }

        public IReadOnlyList<string> ManagedFiles { get; }

        public IReadOnlyList<string> RequiredFiles { get; }
    }
}
