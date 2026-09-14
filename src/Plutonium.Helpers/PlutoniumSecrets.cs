using System.Reflection;
using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Marks a build-class field or property as a secret variable. The value is
/// resolved from the environment at setup, registered for Core-side masking,
/// and exported to the pipeline; it is never assigned in code.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PlutoniumSecretVariableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumSecretVariableAttribute"/> class.
    /// </summary>
    /// <param name="secretVariableName">The secret name. Must not be null or whitespace.</param>
    /// <param name="environmentVariableName">Optional environment override. Defaults to <c>NUKE_</c> plus the secret name.</param>
    public PlutoniumSecretVariableAttribute(string secretVariableName, string? environmentVariableName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretVariableName);
        SecretVariableName = secretVariableName.Trim();
        EnvironmentVariableName = environmentVariableName;
    }

    /// <summary>Gets the secret variable name. Never null or whitespace.</summary>
    public string SecretVariableName { get; }

    /// <summary>Gets the environment variable override, if any.</summary>
    public string? EnvironmentVariableName { get; }
}

/// <summary>
/// One declared secret: the member carrying the attribute plus the attribute itself.
/// </summary>
/// <param name="Member">The field or property. Never null.</param>
/// <param name="Secret">The attribute. Never null.</param>
public sealed record PlutoniumSecretDeclaration(MemberInfo Member, PlutoniumSecretVariableAttribute Secret)
{
    /// <summary>Gets the field or property. Never null.</summary>
    public MemberInfo Member { get; init; } = Member ?? throw new ArgumentNullException(nameof(Member));

    /// <summary>Gets the attribute. Never null.</summary>
    public PlutoniumSecretVariableAttribute Secret { get; init; } = Secret ?? throw new ArgumentNullException(nameof(Secret));

    /// <summary>Gets the environment variable name: the override or <c>NUKE_</c> plus the secret name.</summary>
    public string EnvironmentVariableName =>
        string.IsNullOrWhiteSpace(Secret.EnvironmentVariableName)
            ? "NUKE_" + Secret.SecretVariableName
            : Secret.EnvironmentVariableName.Trim();
}

/// <summary>
/// Reflection scan plus setup for <see cref="PlutoniumSecretVariableAttribute"/>:
/// resolves each secret from the environment, warns when empty, registers the
/// value for Core-side masking, assigns the member, and exports the variable.
/// Masking itself stays Core-side via <see cref="PlutoniumSecretMasker"/>.
/// </summary>
public static class PlutoniumSecrets
{
    /// <summary>
    /// Scans instance fields and properties for <see cref="PlutoniumSecretVariableAttribute"/>.
    /// </summary>
    /// <param name="build">The build instance. Must not be null.</param>
    /// <returns>The declared secrets. Never null.</returns>
    public static IReadOnlyList<PlutoniumSecretDeclaration> GetSecretVariables(object build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var declarations = new List<PlutoniumSecretDeclaration>();
        var type = build.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var attribute in property.GetCustomAttributes(typeof(PlutoniumSecretVariableAttribute), inherit: true))
            {
                declarations.Add(new PlutoniumSecretDeclaration(property, (PlutoniumSecretVariableAttribute)attribute));
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var attribute in field.GetCustomAttributes(typeof(PlutoniumSecretVariableAttribute), inherit: true))
            {
                declarations.Add(new PlutoniumSecretDeclaration(field, (PlutoniumSecretVariableAttribute)attribute));
            }
        }

        return declarations;
    }

    /// <summary>
    /// Resolves every declared secret from the environment and wires it up.
    /// </summary>
    /// <param name="build">The build instance. Must not be null.</param>
    /// <param name="logger">Optional logger receiving the empty-secret warning. May be null.</param>
    /// <param name="export">Optional export sink (variable name, value). May be null.</param>
    /// <param name="masker">Optional Core masker receiving each resolved value. May be null.</param>
    public static void Setup(
        object build,
        IPlutoniumLogger? logger = null,
        Action<string, string>? export = null,
        PlutoniumSecretMasker? masker = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        foreach (var declaration in GetSecretVariables(build))
        {
            var envName = declaration.EnvironmentVariableName;
            var value = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(value))
            {
                logger?.Log(PlutoniumLogLevel.Warning, $"Secret variable {envName} was not resolved.");
                continue;
            }

            masker?.Register(value);
            logger?.RegisterSecret(value);
            SetMemberValue(build, declaration.Member, value, logger, envName);
            export?.Invoke(envName, value);
        }
    }

    private static void SetMemberValue(object build, MemberInfo member, string value, IPlutoniumLogger? logger, string envName)
    {
        switch (member)
        {
            case PropertyInfo property when property.CanWrite:
                property.SetValue(build, value);
                break;
            case FieldInfo field when !field.IsInitOnly:
                field.SetValue(build, value);
                break;
            default:
                logger?.Log(PlutoniumLogLevel.Warning, $"Secret variable {envName} maps to read-only member {member.Name}; value masked and exported but not assigned.");
                break;
        }
    }
}
