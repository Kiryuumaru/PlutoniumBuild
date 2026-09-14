using Xunit;

namespace Plutonium.Core.Tests;

/// <summary>
/// Naming contract for per-job pipeline outputs, including dash and edge entry ids.
/// </summary>
public sealed class PipelineContractNamingTests
{
    /// <summary>
    /// Verifies a simple job and field combine into the expected output name.
    /// </summary>
    [Fact]
    public void OutputName_CombinesJobAndField()
    {
        Assert.Equal("BUILD_MATRIX", PlutoniumPipelineContract.OutputName("build", "matrix"));
    }

    /// <summary>
    /// Verifies dashes in the job id normalize to underscores.
    /// </summary>
    [Fact]
    public void OutputName_NormalizesDashes()
    {
        Assert.Equal("MY_JOB_CONDITION", PlutoniumPipelineContract.OutputName("my-job", "condition"));
    }

    /// <summary>
    /// Verifies dots, dashes, and spaces normalize to single underscores via the upper-name rule.
    /// </summary>
    [Fact]
    public void OutputName_NormalizesDotsDashesAndSpaces()
    {
        Assert.Equal("MY_JOB_TEST_RUN_SCRIPT", PlutoniumPipelineContract.OutputName("my-job.test", "run script"));
    }

    /// <summary>
    /// Verifies surrounding whitespace and edge separators trim to the bare name.
    /// </summary>
    [Fact]
    public void OutputName_TrimsWhitespaceAndEdgeSeparators()
    {
        Assert.Equal("BUILD_CONDITION", PlutoniumPipelineContract.OutputName("  -build-  ", " condition "));
    }

    /// <summary>
    /// Verifies the condition helper delegates to the CONDITION field.
    /// </summary>
    [Fact]
    public void ConditionOutputName_FormatsDashId()
    {
        Assert.Equal("MY_JOB_CONDITION", PlutoniumPipelineContract.ConditionOutputName("my-job"));
    }

    /// <summary>
    /// Verifies ids without letters or digits are rejected.
    /// </summary>
    /// <param name="jobId">The degenerate job id.</param>
    [Theory]
    [InlineData("---")]
    [InlineData("___")]
    [InlineData("...")]
    public void OutputName_RejectsIdsWithoutLettersOrDigits(string jobId)
    {
        Assert.Throws<ArgumentException>(() => PlutoniumPipelineContract.OutputName(jobId, "CONDITION"));
    }

    /// <summary>
    /// Verifies blank job ids are rejected.
    /// </summary>
    /// <param name="jobId">The blank job id.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConditionOutputName_RejectsBlankId(string jobId)
    {
        Assert.Throws<ArgumentException>(() => PlutoniumPipelineContract.ConditionOutputName(jobId));
    }
}
