using System.Diagnostics;

namespace Plutonium.Helpers.Tests;

/// <summary>
/// Hermetic git fixture: a work repository with a bare origin under one temporary
/// root. Identity, signing, and global config are pinned so commits and tags run
/// without user configuration. Every command throws when git exits non-zero.
/// </summary>
internal sealed class GitSandbox : IDisposable
{
    private GitSandbox(string root)
    {
        Root = root;
        Repository = Path.Combine(root, "repo");
        Origin = Path.Combine(root, "origin.git");
    }

    /// <summary>Gets the sandbox root directory.</summary>
    public string Root { get; }

    /// <summary>Gets the work repository directory.</summary>
    public string Repository { get; }

    /// <summary>Gets the bare origin directory.</summary>
    public string Origin { get; }

    /// <summary>Creates a sandbox with an initial origin-pushed commit on <c>main</c>.</summary>
    /// <returns>The sandbox. Never null.</returns>
    public static GitSandbox Create()
    {
        var sandbox = new GitSandbox(Path.Combine(Path.GetTempPath(), "plutonium-git-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(sandbox.Root);
        sandbox.Initialize();
        return sandbox;
    }

    /// <summary>Gets the current repository HEAD commit.</summary>
    public string Head => Run(Repository, "rev-parse", "HEAD").StandardOutput;

    /// <summary>Creates and pushes a commit with one unique file.</summary>
    /// <param name="message">The commit message.</param>
    public void Commit(string message)
    {
        File.WriteAllText(Path.Combine(Repository, Guid.NewGuid().ToString("N") + ".txt"), message);
        Run(Repository, "add", "-A");
        Run(Repository, "commit", "-q", "-m", message);
    }

    /// <summary>Creates or moves lightweight tags at HEAD without pushing.</summary>
    /// <param name="tags">The tag names.</param>
    public void Tag(params string[] tags)
    {
        foreach (var tag in tags)
        {
            Run(Repository, "tag", "-f", tag);
        }
    }

    /// <summary>Creates or moves a lightweight tag at HEAD and pushes it to origin.</summary>
    /// <param name="tag">The tag name.</param>
    public void TagAndPush(string tag)
    {
        Run(Repository, "tag", "-f", tag);
        Run(Repository, "push", "-q", "origin", "refs/tags/" + tag);
    }

    /// <summary>Lists origin tag names.</summary>
    /// <returns>The tag names. Never null.</returns>
    public IReadOnlyList<string> RemoteTags() => Run(Origin, "tag", "--list")
        .StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Resolves a revision in the bare origin.</summary>
    /// <param name="revision">The revision or reference.</param>
    /// <returns>The resolved commit id.</returns>
    public string RemoteRev(string revision) => Run(Origin, "rev-parse", revision).StandardOutput;

    /// <summary>Tests whether origin carries a tag.</summary>
    /// <param name="tag">The tag name.</param>
    /// <returns>True when the tag exists on origin.</returns>
    public bool RemoteTagExists(string tag) =>
        RemoteTags().Contains(tag, StringComparer.Ordinal);

    /// <summary>Runs git with captured output and a pinned identity.</summary>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The captured result. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git exits non-zero or cannot start.</exception>
    public GitCommandResult Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "missing.gitconfig");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_AUTHOR_NAME"] = "Plutonium Tests";
        start.Environment["GIT_AUTHOR_EMAIL"] = "tests@plutonium.invalid";
        start.Environment["GIT_COMMITTER_NAME"] = "Plutonium Tests";
        start.Environment["GIT_COMMITTER_EMAIL"] = "tests@plutonium.invalid";

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("git failed to start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}: {error.Trim()}");
        }

        return new GitCommandResult(output.Trim(), error.Trim());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Initialize()
    {
        Run(Root, "init", "-q", "--bare", Origin);
        Run(Root, "init", "-q", "-b", "main", Repository);
        Run(Repository, "config", "user.name", "Plutonium Tests");
        Run(Repository, "config", "user.email", "tests@plutonium.invalid");
        Run(Repository, "config", "commit.gpgsign", "false");
        Run(Repository, "remote", "add", "origin", Origin);
        Commit("initial");
        Run(Repository, "push", "-q", "origin", "HEAD");
    }
}

/// <summary>Captured stdout and stderr from one successful git command.</summary>
/// <param name="StandardOutput">The trimmed standard output.</param>
/// <param name="StandardError">The trimmed standard error.</param>
internal readonly record struct GitCommandResult(string StandardOutput, string StandardError);
