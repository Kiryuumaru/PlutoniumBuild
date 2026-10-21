namespace Plutonium.Testing;

/// <summary>
/// Resolves the .NET CLI for tests without assuming it is on <c>PATH</c>, and runs it
/// with quiet, telemetry-free defaults.
/// </summary>
public sealed class DotNetCli
{
    private DotNetCli(string executablePath)
    {
        ExecutablePath = executablePath;
    }

    /// <summary>Gets the absolute path of the resolved <c>dotnet</c> executable.</summary>
    public string ExecutablePath { get; }

    /// <summary>Resolves a usable .NET CLI from the environment, the repo bootstrap cache, or <c>PATH</c>.</summary>
    /// <param name="layout">The repository layout.</param>
    /// <returns>The resolved CLI.</returns>
    /// <exception cref="InvalidOperationException">No usable CLI was found.</exception>
    public static DotNetCli Locate(RepositoryLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("PLUTONIUM_DOTNET"),
            Environment.GetEnvironmentVariable("DOTNET_EXE"),
            Path.Combine(layout.BootstrapCacheDirectory, OperatingSystem.IsWindows() ? "dotnet-win" : "dotnet-unix", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
        };

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            }
        }

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return new DotNetCli(fullPath);
            }
        }

        throw new InvalidOperationException(
            "No usable dotnet executable was found. Run ./build.sh once to bootstrap the pinned SDK, " +
            "or set PLUTONIUM_DOTNET to an absolute dotnet path.");
    }

    /// <summary>Runs the CLI with the supplied arguments.</summary>
    /// <param name="arguments">The CLI arguments.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="environment">Additional environment overrides.</param>
    /// <param name="timeout">The maximum run time.</param>
    /// <param name="cancellationToken">A cancellation token for the wait.</param>
    /// <returns>The captured result.</returns>
    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var effectiveEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_NOLOGO"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                effectiveEnvironment[pair.Key] = pair.Value;
            }
        }

        return ProcessHarness.RunAsync(
            new ProcessRequest(ExecutablePath, arguments, workingDirectory, effectiveEnvironment, timeout),
            cancellationToken);
    }

    /// <summary>Reads the SDK version selected in a directory without a <c>global.json</c>.</summary>
    /// <param name="workingDirectory">A directory outside the repository.</param>
    /// <returns>The selected SDK version.</returns>
    public async Task<string> GetInstalledSdkVersionAsync(string workingDirectory)
    {
        var result = await RunAsync(["--version"], workingDirectory).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not read the installed .NET SDK version." + Environment.NewLine + result.Describe());
        }

        return result.StandardOutput.Trim();
    }
}
