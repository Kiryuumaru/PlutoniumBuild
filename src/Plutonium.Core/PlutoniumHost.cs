namespace Plutonium.Core;

/// <summary>
/// CI host kind. Local is the fallback when no CI host is detected.
/// Carries no entry, AppId, or tag knowledge.
/// </summary>
public enum PlutoniumHostKind
{
    /// <summary>Local developer machine; no CI host detected.</summary>
    Local = 0,

    /// <summary>GitHub Actions (<c>GITHUB_ACTIONS</c> is set to true).</summary>
    GitHubActions = 1,

    /// <summary>Azure Pipelines (<c>TF_BUILD</c> is set to True).</summary>
    AzurePipelines = 2,
}

/// <summary>
/// Host detection plus CI grouping and variable-export primitives.
/// Groups collapse on CI hosts and degrade to plain log lines on Local.
/// </summary>
public sealed class PlutoniumHost
{
    private readonly IPlutoniumLogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumHost"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for Local group lines. May be null.</param>
    public PlutoniumHost(IPlutoniumLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the detected host kind.
    /// </summary>
    public PlutoniumHostKind Kind => Detect();

    /// <summary>
    /// Detects the current host from environment markers.
    /// </summary>
    /// <returns>GitHub Actions when <c>GITHUB_ACTIONS</c> is true, Azure Pipelines when <c>TF_BUILD</c> is True, else Local.</returns>
    public static PlutoniumHostKind Detect()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return PlutoniumHostKind.GitHubActions;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("TF_BUILD"), "True", StringComparison.OrdinalIgnoreCase))
        {
            return PlutoniumHostKind.AzurePipelines;
        }

        return PlutoniumHostKind.Local;
    }

    /// <summary>
    /// Opens a named log group; disposing the scope closes it.
    /// </summary>
    /// <param name="name">The group name. Must not be null or whitespace.</param>
    /// <returns>A disposable that closes the group. Never null.</returns>
    public IDisposable BeginGroup(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();

        switch (Detect())
        {
            case PlutoniumHostKind.GitHubActions:
                Console.WriteLine($"::group::{trimmed}");
                return new GroupScope(static () => Console.WriteLine("::endgroup::"));
            case PlutoniumHostKind.AzurePipelines:
                Console.WriteLine($"##[group]{trimmed}");
                return new GroupScope(static () => Console.WriteLine("##[endgroup]"));
            default:
                _logger?.Log(PlutoniumLogLevel.Information, $"--- {trimmed} ---");
                return new GroupScope(() => _logger?.Log(PlutoniumLogLevel.Information, $"--- end {trimmed} ---"));
        }
    }

    /// <summary>
    /// Exports a variable to the current process and, on CI hosts, to subsequent steps.
    /// </summary>
    /// <remarks>
    /// On GitHub Actions the value is appended to the <c>GITHUB_ENV</c> file when present.
    /// On Azure Pipelines a <c>##vso[task.setvariable]</c> line is written to stdout.
    /// On Local only the current process environment is updated.
    /// Multiline values are rejected because neither channel carries them safely.
    /// </remarks>
    /// <param name="name">The variable name. Must not be null or whitespace.</param>
    /// <param name="value">The variable value. Must not be null and must not contain carriage return or newline.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> contains carriage return or newline.</exception>
    public void ExportVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Variable value must not contain carriage return or newline.", nameof(value));
        }

        var trimmed = name.Trim();

        Environment.SetEnvironmentVariable(trimmed, value);

        if (Detect() == PlutoniumHostKind.GitHubActions)
        {
            var envFile = Environment.GetEnvironmentVariable("GITHUB_ENV");
            if (!string.IsNullOrWhiteSpace(envFile))
            {
                File.AppendAllText(envFile, $"{trimmed}={value}{Environment.NewLine}");
            }
        }
        else if (Detect() == PlutoniumHostKind.AzurePipelines)
        {
            Console.WriteLine($"##vso[task.setvariable variable={trimmed}]{value}");
        }
    }

    /// <summary>
    /// Exports a job output to the current process and, on CI hosts, to downstream jobs.
    /// </summary>
    /// <remarks>
    /// On GitHub Actions the value is appended to the <c>GITHUB_OUTPUT</c> file when present,
    /// else only the current process environment is updated.
    /// On Azure Pipelines a <c>##vso[task.setvariable variable={name};isOutput=true]</c> line
    /// is written to stdout; the producing step's <c>name:</c> is the output-reference key.
    /// On Local only the current process environment is updated.
    /// The name and value stay opaque and carry no entry or AppId knowledge.
    /// Multiline values are rejected because neither channel carries them safely.
    /// </remarks>
    /// <param name="name">The output name. Must not be null or whitespace.</param>
    /// <param name="value">The output value. Must not be null and must not contain carriage return or newline.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> contains carriage return or newline.</exception>
    public void ExportJobOutput(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Output value must not contain carriage return or newline.", nameof(value));
        }

        var trimmed = name.Trim();

        Environment.SetEnvironmentVariable(trimmed, value);

        if (Detect() == PlutoniumHostKind.GitHubActions)
        {
            var outputFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                File.AppendAllText(outputFile, $"{trimmed}={value}{Environment.NewLine}");
            }
        }
        else if (Detect() == PlutoniumHostKind.AzurePipelines)
        {
            Console.WriteLine($"##vso[task.setvariable variable={trimmed};isOutput=true]{value}");
        }
    }

    private sealed class GroupScope(Action close) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                close();
            }
        }
    }
}
