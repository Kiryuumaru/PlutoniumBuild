using Plutonium.Core.Process;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;

/// <summary>
/// Sample build definition for a repository of any language. Replace the
/// terminal calls with the tools this repository actually uses.
/// </summary>
internal sealed class SampleBuild : PlutoniumBuildBase
{
    private const string AppIdValue = "sample";

    /// <inheritdoc/>
    protected override WorkflowConfigEntry WorkflowConfig =>
        definition => definition.Name("Sample pipeline");

    /// <summary>Gets the test entry.</summary>
    public TestEntry Test => definition => definition
        .AppId(AppIdValue)
        .ExecuteBeforeBuild(true)
        .Execute(async context =>
        {
            Console.WriteLine("Hello from PlutoniumBuild! Running the sample Test step.");
            await RunToolAsync(context, "git", "--version").ConfigureAwait(false);
        });

    /// <summary>Gets the build entry.</summary>
    public BuildEntry Build => definition => definition
        .AppId(AppIdValue)
        .Execute(async context =>
        {
            Console.WriteLine("Hello from PlutoniumBuild! Running the sample Build step.");
            await RunToolAsync(context, "git", "rev-parse --short HEAD").ConfigureAwait(false);
        });

    /// <summary>Gets the publish entry.</summary>
    public PublishEntry Publish => definition => definition
        .AppId(AppIdValue)
        .Execute(async context =>
        {
            Console.WriteLine("Hello from PlutoniumBuild! Running the sample Publish step.");
            await RunToolAsync(context, "git", "describe --always --tags").ConfigureAwait(false);
        });

    private static async Task RunToolAsync(IRunContext context, string fileName, string arguments)
    {
        var result = await context.Terminal
            .RunAsync(new PlutoniumProcessRequest(fileName, arguments))
            .ConfigureAwait(false);
        result.EnsureSucceeded();
    }
}
