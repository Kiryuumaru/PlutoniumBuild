using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plutonium.Helpers;

/// <summary>
/// One application's version file: the AppId plus per-environment strict versions.
/// Serializes as <c>{appid: {env: version}}</c> inside <see cref="PlutoniumAllVersionsFile"/>.
/// </summary>
public sealed class PlutoniumVersionFile
{
    /// <summary>Gets the application identifier. Never null or whitespace.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; }

    /// <summary>Gets the per-environment versions keyed by lowercase environment. Never null.</summary>
    [JsonPropertyName("environmentVersions")]
    public IReadOnlyDictionary<string, PlutoniumSemVersion> EnvironmentVersions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumVersionFile"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null or whitespace.</param>
    /// <param name="environmentVersions">The per-environment versions. Must not be null; keys must not be null or whitespace and values must not be null.</param>
    /// <exception cref="ArgumentException">Thrown when a key is null or whitespace.</exception>
    public PlutoniumVersionFile(string appId, IReadOnlyDictionary<string, PlutoniumSemVersion> environmentVersions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(environmentVersions);
        foreach (var pair in environmentVersions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key, nameof(environmentVersions));
            ArgumentNullException.ThrowIfNull(pair.Value, nameof(environmentVersions));
        }

        AppId = appId.Trim();
        EnvironmentVersions = environmentVersions;
    }
}

/// <summary>
/// The <c>versions.json</c> document: every application's <see cref="PlutoniumVersionFile"/>.
/// </summary>
public sealed class PlutoniumAllVersionsFile
{
    /// <summary>Gets the per-application version files keyed by lowercase AppId. Never null.</summary>
    [JsonPropertyName("versions")]
    public IReadOnlyDictionary<string, PlutoniumVersionFile> Versions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAllVersionsFile"/> class.
    /// </summary>
    /// <param name="versions">The version files. Must not be null; values must not be null.</param>
    public PlutoniumAllVersionsFile(IReadOnlyDictionary<string, PlutoniumVersionFile> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        foreach (var pair in versions)
        {
            ArgumentNullException.ThrowIfNull(pair.Value, nameof(versions));
        }

        Versions = versions;
    }
}

/// <summary>
/// JSON file versioning over <c>versions.json</c>: raw-map read, typed generation
/// from reconciled versions, and behind-latest verification. The on-disk shape is
/// the flat <c>{appid-lower: {env-lower: version}}</c> map; typed wrappers are the
/// in-memory view. Tolerant reads return null instead of throwing so callers can
/// fall back to tag-only state.
/// </summary>
public static class PlutoniumVersionsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Tries to read the flat versions map from disk. Corrupt or missing files yield null.
    /// </summary>
    /// <param name="path">The file path value. Must not be null or whitespace.</param>
    /// <returns>The raw map, or null when unreadable. Never throws for I/O or JSON errors.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? TryReadRaw(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path.Trim()))
        {
            return null;
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(path.Trim()), JsonOptions);
            return map?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)pair.Value,
                StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the typed versions file, requiring every referenced AppId to exist in the discovery map.
    /// </summary>
    /// <param name="path">The file path value. Must not be null or whitespace.</param>
    /// <param name="knownAppIds">Known lowercase AppIds. Must not be null.</param>
    /// <returns>The typed document. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file is empty, an AppId is unknown, or a version is not strict SemVer.</exception>
    public static PlutoniumAllVersionsFile Read(string path, IReadOnlyCollection<string> knownAppIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(knownAppIds);

        var raw = TryReadRaw(path);
        if (raw is null)
        {
            throw new InvalidOperationException($"Versions file '{path.Trim()}' is empty.");
        }

        var known = new HashSet<string>(knownAppIds.Select(id => id.ToLowerInvariant()), StringComparer.Ordinal);
        return ToTyped(raw, known, path.Trim());
    }

    /// <summary>
    /// Reads one application's version file from the flat map.
    /// </summary>
    /// <param name="raw">The raw map. Must not be null.</param>
    /// <param name="appId">The application identifier. Must not be null or whitespace.</param>
    /// <param name="knownAppIds">Known lowercase AppIds. Must not be null.</param>
    /// <returns>The version file. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the app is missing, unknown, or carries a non-strict version.</exception>
    public static PlutoniumVersionFile ReadApp(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> raw,
        string appId,
        IReadOnlyCollection<string> knownAppIds)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(knownAppIds);

        var key = appId.Trim().ToLowerInvariant();
        if (!raw.TryGetValue(key, out var envMap))
        {
            throw new InvalidOperationException($"App '{appId.Trim()}' does not exist in versions.json.");
        }

        var known = new HashSet<string>(knownAppIds.Select(id => id.ToLowerInvariant()), StringComparer.Ordinal);
        if (!known.Contains(key))
        {
            throw new InvalidOperationException($"App '{appId.Trim()}' is defined in versions.json but has no entry.");
        }

        return new PlutoniumVersionFile(key, ParseEnvMap(envMap));
    }

    /// <summary>
    /// Generates the flat versions map from per-app latest environment versions.
    /// </summary>
    /// <param name="latestByApp">Per-app env groups (env-lower to ascending versions). Must not be null.</param>
    /// <returns>The flat map. Never null.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GenerateRaw(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>>> latestByApp)
    {
        ArgumentNullException.ThrowIfNull(latestByApp);

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var app in latestByApp)
        {
            ArgumentNullException.ThrowIfNull(app.Value, nameof(latestByApp));
            var envVersions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var env in app.Value)
            {
                ArgumentNullException.ThrowIfNull(env.Value, nameof(latestByApp));
                if (env.Value.Count != 0)
                {
                    envVersions[env.Key] = env.Value[^1].WithoutMetadata().ToString();
                }
            }

            result[app.Key] = envVersions;
        }

        return result;
    }

    /// <summary>
    /// Writes the flat versions map as indented JSON, creating parents as needed.
    /// </summary>
    /// <param name="path">The destination file path value. Must not be null or whitespace.</param>
    /// <param name="raw">The flat map. Must not be null.</param>
    public static void WriteRaw(string path, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(raw);

        var parent = Path.GetDirectoryName(Path.GetFullPath(path.Trim()));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path.Trim(), JsonSerializer.Serialize(raw, JsonOptions));
    }

    /// <summary>
    /// Verifies the file map is not behind the reconciled latest per environment.
    /// </summary>
    /// <param name="envVersions">Reconciled ascending versions keyed by env-lower. Must not be null.</param>
    /// <param name="fileMap">File versions keyed by env-lower. Must not be null.</param>
    /// <param name="appId">The application identifier for messages. Must not be null or whitespace.</param>
    /// <param name="environments">The environments to check. Must not be null.</param>
    /// <exception cref="InvalidOperationException">Thrown when a file version trails the reconciled latest.</exception>
    public static void Verify(
        IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> envVersions,
        IReadOnlyDictionary<string, PlutoniumSemVersion> fileMap,
        string appId,
        IEnumerable<string> environments)
    {
        ArgumentNullException.ThrowIfNull(envVersions);
        ArgumentNullException.ThrowIfNull(fileMap);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(environments);

        foreach (var environment in environments)
        {
            ArgumentNullException.ThrowIfNull(environment, nameof(environments));
            var key = environment.ToLowerInvariant();
            if (!envVersions.TryGetValue(key, out var grouped) ||
                !fileMap.TryGetValue(key, out var fileVersion) ||
                grouped.Count == 0)
            {
                continue;
            }

            var latest = grouped[^1];
            if (latest.ComparePrecedenceTo(fileVersion) > 0)
            {
                throw new InvalidOperationException(
                    $"versions.json is invalid. App '{appId.Trim()}' {key} file version {fileVersion} trails the latest {latest}.");
            }
        }
    }

    private static PlutoniumAllVersionsFile ToTyped(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> raw,
        HashSet<string> known,
        string path)
    {
        var versions = new Dictionary<string, PlutoniumVersionFile>(StringComparer.Ordinal);
        foreach (var app in raw)
        {
            var key = app.Key.ToLowerInvariant();
            if (!known.Contains(key))
            {
                throw new InvalidOperationException($"App '{app.Key}' is defined in {path} but has no entry.");
            }

            versions[key] = new PlutoniumVersionFile(key, ParseEnvMap(app.Value));
        }

        return new PlutoniumAllVersionsFile(versions);
    }

    private static Dictionary<string, PlutoniumSemVersion> ParseEnvMap(IReadOnlyDictionary<string, string> envMap)
    {
        var parsed = new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal);
        foreach (var env in envMap)
        {
            if (!PlutoniumSemVersion.TryParse(env.Value, out var version) || version is null)
            {
                throw new InvalidOperationException($"Invalid strict SemVer version '{env.Value}'.");
            }

            parsed[env.Key.ToLowerInvariant()] = version.WithoutMetadata();
        }

        return parsed;
    }
}
