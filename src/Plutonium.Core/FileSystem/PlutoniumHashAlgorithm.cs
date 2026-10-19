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
/// Hash algorithms available for file hashing.
/// Covers the digest set release notes append (MD5, SHA-1, SHA-256, SHA-512).
/// </summary>
public enum PlutoniumHashAlgorithm
{
    /// <summary>MD5 digest.</summary>
    Md5 = 0,

    /// <summary>SHA-1 digest.</summary>
    Sha1 = 1,

    /// <summary>SHA-256 digest.</summary>
    Sha256 = 2,

    /// <summary>SHA-512 digest.</summary>
    Sha512 = 3,
}
