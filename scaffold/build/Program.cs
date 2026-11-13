using ApplicationBuilderHelpers;
using Plutonium.Core.FileSystem;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Shell;

var repositoryRoot = Setting("PLUTONIUM_REPOSITORY_ROOT", Directory.GetCurrentDirectory());
var stateRoot = Path.Combine(repositoryRoot, "build", "temp");
var options = new PlutoniumBuildOptions(
    Setting("PLUTONIUM_ENVIRONMENT_BRANCHES", "main")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    Setting("PLUTONIUM_MAIN_BRANCH", "main"),
    new PlutoniumPath(repositoryRoot),
    new PlutoniumPath(Path.Combine(stateRoot, "scratch")),
    new PlutoniumPath(Path.Combine(stateRoot, "output")),
    new PlutoniumPath(Path.Combine(stateRoot, "cache")),
    Setting("PLUTONIUM_REPOSITORY_URL", repositoryRoot));

var builder = ApplicationBuilder.Create();
builder.AddPlutoniumShell(new SampleBuild(), options);

// Launchers call verbs directly (build.sh Test); the shell registers them under "shell".
var commandArgs = args.Length > 0 && args[0] != "shell" && !args[0].StartsWith('-')
    ? new[] { "shell" }.Concat(args).ToArray()
    : args;
return await builder.RunAsync(commandArgs);

static string Setting(string name, string fallback)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
