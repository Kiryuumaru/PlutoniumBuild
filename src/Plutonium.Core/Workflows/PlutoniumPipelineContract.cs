using System.Text.Json;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Workflows;

/// <summary>
/// Pipeline pre-setup contract: base64-JSON environment payload helpers plus
/// per-job output naming. The payload is opaque JSON text; Helpers owns its schema.
/// </summary>
/// <remarks>
/// Successor to the <c>NUKE_PRE_SETUP</c> base64-JSON contract seen in the inspirations;
/// Plutonium uses <c>PLUTONIUM_PRE_SETUP</c>. <c>NUKE_PRE_SETUP</c> is a legacy
/// inspiration reference only and is never read or written here.
/// </remarks>
public static class PlutoniumPipelineContract
{
    /// <summary>Gets the environment variable carrying the pre-setup payload.</summary>
    public const string PayloadVariableName = "PLUTONIUM_PRE_SETUP";

    /// <summary>Gets the environment variable carrying the entry id a pipeline entry job runs.</summary>
    public const string EntryVariableName = "PLUTONIUM_ENTRY";

    /// <summary>
    /// Builds the environment variable carrying one entry's normalized run result.
    /// </summary>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <returns>The variable name (for example <c>PLUTONIUM_RUN_RESULT_MY_JOB</c>). Never null.</returns>
    public static string RunResultVariableName(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        return "PLUTONIUM_RUN_RESULT_" + ToUpperName(entryId);
    }

    /// <summary>
    /// Builds the environment variable carrying one entry's raw host run result.
    /// </summary>
    /// <param name="host">The host suffix (for example <c>GITHUB</c>). Must not be null or whitespace.</param>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <returns>The variable name (for example <c>PLUTONIUM_RUN_RESULT_GITHUB_MY_JOB</c>). Never null.</returns>
    public static string HostRunResultVariableName(string host, string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        return "PLUTONIUM_RUN_RESULT_" + ToUpperName(host) + "_" + ToUpperName(entryId);
    }

    /// <summary>
    /// Encodes opaque JSON text as base64 UTF-8 for transport in an environment variable.
    /// </summary>
    /// <param name="json">Opaque JSON text. Must not be null or whitespace and must parse as JSON.</param>
    /// <returns>Base64 payload. Never null.</returns>
    public static string EncodePayload(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var trimmed = json.Trim();
        using var parsed = JsonDocument.Parse(trimmed);
        _ = parsed;
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(trimmed));
    }

    /// <summary>
    /// Decodes a base64 UTF-8 payload back to opaque JSON text.
    /// </summary>
    /// <param name="payload">Base64 payload. Must not be null or whitespace and must decode to JSON.</param>
    /// <returns>Opaque JSON text. Never null.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="payload"/> is not valid base64 or not valid JSON.</exception>
    public static string DecodePayload(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        string json;
        try
        {
            json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.Trim()));
        }
        catch (FormatException ex)
        {
            throw new FormatException("Pre-setup payload is not valid base64.", ex);
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            _ = parsed;
        }
        catch (JsonException ex)
        {
            throw new FormatException("Pre-setup payload did not decode to valid JSON.", ex);
        }

        return json;
    }

    /// <summary>
    /// Reads the payload from the environment.
    /// </summary>
    /// <param name="variableName">The variable name. Must not be null or whitespace; defaults to <see cref="PayloadVariableName"/>.</param>
    /// <returns>The opaque JSON text. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the variable is missing or empty.</exception>
    public static string ReadFromEnvironment(string variableName = PayloadVariableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        var payload = Environment.GetEnvironmentVariable(variableName.Trim());
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException($"Environment variable '{variableName.Trim()}' is missing or empty.");
        }

        return DecodePayload(payload);
    }

    /// <summary>
    /// Writes the payload to the current process environment.
    /// </summary>
    /// <param name="json">Opaque JSON text. Must not be null or whitespace.</param>
    /// <param name="variableName">The variable name. Must not be null or whitespace; defaults to <see cref="PayloadVariableName"/>.</param>
    public static void WriteToEnvironment(string json, string variableName = PayloadVariableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        Environment.SetEnvironmentVariable(variableName.Trim(), EncodePayload(json));
    }

    /// <summary>
    /// Builds the per-job output name for a matrix field.
    /// </summary>
    /// <param name="jobId">The job id. Must not be null or whitespace.</param>
    /// <param name="field">The field name. Must not be null or whitespace.</param>
    /// <returns>The output name (for example <c>BUILD_MATRIX</c>). Never null.</returns>
    public static string OutputName(string jobId, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return ToUpperName(jobId) + "_" + ToUpperName(field);
    }

    /// <summary>
    /// Builds the condition output name for a job.
    /// </summary>
    /// <param name="jobId">The job id. Must not be null or whitespace.</param>
    /// <returns>The condition output name. Never null.</returns>
    public static string ConditionOutputName(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        return OutputName(jobId, "CONDITION");
    }

    /// <summary>
    /// Builds the run-script output name for a job.
    /// </summary>
    /// <param name="jobId">The job id. Must not be null or whitespace.</param>
    /// <returns>The run-script output name. Never null.</returns>
    public static string RunScriptOutputName(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        return OutputName(jobId, "RUN_SCRIPT");
    }

    /// <summary>
    /// Builds the runs-on output name for a job.
    /// </summary>
    /// <param name="jobId">The job id. Must not be null or whitespace.</param>
    /// <returns>The runs-on output name. Never null.</returns>
    public static string RunsOnOutputName(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        return OutputName(jobId, "RUNS_ON");
    }

    private static string ToUpperName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }

        var name = builder.ToString().Trim('_');
        if (name.Length == 0)
        {
            throw new ArgumentException("Value must contain at least one letter or digit.", nameof(value));
        }

        return name;
    }
}
