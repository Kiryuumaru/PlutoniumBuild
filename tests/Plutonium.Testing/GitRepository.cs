namespace Plutonium.Testing;

/// <summary>
/// A disposable git repository used as a scaffold source or as a consumer
/// working tree. Commits carry fixed identities so tests never depend on the
/// developer's git configuration.
/// </summary>
public sealed class GitRepository
{
    private const string AuthorName = "PlutoniumBuild Tests";
    private const string AuthorEmail = "tests@plutoniumbuild.invalid";

    private GitRepository(string rootPath)
    {
        RootPath = rootPath;
    }

    /// <summary>Gets the absolute path of the working tree.</summary>
    public string RootPath { get; }

    /// <summary>Gets the <c>file://</c> URL used when a script expects a git URL.</summary>
    public string FileUrl => new Uri(RootPath + Path.DirectorySeparatorChar).AbsoluteUri;

    /// <summary>Gets the current commit SHA.</summary>
    public string HeadSha => RunRequired("rev-parse", "HEAD").StandardOutput.Trim();

    /// <summary>Creates and initializes a repository with the requested default branch.</summary>
    /// <param name="workspace">The workspace that owns the repository.</param>
    /// <param name="name">A short name for the repository directory.</param>
    /// <param name="defaultBranch">The branch the initial <c>HEAD</c> points at.</param>
    /// <returns>The initialized repository.</returns>
    public static GitRepository Create(
        TempWorkspace workspace,
        string name,
        string defaultBranch = "main")
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBranch);

        var rootPath = workspace.PathFor("repos", name);
        Directory.CreateDirectory(rootPath);
        var repository = new GitRepository(rootPath);
        repository.RunRequired("init", "-b", defaultBranch);
        repository.RunRequired("config", "user.name", AuthorName);
        repository.RunRequired("config", "user.email", AuthorEmail);
        return repository;
    }

    /// <summary>Writes a UTF-8 text file, creating parent directories as needed.</summary>
    /// <param name="relativePath">The path relative to the repository root.</param>
    /// <param name="content">The file content.</param>
    public void WriteFile(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>Copies a directory tree into the repository.</summary>
    /// <param name="sourceDirectory">The directory to copy.</param>
    /// <param name="relativeTargetPath">The target path relative to the repository root.</param>
    public void CopyDirectory(string sourceDirectory, string relativeTargetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeTargetPath);

        var targetDirectory = Path.Combine(RootPath, relativeTargetPath);
        CopyTree(sourceDirectory, targetDirectory);
    }

    /// <summary>Stages every change and commits it.</summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The new commit SHA.</returns>
    public string Commit(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        RunRequired("add", "--all");
        RunRequired(
            "-c",
            "commit.gpgsign=false",
            "commit",
            "--allow-empty",
            "--no-verify",
            "--quiet",
            "-m",
            message);
        return HeadSha;
    }

    /// <summary>Creates and checks out a branch at <c>HEAD</c>.</summary>
    /// <param name="name">The branch name.</param>
    public void CreateBranch(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunRequired("checkout", "-q", "-b", name);
    }

    /// <summary>Creates a lightweight tag at <c>HEAD</c>.</summary>
    /// <param name="name">The tag name.</param>
    /// <returns>The tagged commit SHA.</returns>
    public string CreateLightweightTag(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunRequired("tag", name);
        return ResolveCommit("refs/tags/" + name);
    }

    /// <summary>Creates an annotated tag at <c>HEAD</c>.</summary>
    /// <param name="name">The tag name.</param>
    /// <param name="message">The tag message.</param>
    /// <returns>The tagged commit SHA.</returns>
    public string CreateAnnotatedTag(string name, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        RunRequired("-c", "tag.gpgsign=false", "tag", "-a", name, "-m", message);
        return ResolveCommit("refs/tags/" + name);
    }

    /// <summary>Resolves the object a ref points at without peeling annotated tags.</summary>
    /// <param name="refName">The ref to resolve.</param>
    /// <returns>The object SHA.</returns>
    public string ResolveObject(string refName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        return RunRequired("rev-parse", refName).StandardOutput.Trim();
    }

    /// <summary>Resolves the commit a ref ultimately points at, peeling annotated tags.</summary>
    /// <param name="refName">The ref to resolve.</param>
    /// <returns>The commit SHA.</returns>
    public string ResolveCommit(string refName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        return RunRequired("rev-parse", refName + "^{commit}").StandardOutput.Trim();
    }

    /// <summary>Runs a git command and returns its captured result.</summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The captured result.</returns>
    public ProcessResult Run(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var allArguments = new List<string>(arguments.Length + 2) { "-C", RootPath };
        allArguments.AddRange(arguments);
        return ProcessHarness.RunAsync(
                new ProcessRequest(
                    "git",
                    allArguments,
                    RootPath,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["GIT_AUTHOR_NAME"] = AuthorName,
                        ["GIT_AUTHOR_EMAIL"] = AuthorEmail,
                        ["GIT_COMMITTER_NAME"] = AuthorName,
                        ["GIT_COMMITTER_EMAIL"] = AuthorEmail,
                        ["GIT_CONFIG_NOSYSTEM"] = "1",
                        ["GIT_TERMINAL_PROMPT"] = "0",
                    }))
            .GetAwaiter()
            .GetResult();
    }

    private ProcessResult RunRequired(params string[] arguments)
    {
        var result = Run(arguments);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "git " + string.Join(' ', arguments) + " failed." + Environment.NewLine + result.Describe());
        }

        return result;
    }

    private static void CopyTree(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(
                    Path.GetFileName(directory),
                    ".git",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyTree(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }
    }
}
