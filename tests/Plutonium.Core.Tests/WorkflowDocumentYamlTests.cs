using Xunit;

namespace Plutonium.Core.Tests;

/// <summary>
/// Verifies job outputs render verbatim in workflow YAML without quoting.
/// </summary>
public sealed class WorkflowDocumentYamlTests
{
    /// <summary>
    /// Verifies GitHub step-output expressions stay unquoted in the outputs block.
    /// </summary>
    [Fact]
    public void WriteYaml_EmitsOutputExpressionsVerbatim()
    {
        var job = new PlutoniumWorkflowJob(
            "PRE_SETUP",
            "ubuntu-22.04",
            ["- run: echo"],
            outputs: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PRE_SETUP_PLUTONIUM_PRE_SETUP"] = "${{ steps.PRE_SETUP_RUN.outputs.PRE_SETUP_PLUTONIUM_PRE_SETUP }}",
                ["MY_JOB_CONDITION"] = "${{ steps.PRE_SETUP_RUN.outputs.MY_JOB_CONDITION }}",
            });
        var document = new PlutoniumWorkflowDocument("Pipe", "push:\n  branches:\n    - 'main'", [job]);

        var yaml = document.WriteYaml();

        Assert.Contains("PRE_SETUP_PLUTONIUM_PRE_SETUP: ${{ steps.PRE_SETUP_RUN.outputs.PRE_SETUP_PLUTONIUM_PRE_SETUP }}", yaml, StringComparison.Ordinal);
        Assert.Contains("MY_JOB_CONDITION: ${{ steps.PRE_SETUP_RUN.outputs.MY_JOB_CONDITION }}", yaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Azure dependency expressions stay unquoted in the outputs block.
    /// </summary>
    [Fact]
    public void WriteYaml_EmitsAzureOutputExpressionsVerbatim()
    {
        var job = new PlutoniumWorkflowJob(
            "PRE_SETUP",
            "ubuntu-22.04",
            ["- script: echo"],
            outputs: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PRE_SETUP_BUILD_ID"] = "$[ dependencies.PRE_SETUP.outputs['PreSetup.PRE_SETUP_BUILD_ID'] ]",
            });
        var document = new PlutoniumWorkflowDocument("Pipe", "trigger: none", [job]);

        var yaml = document.WriteYaml();

        Assert.Contains("PRE_SETUP_BUILD_ID: $[ dependencies.PRE_SETUP.outputs['PreSetup.PRE_SETUP_BUILD_ID'] ]", yaml, StringComparison.Ordinal);
    }
}
