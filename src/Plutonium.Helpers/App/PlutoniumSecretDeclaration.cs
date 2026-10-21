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
/// One declared secret: the member carrying the attribute plus the attribute itself.
/// </summary>
/// <param name="Member">The field or property. Never null.</param>
/// <param name="Secret">The attribute. Never null.</param>
public sealed record PlutoniumSecretDeclaration(MemberInfo Member, PlutoniumSecretVariableAttribute Secret)
{
    /// <summary>Gets the field or property. Never null.</summary>
    public MemberInfo Member { get; init; } = Member ?? throw new ArgumentNullException(nameof(Member));

    /// <summary>Gets the attribute. Never null.</summary>
    public PlutoniumSecretVariableAttribute Secret { get; init; } = Secret ?? throw new ArgumentNullException(nameof(Secret));

    /// <summary>Gets the environment variable name: the override or <c>PLUTONIUM_</c> plus the secret name.</summary>
    public string EnvironmentVariableName =>
        string.IsNullOrWhiteSpace(Secret.EnvironmentVariableName)
            ? "PLUTONIUM_" + Secret.SecretVariableName
            : Secret.EnvironmentVariableName.Trim();

    /// <summary>Gets whether the secret is declared for the given entry id, or for every entry when unscoped.</summary>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <returns>True when the secret applies to the entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the attribute entry scope is null.</exception>
    public bool AppliesToEntry(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var entryIds = Secret.EntryIds
            ?? throw new InvalidOperationException($"Secret '{Secret.SecretVariableName}' has a null entry scope.");
        return entryIds.Length == 0 ||
            entryIds.Contains(entryId.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
