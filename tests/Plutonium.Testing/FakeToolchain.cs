using System.Text;

namespace Plutonium.Testing;

/// <summary>
/// Builds a PATH containing deterministic shims for <c>dotnet</c> and
/// <c>curl</c> so bootstrap behavior can be exercised without a network or a
/// real SDK install. Every shim appends its argument list to a shared log.
/// </summary>
public sealed class FakeToolchain
{
    private readonly StringBuilder _log = new();

    private FakeToolchain(string rootDirectory, string shimDirectory, string logPath)
    {
        RootDirectory = rootDirectory;
        ShimDirectory = shimDirectory;
        LogPath = logPath;
    }

    /// <summary>Gets the root directory that owns the shims, home, and temporary files.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the directory that must be prepended to <c>PATH</c>.</summary>
    public string ShimDirectory { get; }

    /// <summary>Gets the file that receives one line per shim invocation.</summary>
    public string LogPath { get; }

    /// <summary>Gets the home directory tests should export as <c>HOME</c>.</summary>
    public string HomeDirectory => Path.Combine(RootDirectory, "home");

    /// <summary>Gets the temporary directory tests should export as <c>TMPDIR</c>.</summary>
    public string TempDirectory => Path.Combine(RootDirectory, "tmp");

    /// <summary>Creates the shim and home directories.</summary>
    /// <param name="workspace">The workspace that owns the toolchain.</param>
    /// <returns>The prepared toolchain.</returns>
    public static FakeToolchain Create(TempWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var root = workspace.PathFor("toolchain");
        var shims = Path.Combine(root, "bin");
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(root, "tmp"));
        return new FakeToolchain(root, shims, Path.Combine(root, "invocations.log"));
    }

    /// <summary>
    /// Adds a <c>dotnet</c> shim. Version probes succeed; all other commands exit
    /// with <paramref name="commandExitCode"/>.
    /// </summary>
    /// <param name="version">The SDK version reported by version probes.</param>
    /// <param name="commandExitCode">The exit code for non-version commands.</param>
    public void AddDotNet(string version, int commandExitCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var sdkDirectory = Path.Combine(RootDirectory, "sdk");
        Directory.CreateDirectory(sdkDirectory);

        AddShim(
            "dotnet",
            """
            #!/usr/bin/env bash
            printf 'dotnet %s\n' "$*" >> "$FAKE_TOOL_LOG"
            case "${1:-}" in
              --version) printf '%s\n' "$FAKE_DOTNET_VERSION"; exit 0 ;;
              --list-sdks) printf '%s [%s]\n' "$FAKE_DOTNET_VERSION" "$FAKE_DOTNET_DIR"; exit 0 ;;
              --list-runtimes) printf 'Microsoft.NETCore.App %s [%s]\n' "$FAKE_DOTNET_VERSION" "$FAKE_DOTNET_DIR"; exit 0 ;;
              --info) printf 'Version: %s\n' "$FAKE_DOTNET_VERSION"; exit 0 ;;
            esac
            exit "$FAKE_DOTNET_EXIT"
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FAKE_DOTNET_VERSION"] = version,
                ["FAKE_DOTNET_EXIT"] = commandExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["FAKE_DOTNET_DIR"] = sdkDirectory,
            });
    }

    /// <summary>
    /// Adds a <c>curl</c> shim. <c>fail</c> exits with the timeout code 28;
    /// <c>tamper</c> succeeds but writes a payload that cannot match a pinned
    /// checksum; <c>copy</c> writes <c>FAKE_CURL_PAYLOAD</c>.
    /// </summary>
    /// <param name="mode">The behavior mode.</param>
    public void AddCurl(string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        AddShim(
            "curl",
            """
            #!/usr/bin/env bash
            printf 'curl %s\n' "$*" >> "$FAKE_TOOL_LOG"
            output=""
            while [ "$#" -gt 0 ]; do
              case "$1" in
                -o|--output) output="${2:-}"; shift 2 ;;
                --output=*) output="${1#--output=}"; shift ;;
                -*o) output="${2:-}"; shift 2 ;;
                *) shift ;;
              esac
            done
            case "$FAKE_CURL_MODE" in
              fail)
                echo "curl: (28) Failed to connect to dot.net: connection timed out" >&2
                exit 28
                ;;
              tamper)
                if [ -n "$output" ]; then
                  printf 'tampered download payload' > "$output"
                else
                  printf 'tampered download payload'
                fi
                exit 0
                ;;
              copy)
                if [ -z "$FAKE_CURL_PAYLOAD" ]; then
                  echo "missing FAKE_CURL_PAYLOAD" >&2
                  exit 2
                fi
                if [ -n "$output" ]; then
                  cp "$FAKE_CURL_PAYLOAD" "$output"
                else
                  cat "$FAKE_CURL_PAYLOAD"
                fi
                exit 0
                ;;
              *)
                echo "unknown fake curl mode: $FAKE_CURL_MODE" >&2
                exit 3
                ;;
            esac
            """,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["FAKE_CURL_MODE"] = mode });
    }

    /// <summary>Adds an executable shell shim as-is.</summary>
    /// <param name="name">The shim file name.</param>
    /// <param name="body">The full script body.</param>
    /// <param name="environment">Extra environment values injected into the shim source.</param>
    public void AddShim(string name, string body, IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                builder.Append("export ").Append(pair.Key).Append('=')
                    .AppendLine(ShellQuote(pair.Value));
            }
        }

        var bodyText = body.TrimStart();
        if (bodyText.StartsWith("#!/usr/bin/env bash", StringComparison.Ordinal))
        {
            var newline = bodyText.IndexOf('\n', StringComparison.Ordinal);
            bodyText = newline < 0 ? string.Empty : bodyText[(newline + 1)..];
        }

        builder.Append(bodyText);
        if (!builder.ToString().EndsWith('\n'))
        {
            builder.AppendLine();
        }

        var path = Path.Combine(ShimDirectory, name);
        File.WriteAllText(path, builder.ToString());
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>Builds a PATH with the shims first and the standard Unix tool directories after.</summary>
    /// <param name="includeExisting">Whether to append the current process PATH after the standard directories.</param>
    /// <returns>The PATH value.</returns>
    public string BuildPath(bool includeExisting = true)
    {
        var entries = new List<string> { ShimDirectory, "/usr/bin", "/bin" };
        var existing = Environment.GetEnvironmentVariable("PATH");
        if (includeExisting && !string.IsNullOrWhiteSpace(existing))
        {
            foreach (var entry in existing.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (entries.Contains(entry, StringComparer.Ordinal))
                {
                    continue;
                }

                entries.Add(entry);
            }
        }

        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>Builds the environment overrides every shim-based test needs.</summary>
    /// <param name="extra">Additional overrides.</param>
    /// <param name="includeExistingPath">Whether to append the current process PATH.</param>
    /// <returns>The environment dictionary.</returns>
    public Dictionary<string, string?> BuildEnvironment(
        IReadOnlyDictionary<string, string?>? extra = null,
        bool includeExistingPath = true)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = BuildPath(includeExistingPath),
            ["HOME"] = HomeDirectory,
            ["TMPDIR"] = TempDirectory,
            ["DOTNET_CLI_HOME"] = HomeDirectory,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_NOLOGO"] = "1",
            ["FAKE_TOOL_LOG"] = LogPath,
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["ALL_PROXY"] = "http://127.0.0.1:9",
            ["NO_PROXY"] = string.Empty,
        };

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }

    /// <summary>Reads the recorded shim invocations.</summary>
    /// <returns>One line per invocation, or an empty string when nothing ran.</returns>
    public string ReadLog() => File.Exists(LogPath) ? File.ReadAllText(LogPath) : string.Empty;

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
