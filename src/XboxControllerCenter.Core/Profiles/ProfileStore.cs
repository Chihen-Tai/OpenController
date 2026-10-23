namespace XboxControllerCenter.Profiles;

/// <summary>A profile file that could not be loaded. The file is left untouched on disk.</summary>
public sealed record ProfileLoadFailure(string FilePath, string Error);

public sealed record ProfileStoreLoadResult(IReadOnlyList<Profile> Profiles, IReadOnlyList<ProfileLoadFailure> Failures);

/// <summary>
/// Stores one profile per JSON file (<c>{id}.json</c>) in a directory.
/// <list type="bullet">
/// <item>Writes are atomic (temporary file + replace) so a crash cannot leave a half-written profile.</item>
/// <item>Corrupted or unreadable files are reported, never overwritten or deleted automatically.
/// <see cref="Quarantine"/> is the explicit recovery action: it renames the file aside.</item>
/// </list>
/// </summary>
public sealed class ProfileStore
{
    public const string Extension = ".json";

    public ProfileStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string PathFor(Guid id) => Path.Combine(Directory, id.ToString("D") + Extension);

    /// <summary>Loads all profiles. If the directory has no profile files at all, the built-in presets are created.</summary>
    public ProfileStoreLoadResult LoadAll()
    {
        System.IO.Directory.CreateDirectory(Directory);
        string[] files = System.IO.Directory.GetFiles(Directory, "*" + Extension);
        if (files.Length == 0)
        {
            IReadOnlyList<Profile> presets = ProfilePresets.All();
            foreach (Profile p in presets)
                Save(p);
            return new ProfileStoreLoadResult(presets, []);
        }

        var profiles = new List<Profile>();
        var failures = new List<ProfileLoadFailure>();
        var seenIds = new HashSet<Guid>();
        foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ProfileLoadFailure(file, $"Cannot read file: {ex.Message}"));
                continue;
            }

            ProfileLoadResult result = ProfileSerializer.Deserialize(json);
            if (!result.Success)
            {
                failures.Add(new ProfileLoadFailure(file, result.Error ?? "Unknown error."));
                continue;
            }

            Profile profile = result.Profile!;
            if (!seenIds.Add(profile.Id))
            {
                failures.Add(new ProfileLoadFailure(file, $"Duplicate profile id {profile.Id}; file ignored."));
                continue;
            }

            // A migrated profile is written back only if its file name matches its id; the original is
            // kept as a .bak copy first so the migration is reversible.
            if (result.WasMigrated && string.Equals(Path.GetFullPath(file), Path.GetFullPath(PathFor(profile.Id)), StringComparison.OrdinalIgnoreCase))
            {
                TryBackup(file);
                Save(profile);
            }

            profiles.Add(profile);
        }

        return new ProfileStoreLoadResult(profiles, failures);
    }

    public void Save(Profile profile)
    {
        IReadOnlyList<string> errors = profile.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException("Refusing to save an invalid profile: " + string.Join(" ", errors));
        System.IO.Directory.CreateDirectory(Directory);
        AtomicFile.WriteAllText(PathFor(profile.Id), ProfileSerializer.Serialize(profile));
    }

    public void Delete(Guid id)
    {
        string path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static void Export(Profile profile, string path) =>
        AtomicFile.WriteAllText(path, ProfileSerializer.Serialize(profile));

    /// <summary>
    /// Reads a profile from an external file. If its id collides with an existing profile it receives a new id.
    /// The imported profile is not saved; call <see cref="Save"/>.
    /// </summary>
    public static ProfileLoadResult Import(string path, IEnumerable<Guid> existingIds)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProfileLoadResult.Fail($"Cannot read file: {ex.Message}");
        }

        ProfileLoadResult result = ProfileSerializer.Deserialize(json);
        if (result.Profile is { } p && existingIds.Contains(p.Id))
            return result with { Profile = p with { Id = Guid.NewGuid() } };
        return result;
    }

    /// <summary>
    /// Explicit recovery for a corrupted file: renames it to <c>name.corrupt-YYYYMMDD-HHMMSS</c> so it no longer
    /// loads but remains available for manual inspection. Returns the new path.
    /// </summary>
    public static string Quarantine(string filePath, DateTimeOffset now)
    {
        string target = $"{filePath}.corrupt-{now:yyyyMMdd-HHmmss}";
        int n = 1;
        while (File.Exists(target))
            target = $"{filePath}.corrupt-{now:yyyyMMdd-HHmmss}-{n++}";
        File.Move(filePath, target);
        return target;
    }

    private static void TryBackup(string file)
    {
        try
        {
            string backup = file + ".bak";
            if (!File.Exists(backup))
                File.Copy(file, backup);
        }
        catch (IOException)
        {
            // Backup is best-effort; the migrated profile is still loaded in memory.
        }
    }
}

/// <summary>Atomic text-file replacement.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string fullPath = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string temp = fullPath + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, fullPath, overwrite: true);
    }
}
