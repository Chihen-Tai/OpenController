using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenController.Profiles;

public sealed record ProfileLoadResult(Profile? Profile, string? Error, bool WasMigrated)
{
    public bool Success => Profile is not null;
    public static ProfileLoadResult Fail(string error) => new(null, error, false);
}

/// <summary>
/// Versioned JSON (de)serialization for profiles.
/// <para>
/// Every document carries <c>schemaVersion</c>. Older versions are upgraded step by step by
/// <see cref="Migrations"/> operating on the raw JSON tree before typed deserialization. Documents from a
/// newer, unknown version are rejected instead of being silently downgraded.
/// </para>
/// </summary>
public static class ProfileSerializer
{
    /// <summary>Version written by this build.</summary>
    public const int CurrentSchemaVersion = 2;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Migration steps keyed by the version they upgrade *from*.
    /// v1 (pre-release format) stored one "deadzone" per stick and a flat "triggerDeadzone"; v2 split these into
    /// stick/trigger setting objects.
    /// </summary>
    private static readonly Dictionary<int, Action<JsonObject>> Migrations = new()
    {
        [1] = MigrateV1ToV2,
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return o;
    }

    public static string Serialize(Profile profile) =>
        JsonSerializer.Serialize(profile with { SchemaVersion = CurrentSchemaVersion }, Options);

    /// <summary>Parses, migrates and validates a profile document. Never throws for bad input.</summary>
    public static ProfileLoadResult Deserialize(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject ?? throw new JsonException("The document is not a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return ProfileLoadResult.Fail($"Invalid JSON: {ex.Message}");
        }

        int version;
        try
        {
            version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return ProfileLoadResult.Fail("schemaVersion must be an integer.");
        }

        if (version <= 0)
            return ProfileLoadResult.Fail("Missing schemaVersion; this does not look like a profile file.");
        if (version > CurrentSchemaVersion)
            return ProfileLoadResult.Fail(
                $"Profile was written by a newer version of the application (schema {version}, this build supports up to {CurrentSchemaVersion}).");

        bool migrated = false;
        try
        {
            while (version < CurrentSchemaVersion)
            {
                if (!Migrations.TryGetValue(version, out Action<JsonObject>? step))
                    return ProfileLoadResult.Fail($"No migration path from schema {version}.");
                step(root);
                version++;
                root["schemaVersion"] = version;
                migrated = true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return ProfileLoadResult.Fail($"Migration failed: {ex.Message}");
        }

        Profile? profile;
        try
        {
            profile = root.Deserialize<Profile>(Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return ProfileLoadResult.Fail($"Invalid profile content: {ex.Message}");
        }

        if (profile is null)
            return ProfileLoadResult.Fail("Profile is empty.");

        IReadOnlyList<string> errors = profile.Validate();
        if (errors.Count > 0)
            return ProfileLoadResult.Fail("Invalid settings: " + string.Join(" ", errors));

        return new ProfileLoadResult(profile, null, migrated);
    }

    /// <summary>
    /// v1 → v2: <c>{"leftDeadzone":0.1,"rightDeadzone":0.1,"triggerDeadzone":0.05}</c> becomes
    /// <c>leftStick.innerDeadzone</c>, <c>rightStick.innerDeadzone</c> and <c>left/rightTrigger.deadzone</c>.
    /// </summary>
    private static void MigrateV1ToV2(JsonObject root)
    {
        MoveInto(root, "leftDeadzone", "leftStick", "innerDeadzone");
        MoveInto(root, "rightDeadzone", "rightStick", "innerDeadzone");
        if (root["triggerDeadzone"] is JsonNode td)
        {
            double value = td.GetValue<double>();
            root.Remove("triggerDeadzone");
            EnsureObject(root, "leftTrigger")["deadzone"] = value;
            EnsureObject(root, "rightTrigger")["deadzone"] = value;
        }
    }

    private static void MoveInto(JsonObject root, string oldName, string objectName, string newName)
    {
        if (root[oldName] is not JsonNode node)
            return;
        double value = node.GetValue<double>();
        root.Remove(oldName);
        EnsureObject(root, objectName)[newName] = value;
    }

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
            return existing;
        var created = new JsonObject();
        root[name] = created;
        return created;
    }
}
