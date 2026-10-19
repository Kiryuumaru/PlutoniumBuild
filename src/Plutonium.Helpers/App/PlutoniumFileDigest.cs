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
/// One file digest: the algorithm display name plus the lowercase hex value.
/// </summary>
public sealed class PlutoniumFileDigest
{
    /// <summary>Gets the algorithm display name (for example <c>SHA-256</c>). Never null.</summary>
    public string AlgorithmName { get; }

    /// <summary>Gets the lowercase hex digest. Never null.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumFileDigest"/> class.
    /// </summary>
    /// <param name="algorithmName">The algorithm name. Must not be null or whitespace.</param>
    /// <param name="value">The digest. Must not be null or whitespace.</param>
    public PlutoniumFileDigest(string algorithmName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        AlgorithmName = algorithmName.Trim();
        Value = value.Trim();
    }
}
