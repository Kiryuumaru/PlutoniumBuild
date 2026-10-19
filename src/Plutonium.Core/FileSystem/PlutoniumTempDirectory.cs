using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.FileSystem;

/// <summary>
/// Self-cleaning temp directory scope: creates a unique directory under the
/// system temp root and deletes it recursively on dispose.
/// </summary>
public sealed class PlutoniumTempDirectory : IDisposable
{
    private bool _disposed;

    /// <summary>Gets the temp directory path. Never null.</summary>
    public PlutoniumPath Directory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumTempDirectory"/> class.
    /// Creates a unique directory under <see cref="Path.GetTempPath"/>.
    /// </summary>
    public PlutoniumTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "plutonium-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        Directory = new PlutoniumPath(path);
    }

    /// <summary>
    /// Deletes the temp directory recursively; a missing directory is tolerated.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (System.IO.Directory.Exists(Directory.Value))
            {
                System.IO.Directory.Delete(Directory.Value, recursive: true);
            }
        }
    }
}
