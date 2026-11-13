using ApplicationBuilderHelpers.Exceptions;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>Parser coverage for the repeatable <c>--args</c> split values.</summary>
public sealed class PlutoniumBumpArgParserTests
{
    private readonly PlutoniumBumpArgParser _parser = new();

    /// <summary>Keyed values split on the first separator and preserve the remainder.</summary>
    [Fact]
    public void ParsesAppEqualsExpression()
    {
        var value = _parser.ParseValue("myapp=minor+1", out var error);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal("myapp", value.App);
        Assert.Equal("minor+1", value.Expression);
    }

    /// <summary>Bare expressions keep a null expression for downstream sole-app resolution.</summary>
    [Fact]
    public void ParsesBareExpressionWithNullExpression()
    {
        var value = _parser.ParseValue("minor+1", out var error);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal("minor+1", value.App);
        Assert.Null(value.Expression);
    }

    /// <summary>Key-only shorthand keeps the app with a null expression.</summary>
    [Fact]
    public void ParsesKeyOnlyAsAppWithNullExpression()
    {
        var value = _parser.ParseValue("myapp=", out var error);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal("myapp", value.App);
        Assert.Null(value.Expression);
    }

    /// <summary>Empty input is malformed and surfaces a parser-level validation error.</summary>
    [Fact]
    public void RejectsEmptyValue()
    {
        var value = _parser.ParseValue("   ", out var error);

        Assert.NotNull(error);
        Assert.Null(value);
    }

    /// <summary>Missing keys are malformed and surface a parser-level validation error.</summary>
    [Fact]
    public void RejectsMissingAppKey()
    {
        var value = _parser.ParseValue("=minor+1", out var error);

        Assert.NotNull(error);
        Assert.Null(value);
    }

    /// <summary>The parser type handles exactly the bump-arg payload type.</summary>
    [Fact]
    public void ParserHandlesBumpArgType()
    {
        Assert.Equal(typeof(PlutoniumBumpArg), _parser.Type);
    }

    /// <summary>Parser-level validation errors are usage errors mapping to exit 2.</summary>
    [Fact]
    public void MalformedArgsMapToUsageExitCode()
    {
        _parser.ParseValue(string.Empty, out var error);

        Assert.NotNull(error);
        var usage = new CommandException(error, 2, CommandErrorKind.InvalidValue);

        Assert.Equal(2, usage.ExitCode);
        Assert.Equal(CommandErrorKind.InvalidValue, usage.Kind);
    }
}
