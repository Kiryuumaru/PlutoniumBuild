using System.Reflection;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;

namespace Plutonium.Helpers.App;
/// <summary>
/// Marks a build-class field or property as a secret variable. The value is
/// resolved from the environment at setup, registered for Core-side masking,
/// and exported to the pipeline; it is never assigned in code.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PlutoniumSecretVariableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumSecretVariableAttribute"/> class.
    /// </summary>
    /// <param name="secretVariableName">The secret name. Must not be null or whitespace.</param>
    /// <param name="environmentVariableName">Optional environment override. Defaults to <c>NUKE_</c> plus the secret name.</param>
    public PlutoniumSecretVariableAttribute(string secretVariableName, string? environmentVariableName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretVariableName);
        SecretVariableName = secretVariableName.Trim();
        EnvironmentVariableName = environmentVariableName;
    }

    /// <summary>Gets the secret variable name. Never null or whitespace.</summary>
    public string SecretVariableName { get; }

    /// <summary>Gets the environment variable override, if any.</summary>
    public string? EnvironmentVariableName { get; }
}
