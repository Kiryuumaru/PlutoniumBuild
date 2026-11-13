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
/// One staged release asset: the staged path plus every computed digest.
/// </summary>
public sealed class PlutoniumReleaseAsset
{
    /// <summary>Gets the staged asset path. Never null.</summary>
    public PlutoniumPath Path { get; }

    /// <summary>Gets the asset file name. Never null.</summary>
    public string FileName { get; }

    /// <summary>Gets whether the source was a directory zipped before staging.</summary>
    public bool IsArchive { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumReleaseAsset"/> class.
    /// </summary>
    /// <param name="path">The staged path. Must not be null.</param>
    /// <param name="fileName">The file name. Must not be null or whitespace.</param>
    /// <param name="isArchive">Whether the source was a directory.</param>
    public PlutoniumReleaseAsset(PlutoniumPath path, string fileName, bool isArchive)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Path = path;
        FileName = fileName.Trim();
        IsArchive = isArchive;
    }
}
