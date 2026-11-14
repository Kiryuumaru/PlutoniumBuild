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
    /// <param name="literalArgumentIndexes">
    /// Zero-based indexes of arguments that are grammar tokens rather than
    /// paths, such as ref names. Those arguments reach the script unchanged on
    /// every platform; every other argument has its separators normalized for
    /// bash.
    /// </param>
    /// <returns>The captured result.</returns>
    public static Task<ProcessResult> RunBashAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        IReadOnlyCollection<int>? literalArgumentIndexes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var executable = ResolveBashExecutable();
        var allArguments = new List<string>(arguments.Count + 1)
        {
            ToBashPath(scriptPath),
        };
        allArguments.AddRange(NormalizeBashArguments(arguments, literalArgumentIndexes));
        return RunAsync(
            new ProcessRequest(
                executable,
                allArguments,
                workingDirectory,
                PrepareBashEnvironment(executable, environment),
                timeout));
    }

    /// <summary>
    /// Normalizes path-shaped bash arguments for the host platform while
    /// forwarding literal arguments unchanged. A ref name is git grammar, not a
    /// path: rewriting its backslash on Windows would turn a malformed ref into
    /// a different, valid ref and hide the rejection under test.
    /// </summary>
    /// <param name="arguments">The arguments forwarded to the script.</param>
    /// <param name="literalArgumentIndexes">Zero-based indexes to forward unchanged.</param>
    /// <returns>The argument list to pass to bash.</returns>
    internal static IReadOnlyList<string> NormalizeBashArguments(
        IReadOnlyList<string> arguments,
        IReadOnlyCollection<int>? literalArgumentIndexes)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (literalArgumentIndexes is null || literalArgumentIndexes.Count == 0)
        {
            return arguments.Select(ToBashPath).ToArray();
        }

        var literalIndexes = new HashSet<int>(literalArgumentIndexes);
        foreach (var index in literalIndexes)
        {
            if (index < 0 || index >= arguments.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(literalArgumentIndexes),
                    index,
                    "The literal argument index is outside the argument list.");
            }
        }

        var normalized = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            normalized[index] = literalIndexes.Contains(index)
                ? arguments[index]
                : ToBashPath(arguments[index]);
        }

        return normalized;
    }

    /// <summary>
    /// Adds the Git for Windows tool directories to the child's <c>PATH</c> on
    /// Windows. The <c>bin\bash.exe</c> compat wrapper used to prepend these
    /// directories itself; the real <c>usr\bin\bash.exe</c> does not, so the
    /// harness supplies them explicitly. A caller-supplied <c>PATH</c> keeps its
    /// entries first so a shim directory stays ahead of them; an inherited host
    /// <c>PATH</c> gets the tool directories first so Git's POSIX tools win over
    /// the same-named Windows system tools (<c>find.exe</c>, <c>sort.exe</c>).
    /// </summary>
    /// <param name="executable">The resolved bash executable.</param>
    /// <param name="environment">The caller's environment overrides.</param>
    /// <returns>The environment to use for the child.</returns>
    private static IReadOnlyDictionary<string, string?>? PrepareBashEnvironment(
        string executable,
        IReadOnlyDictionary<string, string?>? environment)
    {
        if (!OperatingSystem.IsWindows())
        {
            return environment;
        }

        var toolDirectories = BashToolDirectories(executable);
        if (toolDirectories.Count == 0)
        {
            return environment;
        }

        return PrepareBashEnvironmentCore(
            toolDirectories,
            environment,
            Environment.GetEnvironmentVariable("PATH"));
    }

    /// <summary>
    /// Builds the child environment for a bash invocation on Windows. The
    /// platform-neutral core keeps the ordering decision testable off Windows:
    /// a caller-supplied <c>PATH</c> stays first and the Git tool directories
    /// are appended, while an inherited host <c>PATH</c> gets the Git tool
    /// directories prepended. A caller that removes <c>PATH</c> by setting it
    /// to null keeps that removal.
    /// </summary>
    /// <param name="toolDirectories">The Git for Windows tool directories.</param>
    /// <param name="environment">The caller's environment overrides.</param>
    /// <param name="inheritedPath">The host <c>PATH</c> used when the caller supplies none.</param>
    /// <returns>The environment to use for the child.</returns>
    internal static IReadOnlyDictionary<string, string?>? PrepareBashEnvironmentCore(
        IReadOnlyList<string> toolDirectories,
        IReadOnlyDictionary<string, string?>? environment,
        string? inheritedPath)
    {
        ArgumentNullException.ThrowIfNull(toolDirectories);

        string? configuredPath = null;
        var callerSuppliedPath = environment is not null &&
            environment.TryGetValue("PATH", out configuredPath);
        string? path;
        if (callerSuppliedPath)
        {
            if (configuredPath is null)
            {
                return environment;
            }

            path = configuredPath;
        }
        else
        {
            path = inheritedPath;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var pathEntries = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            pathEntries.AddRange(path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var entries = new List<string>();
        void Add(string entry)
        {
            if (!entries.Contains(entry, comparer))
            {
                entries.Add(entry);
            }
        }

        if (callerSuppliedPath)
        {
            foreach (var entry in pathEntries)
            {
                Add(entry);
            }

            foreach (var directory in toolDirectories)
            {
                Add(directory);
            }
        }
        else
        {
            foreach (var directory in toolDirectories)
            {
                Add(directory);
            }

            foreach (var entry in pathEntries)
            {
                Add(entry);
            }
        }

        var prepared = environment is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(environment, StringComparer.Ordinal);
        prepared["PATH"] = string.Join(Path.PathSeparator, entries);
        return prepared;
    }

    /// <summary>
    /// Lists the Git for Windows directories a bash child needs on
    /// <c>PATH</c>: <c>usr\bin</c> for the POSIX tools and the
    /// <c>&lt;MSYSTEM&gt;\bin</c> directory that holds <c>git.exe</c> and its
    /// runtime. The list is ordered with <c>usr\bin</c> first so a script that
    /// runs <c>bash</c> finds the real shell, not the compat wrapper.
    /// </summary>
    /// <param name="executable">The resolved bash executable.</param>
    /// <returns>The existing tool directories, or an empty list off Windows.</returns>
    internal static IReadOnlyList<string> BashToolDirectories(string executable)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var bashDirectory = Path.GetDirectoryName(executable);
        if (string.IsNullOrWhiteSpace(bashDirectory))
        {
            return [];
        }

        var name = Path.GetFileName(bashDirectory);
        string? root = null;
        if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(bashDirectory);
            root = string.Equals(Path.GetFileName(parent), "usr", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(parent)
                : parent;
        }

        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? directory)
        {
            if (!string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory) &&
                seen.Add(directory))
            {
                directories.Add(directory);
            }
        }

        if (root is not null)
        {
            Add(Path.Combine(root, "usr", "bin"));
        }

        Add(bashDirectory);

        if (root is not null)
        {
            foreach (var msystem in new[] { "ucrt64", "mingw64", "clang64", "clangarm64", "mingw32" })
            {
                Add(Path.Combine(root, msystem, "bin"));
            }
        }

        return directories;
    }

    /// <summary>
    /// Resolves the bash used to run scripts. Unix uses <c>bash</c> from
    /// <c>PATH</c>; Windows must use Git for Windows and never the WSL stub in
    /// <c>System32</c>. Git for Windows ships two entry points: the
    /// <c>bin\bash.exe</c> compat wrapper prepends <c>&lt;MSYSTEM&gt;\bin</c>
    /// (for example <c>ucrt64\bin</c>) to <c>PATH</c> before running the real
    /// shell, which would displace a test's shim directory. The sibling
    /// <c>usr\bin\bash.exe</c> is the real shell and preserves <c>PATH</c> order.
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

            return PreferRealGitBash(configured);
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
                return PreferRealGitBash(fullPath);
            }
        }

        throw new InvalidOperationException(
            "Git Bash was not found. The Windows test suite executes shell scripts with Git for Windows " +
            "and refuses to fall back to the WSL stub in System32. Install Git for Windows or set " +
            "PLUTONIUM_GIT_BASH to the absolute path of its bash.exe.");
    }

    /// <summary>
    /// Resolves Git for Windows' <c>bin\bash.exe</c> compat wrapper to the real
    /// <c>usr\bin\bash.exe</c> sibling when it exists. The wrapper's
    /// <c>setup_environment</c> prepends <c>&lt;MSYSTEM&gt;\bin</c> to
    /// <c>PATH</c>, which displaces the shim directory the test harness puts
    /// first; the real shell does not rewrite <c>PATH</c>.
    /// </summary>
    /// <param name="bashPath">A bash executable that already exists.</param>
    /// <returns>The real shell when the input is the compat wrapper; otherwise the input.</returns>
    private static string PreferRealGitBash(string bashPath)
    {
        var directory = Path.GetDirectoryName(bashPath);
        if (string.IsNullOrWhiteSpace(directory) ||
            !string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase))
        {
            return bashPath;
        }

        var parent = Path.GetDirectoryName(directory);
        if (parent is null ||
            string.Equals(Path.GetFileName(parent), "usr", StringComparison.OrdinalIgnoreCase))
        {
            return bashPath;
        }

        var realBash = Path.Combine(parent, "usr", "bin", "bash.exe");
        return File.Exists(realBash) ? realBash : bashPath;
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
