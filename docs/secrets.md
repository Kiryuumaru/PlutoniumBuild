# Secrets

Declare a secret on your build class, create it in your repository or pipeline with the same name, and PlutoniumBuild resolves it at run time. Resolution happens on every run — local and CI — and resolved values are masked from log output.

## Declare a secret

```csharp
using Plutonium.Helpers.App;

public sealed class MyBuild
{
    [PlutoniumSecretVariable("MY_TOKEN")]
    public string? MyToken { get; private set; }
}
```

The attribute works on instance fields and properties. Leave the member unassigned in code — setup resolves the value from the environment, registers it for masking, and assigns it before your steps run. A member with no setter is not assigned; setup still masks and exports the value and logs a warning. Sources: `src/Plutonium.Helpers/App/PlutoniumSecretVariableAttribute.cs:23-53`, `src/Plutonium.Helpers/App/PlutoniumSecrets.cs:67-105`.

## Scope a secret to entries

On GitHub, a secret with no `EntryIds` applies to every entry job. Set `EntryIds` to limit it to named entries:

```csharp
[PlutoniumSecretVariable("NUGET_KEY", EntryIds = new[] { "my-publish" })]
public string? NugetKey { get; private set; }
```

Entry ids are case-insensitive. An empty `EntryIds` array applies the secret to every entry job. An id that does not match a discovered run entry fails GitHub workflow generation with an error naming the secret and the unknown id. Pre-setup and post-setup always receive every declared secret because they stamp tags and manage the release. Only the GitHub generator applies `EntryIds`; the generated Azure pipeline keeps the token and declared secrets as pipeline variables. Sources: `src/Plutonium.Helpers/App/PlutoniumSecretDeclaration.cs:42-53`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:202-245`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:176-192`.

## Create the secret in CI

Create the secret with the same name you passed to the attribute — `MY_TOKEN` above:

- GitHub: a repository secret named `MY_TOKEN`.
- Azure: a pipeline variable (kept secret) named `MY_TOKEN`.

The generated GitHub workflow attaches the declared secret only to the jobs it applies to: pre-setup and post-setup receive every declared secret, and an entry job receives a secret when it is unscoped or lists that entry. The workflow has no workflow-level secret `env` block, and only pre-setup and post-setup get `GITHUB_TOKEN` in their job environment. Entry checkouts use `persist-credentials: false`; the pre-setup and post-setup checkouts keep credentials so they can push tags and manage the release. The workflow defaults to `permissions: contents: read`; only pre-setup and post-setup add `permissions: contents: write`.

The generated Azure pipeline writes the token and declared secrets as pipeline `variables:` and entry stages consume them from there. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:158-188,383-446,465-486`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:176-192`, `src/Plutonium.Core/Workflows/Emitters/PlutoniumAzureEmitter.cs:144-150`.

## Environment keys

A secret named `MY_TOKEN` resolves from the environment key `PLUTONIUM_MY_TOKEN` — `PLUTONIUM_` plus the secret name. Override the key with the second attribute argument: `[PlutoniumSecretVariable("MY_TOKEN", "MY_ENV")]` reads `MY_ENV`. Source: `src/Plutonium.Helpers/App/PlutoniumSecretDeclaration.cs:36-40`.

The key must not collide with pipeline wiring — `PLUTONIUM_PRE_SETUP`, `PLUTONIUM_ENTRY`, `PLUTONIUM_ALLOW_TAG_WIPE`, `PLUTONIUM_APP_PROJECT`, the pipeline token key (`GITHUB_TOKEN` on GitHub, `SYSTEM_ACCESSTOKEN` on Azure), or another secret's key. A collision fails workflow generation with an error naming the secret and the key. Collision checks are case-insensitive: a key differing from a reserved or already-used key only by letter case (for example `plutonium_pre_setup` versus `PLUTONIUM_PRE_SETUP`) is rejected too, because generated workflows can run on hosts whose environment variable names are case-insensitive (Windows). Source: `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineDetection.cs:155-198,320-326`.

## Run locally

Local runs resolve the same environment keys. Set the variable yourself, for example `PLUTONIUM_MY_TOKEN=... mybuild shell Run`. When the value is empty, setup logs `Secret variable PLUTONIUM_MY_TOKEN was not resolved.` and leaves the member unset. Local environment variables must use the new `PLUTONIUM_<NAME>` key: a `NUKE_*` variable from an earlier setup no longer resolves and logs the warning above. Sources: `src/Plutonium.Helpers/App/PlutoniumSecrets.cs:74-88`, `src/Plutonium.Shell/PlutoniumShellState.cs:105-109`.

## Masking

Once a value is registered, log lines and terminal call output replace it with `***`. Masking covers the run log and captured tool output; it does not inspect files or artifacts your own steps write. Sources: `src/Plutonium.Core/Logging/Logging.cs:21-55,126-141`, `src/Plutonium.Core/Process/PlutoniumProcessRunner.cs:194-195`.

## Regenerate after secret changes

After you add, rename, re-key, or rescope a secret, regenerate the workflow files with `shell github-workflow` / `shell azure-workflow` — the secret environment and job scope are part of the generated files. Secret names in your repository or pipeline do not change. Generated environment keys use the `PLUTONIUM_` prefix: a file generated before the prefix change carried the older `NUKE_<NAME>` key and must be regenerated. See [Workflows](workflows.md).
