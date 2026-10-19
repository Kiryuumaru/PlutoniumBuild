using System.Reflection;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;

namespace Plutonium.Helpers.App;
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
