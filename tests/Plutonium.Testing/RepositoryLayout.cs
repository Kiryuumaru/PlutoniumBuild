using System.Xml.Linq;

namespace Plutonium.Testing;

/// <summary>
/// Locates the repository from the test assembly location and exposes the paths
/// the end-to-end suites are allowed to read.
/// </summary>
public sealed class RepositoryLayout
{
    private RepositoryLayout(string root)
    {
        Root = root;
    }

    /// <summary>Gets the absolute path of the repository root.</summary>
    public string Root { get; }

    /// <summary>Gets the absolute path of the solution file.</summary>
    public string SolutionPath => Path.Combine(Root, "PlutoniumBuild.sln");

    /// <summary>Gets the absolute path of the package project that publishes <c>PlutoniumBuild</c>.</summary>
    public string PackageProjectPath => Path.Combine(Root, "src", "PlutoniumBuild.Package", "PlutoniumBuild.Package.csproj");

    /// <summary>Gets the absolute path of the scaffold payload installed as a consumer's <c>build/</c> folder.</summary>
    public string ScaffoldBuildDirectory => Path.Combine(Root, "scaffold", "build");

    /// <summary>Gets the absolute path of the scaffold payload installed at a consumer's repository root.</summary>
    public string ScaffoldRootDirectory => Path.Combine(Root, "scaffold", "root");

    /// <summary>Gets the absolute path of the Linux/macOS init script.</summary>
    public string InitScriptPath => Path.Combine(Root, "init.sh");

    /// <summary>Gets the absolute path of the PowerShell init script.</summary>
    public string InitPowerShellPath => Path.Combine(Root, "init.ps1");

    /// <summary>Gets the absolute path of the repo-local bootstrap cache used by the maintainer runner.</summary>
    public string BootstrapCacheDirectory => Path.Combine(Root, ".plutonium", "temp");

    /// <summary>Finds the repository root by walking up from the running test assembly.</summary>
    /// <returns>The located repository layout.</returns>
    /// <exception cref="InvalidOperationException">No ancestor directory contains <c>PlutoniumBuild.sln</c>.</exception>
    public static RepositoryLayout Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlutoniumBuild.sln")))
            {
                return new RepositoryLayout(directory.FullName);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate PlutoniumBuild.sln by walking up from '" + AppContext.BaseDirectory + "'.");
    }

    /// <summary>Reads the builder version pinned in <c>src/Directory.Build.props</c>.</summary>
    /// <returns>The pinned version, or an empty string when the property is absent.</returns>
    public string ReadBuilderVersion()
    {
        var propsPath = Path.Combine(Root, "src", "Directory.Build.props");
        if (!File.Exists(propsPath))
        {
            return string.Empty;
        }

        var document = XDocument.Load(propsPath);
        return document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Version")
            ?.Value
            .Trim() ?? string.Empty;
    }
}
