using System.Text.Json;
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
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines.RunnerOS;

/// <summary>
/// Runner operating-system abstraction: entries declare the OS once and every
/// pipeline emitter renders it. Helpers owns the OS knowledge and maps each OS
/// onto the opaque Core <see cref="PlutoniumRunnerSpec"/>; Core stays opaque.
/// </summary>
public abstract class PlutoniumRunnerOS
{
    /// <summary>Gets the latest Ubuntu runner OS.</summary>
    public static PlutoniumRunnerOS UbuntuLatest { get; } = new PlutoniumRunnerOSUbuntuLatest();

    /// <summary>Gets the Ubuntu 22.04 runner OS.</summary>
    public static PlutoniumRunnerOS Ubuntu2204 { get; } = new PlutoniumRunnerOSUbuntu2204();

    /// <summary>Gets the Ubuntu 20.04 runner OS.</summary>
    public static PlutoniumRunnerOS Ubuntu2004 { get; } = new PlutoniumRunnerOSUbuntu2004();

    /// <summary>Gets the latest Windows runner OS.</summary>
    public static PlutoniumRunnerOS WindowsLatest { get; } = new PlutoniumRunnerOSWindowsLatest();

    /// <summary>Gets the Windows 2022 runner OS.</summary>
    public static PlutoniumRunnerOS Windows2022 { get; } = new PlutoniumRunnerOSWindows2022();

    /// <summary>Gets the Windows 2019 runner OS.</summary>
    public static PlutoniumRunnerOS Windows2019 { get; } = new PlutoniumRunnerOSWindows2019();

    /// <summary>Gets the runner name (for example <c>ubuntu-22.04</c>). Never null.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Maps this OS onto the opaque Core runner spec for the given pipeline.
    /// GitHub payloads carry <c>{runsOn}</c>; Azure payloads carry <c>{poolName, vmImage}</c>.
    /// Local intentionally reuses the GitHub shape as a fallback; no CI upload.
    /// </summary>
    /// <param name="pipelineType">The pipeline to render for.</param>
    /// <returns>The runner spec. Never null.</returns>
    public PlutoniumRunnerSpec GetPipelineOS(PlutoniumPipelineType pipelineType) => pipelineType switch
    {
        PlutoniumPipelineType.GitHub => new PlutoniumRunnerSpec(
            Name,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["runsOn"] = Name }),
            GetRunScript(pipelineType)),
        PlutoniumPipelineType.Azure => new PlutoniumRunnerSpec(
            Name,
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["poolName"] = "Azure Pipelines",
                ["vmImage"] = Name,
            }),
            GetRunScript(pipelineType)),
        PlutoniumPipelineType.Local => new PlutoniumRunnerSpec(
            Name,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["runsOn"] = Name }),
            GetRunScript(pipelineType)),
        _ => throw new ArgumentOutOfRangeException(nameof(pipelineType), pipelineType, "Unknown pipeline type."),
    };

    /// <summary>
    /// Gets the run script invoking the build for the given pipeline.
    /// </summary>
    /// <param name="pipelineType">The pipeline to render for.</param>
    /// <returns>The run script. Never null.</returns>
    public abstract string GetRunScript(PlutoniumPipelineType pipelineType);
}

/// <summary>Ubuntu latest runner OS.</summary>
public sealed class PlutoniumRunnerOSUbuntuLatest : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "ubuntu-latest";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) =>
        "chmod +x ./build.sh && ./build.sh";
}

/// <summary>Ubuntu 22.04 runner OS.</summary>
public sealed class PlutoniumRunnerOSUbuntu2204 : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "ubuntu-22.04";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) =>
        "chmod +x ./build.sh && ./build.sh";
}

/// <summary>Ubuntu 20.04 runner OS.</summary>
public sealed class PlutoniumRunnerOSUbuntu2004 : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "ubuntu-20.04";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) =>
        "chmod +x ./build.sh && ./build.sh";
}

/// <summary>Windows latest runner OS.</summary>
public sealed class PlutoniumRunnerOSWindowsLatest : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "windows-latest";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) => "./build.cmd";
}

/// <summary>Windows 2022 runner OS.</summary>
public sealed class PlutoniumRunnerOSWindows2022 : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "windows-2022";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) => "./build.cmd";
}

/// <summary>Windows 2019 runner OS.</summary>
public sealed class PlutoniumRunnerOSWindows2019 : PlutoniumRunnerOS
{
    /// <inheritdoc/>
    public override string Name { get; } = "windows-2019";

    /// <inheritdoc/>
    public override string GetRunScript(PlutoniumPipelineType pipelineType) => "./build.cmd";
}
