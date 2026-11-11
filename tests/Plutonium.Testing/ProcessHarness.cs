using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Plutonium.Testing;

/// <summary>Describes one child process invocation.</summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The argument list, passed without shell interpretation.</param>
/// <param name="WorkingDirectory">The working directory, or null for the current directory.</param>
/// <param name="Environment">Environment overrides; a null value removes the variable.</param>
/// <param name="Timeout">The maximum run time, or null for the harness default.</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    TimeSpan? Timeout = null);

/// <summary>Captures the result of one child process invocation.</summary>
/// <param name="ExitCode">The process exit code, or -1 when the process did not exit normally.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
/// <param name="Duration">The observed run time.</param>
/// <param name="TimedOut">Whether the run was terminated by the request timeout.</param>
/// <param name="CommandLine">The rendered command line for diagnostics.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    string CommandLine)
{
    /// <summary>Gets a value indicating whether the process exited with code zero.</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>Renders the exit code, command line, and captured streams for assertion messages.</summary>
    /// <returns>A diagnostic block.</returns>
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.Append("command: ").AppendLine(CommandLine);
        builder.Append("exit: ").Append(ExitCode);
        if (TimedOut)
        {
            builder.Append(" (timed out)");
        }

        builder.AppendLine();
        builder.AppendLine("stdout:");
        builder.AppendLine(StandardOutput);
        builder.AppendLine("stderr:");
        builder.AppendLine(StandardError);
        return builder.ToString();
    }
}

/// <summary>Runs child processes with captured output, explicit environments, and timeout termination.</summary>
public static class ProcessHarness
{
    /// <summary>Runs one process and waits for it to exit.</summary>
    /// <param name="request">The request to run.</param>
    /// <param name="cancellationToken">A cancellation token for the wait.</param>
    /// <returns>The captured result.</returns>
    public static async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        var commandLine = RenderCommandLine(request.FileName, request.Arguments);
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The process did not start: " + commandLine);
            }
        }
        catch (Win32Exception failure)
        {
            throw new InvalidOperationException(
                "Could not start '" + request.FileName + "' for command: " + commandLine,
                failure);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var timedOut = false;
        var exitObserved = false;

        using var timeout = new CancellationTokenSource(request.Timeout ?? TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            exitObserved = true;
        }
        catch (OperationCanceledException)
        {
            timedOut = timeout.IsCancellationRequested;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                exitObserved = true;
            }
            catch (TimeoutException)
            {
                exitObserved = false;
            }
        }

        stopwatch.Stop();
        var output = await ReadAvailableAsync(standardOutput).ConfigureAwait(false);
        var error = await ReadAvailableAsync(standardError).ConfigureAwait(false);
        return new ProcessResult(
            exitObserved && process.HasExited ? process.ExitCode : -1,
            output,
            error,
            stopwatch.Elapsed,
            timedOut || !exitObserved,
            commandLine);
    }

    /// <summary>Runs a bash script with arguments.</summary>
    /// <param name="scriptPath">The script path.</param>
    /// <param name="arguments">The arguments forwarded to the script.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="environment">Environment overrides.</param>
    /// <param name="timeout">The maximum run time.</param>
    /// <returns>The captured result.</returns>
    public static Task<ProcessResult> RunBashAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var executable = ResolveBashExecutable();
        var allArguments = new List<string>(arguments.Count + 1)
        {
            ToBashPath(scriptPath),
        };
        allArguments.AddRange(arguments.Select(ToBashPath));
        return RunAsync(
            new ProcessRequest(executable, allArguments, workingDirectory, environment, timeout));
    }

    /// <summary>
    /// Resolves the bash used to run scripts. Unix uses <c>bash</c> from
    /// <c>PATH</c>; Windows must use Git for Windows and never the WSL stub in
    /// <c>System32</c>.
    /// </summary>
    /// <returns>The bash executable path or command name.</returns>
    /// <exception cref="InvalidOperationException">
    /// No usable Git Bash was found, or the configured path is the WSL stub.
    /// </exception>
    internal static string ResolveBashExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        var configured = Environment.GetEnvironmentVariable("PLUTONIUM_GIT_BASH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            if (!File.Exists(configured))
            {
                throw new InvalidOperationException(
                    "PLUTONIUM_GIT_BASH points to '" + configured +
                    "', but that file does not exist. Point it at Git for Windows' bash.exe.");
            }

            if (IsWslBashStub(configured))
            {
                throw new InvalidOperationException(
                    "PLUTONIUM_GIT_BASH points to '" + configured +
                    "', which is the WSL stub. Point it at Git for Windows' bash.exe.");
            }

            return configured;
        }

        var candidates = new List<string>();
        AddGitBashCandidate(
            candidates,
            Environment.GetEnvironmentVariable("GIT_INSTALL_ROOT"),
            "bin",
            "bash.exe");
        AddGitBashCandidate(
            candidates,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "bin",
            "bash.exe");
        AddGitBashCandidate(
            candidates,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Git",
            "bin",
            "bash.exe");
        AddGitBashCandidate(
            candidates,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Git",
            "bin",
            "bash.exe");

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var entry in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = entry.Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                AddGitBashCandidate(candidates, directory, "bash.exe");
                AddGitBashCandidate(candidates, directory, "..", "bin", "bash.exe");
            }
        }

        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (IsWslBashStubPath(fullPath))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new InvalidOperationException(
            "Git Bash was not found. The Windows test suite executes shell scripts with Git for Windows " +
            "and refuses to fall back to the WSL stub in System32. Install Git for Windows or set " +
            "PLUTONIUM_GIT_BASH to the absolute path of its bash.exe.");
    }

    private static bool IsWslBashStub(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return IsWslBashStubPath(fullPath);
    }

    private static bool IsWslBashStubPath(string fullPath)
    {
        var systemBash = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "bash.exe");
        if (string.Equals(fullPath, systemBash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var directory = Path.GetFileName(Path.GetDirectoryName(fullPath));
        return string.Equals(directory, "System32", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directory, "Sysnative", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddGitBashCandidate(
        ICollection<string> candidates,
        string? root,
        params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        candidates.Add(path);
    }

    internal static string ToBashPath(string value) =>
        OperatingSystem.IsWindows()
            ? value.Replace('\\', '/')
            : value;

    private static async Task<string> ReadAvailableAsync(Task<string> read)
    {
        try
        {
            return await read.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
    }

    private static string RenderCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder(fileName);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            if (argument.Length == 0 || argument.Any(char.IsWhiteSpace))
            {
                builder.Append('"').Append(argument.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                builder.Append(argument);
            }
        }

        return builder.ToString();
    }
}
