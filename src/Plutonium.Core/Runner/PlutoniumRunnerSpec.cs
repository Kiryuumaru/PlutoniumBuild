using Plutonium.Core;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Runner;

/// <summary>
/// Opaque runner-OS descriptor for future workflow emitters:
/// a display name, an opaque OS payload (JSON), and the run script.
/// Carries no entry, AppId, or tag knowledge; a future emitter maps this onto RunnerOS.
/// </summary>
/// <param name="Name">The runner name. Must not be null or whitespace.</param>
/// <param name="OsPayloadJson">Opaque OS payload as JSON. Must not be null.</param>
/// <param name="RunScript">The run script. Must not be null or whitespace.</param>
public sealed record PlutoniumRunnerSpec(string Name, string OsPayloadJson, string RunScript)
{
    /// <summary>Gets the runner name. Never null or whitespace.</summary>
    public string Name { get; init; } = ValidateName(Name);

    /// <summary>Gets the opaque OS payload as JSON. Never null.</summary>
    public string OsPayloadJson { get; init; } = ValidatePayload(OsPayloadJson);

    /// <summary>Gets the run script. Never null or whitespace.</summary>
    public string RunScript { get; init; } = ValidateScript(RunScript);

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    private static string ValidatePayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload;
    }

    private static string ValidateScript(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return script.Trim();
    }
}
