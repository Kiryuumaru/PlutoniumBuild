// Routing table: one command class per verb + alias, so adding a verb means
// adding a class here plus one AddCommand line in PlutoniumShellExtensions.
// Local verbs (Fetch/Bump/Run/Test/Build/Publish/Version/Interactive/StatusWatch)
// use PascalCase primary names; workflow verbs
// (github-workflow/azure-workflow/pipeline-pre-setup/pipeline-post-setup/
// pipeline-entry-run/delete-origin-tags)
// use kebab primary names with Pascal aliases. The const value "GithubWorkflow"
// in Helpers/App/PlutoniumBuildVerbs.cs is a frozen wire name: both the kebab
// primary and the legacy GithubWorkflow spelling must keep routing, and a future
// normalize-casing pass must not change it.
using ApplicationBuilderHelpers.Attributes;

namespace Plutonium.Shell;

/// <summary>Fetch commits and tags from origin.</summary>
[Command("shell Fetch", "Fetch commits and tags from origin.")]
public sealed class PlutoniumFetchCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Fetch;
}

/// <summary>Fetch, reconcile tags, print per-app version tables.</summary>
[Command("shell Version", "Fetch, reconcile tags, print per-app version tables.")]
public sealed class PlutoniumVersionCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Version;
}

/// <summary>Bump versions with --args, then watch.</summary>
[Command("shell Bump", "Bump versions with --args {appid=[major|minor|patch|prerelease][&gt;|+]{n}}, then watch.")]
public sealed class PlutoniumBumpCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Bump;
}

/// <summary>Bump versions without watching.</summary>
[Command("shell BumpAndForget", "Bump versions without watching.")]
public sealed class PlutoniumBumpAndForgetCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.BumpAndForget;
}

/// <summary>Kebab-case alias accepting <c>bump-and-forget</c> for <c>BumpAndForget</c>.</summary>
[Command("shell bump-and-forget", "Bump versions without watching.")]
public sealed class PlutoniumBumpAndForgetAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.BumpAndForget;
}

/// <summary>Attach to in-flight runs for the current commit.</summary>
[Command("shell StatusWatch", "Attach to in-flight runs for the current commit.")]
public sealed class PlutoniumStatusWatchCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.StatusWatch;
}

/// <summary>Kebab-case alias accepting <c>status-watch</c> for <c>StatusWatch</c>.</summary>
[Command("shell status-watch", "Attach to in-flight runs for the current commit.")]
public sealed class PlutoniumStatusWatchAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.StatusWatch;
}

/// <summary>Run all local entries in DAG order.</summary>
[Command("shell Run", "Run all local entries in DAG order.")]
public sealed class PlutoniumRunCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Run;
}

/// <summary>Run local test entries.</summary>
[Command("shell Test", "Run local test entries.")]
public sealed class PlutoniumTestCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Test;
}

/// <summary>Run local build entries.</summary>
[Command("shell Build", "Run local build entries.")]
public sealed class PlutoniumBuildCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Build;
}

/// <summary>Run local publish entries.</summary>
[Command("shell Publish", "Run local publish entries.")]
public sealed class PlutoniumPublishCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Publish;
}

/// <summary>Regenerate the GitHub workflow file.</summary>
[Command("shell github-workflow", "Regenerate the GitHub workflow file.")]
public sealed class PlutoniumGitHubWorkflowCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.GitHubWorkflow;
}

/// <summary>Legacy-name alias accepting <c>GithubWorkflow</c> for the GitHub workflow verb.</summary>
[Command("shell GithubWorkflow", "Regenerate the GitHub workflow file.")]
public sealed class PlutoniumGithubWorkflowAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.GitHubWorkflow;
}

/// <summary>Regenerate the Azure workflow file.</summary>
[Command("shell azure-workflow", "Regenerate the Azure workflow file.")]
public sealed class PlutoniumAzureWorkflowCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.AzureWorkflow;
}

/// <summary>PascalCase alias accepting <c>AzureWorkflow</c> for the Azure workflow verb.</summary>
[Command("shell AzureWorkflow", "Regenerate the Azure workflow file.")]
public sealed class PlutoniumAzureWorkflowAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.AzureWorkflow;
}

/// <summary>Pipeline-internal pre-setup.</summary>
[Command("shell pipeline-pre-setup", "Pipeline-internal pre-setup.")]
public sealed class PlutoniumPipelinePreSetupCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelinePreSetup;
}

/// <summary>PascalCase alias accepting <c>PipelinePreSetup</c> for the pre-setup verb.</summary>
[Command("shell PipelinePreSetup", "Pipeline-internal pre-setup.")]
public sealed class PlutoniumPipelinePreSetupAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelinePreSetup;
}

/// <summary>Pipeline-internal post-setup.</summary>
[Command("shell pipeline-post-setup", "Pipeline-internal post-setup.")]
public sealed class PlutoniumPipelinePostSetupCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelinePostSetup;
}

/// <summary>PascalCase alias accepting <c>PipelinePostSetup</c> for the post-setup verb.</summary>
[Command("shell PipelinePostSetup", "Pipeline-internal post-setup.")]
public sealed class PlutoniumPipelinePostSetupAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelinePostSetup;
}

/// <summary>Pipeline-internal entry run.</summary>
[Command("shell pipeline-entry-run", "Pipeline-internal entry run.")]
public sealed class PlutoniumPipelineEntryRunCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelineEntryRun;
}

/// <summary>PascalCase alias accepting <c>PipelineEntryRun</c> for the entry-run verb.</summary>
[Command("shell PipelineEntryRun", "Pipeline-internal entry run.")]
public sealed class PlutoniumPipelineEntryRunAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.PipelineEntryRun;
}

/// <summary>Delete origin tags for --args apps only.</summary>
[Command("shell delete-origin-tags", "Delete origin tags for --args apps only; no-args wipe requires PLUTONIUM_ALLOW_TAG_WIPE=true.")]
public sealed class PlutoniumDeleteOriginTagsCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.DeleteOriginTags;
}

/// <summary>PascalCase alias accepting <c>DeleteOriginTags</c> for the tag-deletion verb.</summary>
[Command("shell DeleteOriginTags", "Delete origin tags for --args apps only; no-args wipe requires PLUTONIUM_ALLOW_TAG_WIPE=true.")]
public sealed class PlutoniumDeleteOriginTagsAliasCommand : PlutoniumShellGroup
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.DeleteOriginTags;
}
