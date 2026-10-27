using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using OpenController.App.ViewModels;
using OpenController.Platform.Input;
using OpenController.Profiles;

namespace OpenController.App.Services;

/// <summary>
/// UI-thread owner of the profile list. Every change is validated, pushed atomically to the input
/// sampler (so tuning takes effect immediately), and saved after a short debounce.
/// </summary>
public sealed class ProfileManager : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly InputService _input;
    private readonly DispatcherTimer _saveTimer;
    private readonly HashSet<Guid> _dirty = [];
    private Profile _active;

    public ProfileManager(ProfileStore store, InputService input, Guid? activeId)
    {
        _store = store;
        _input = input;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => FlushSaves();

        ProfileStoreLoadResult loaded;
        try
        {
            loaded = store.LoadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loaded = new ProfileStoreLoadResult([], [new ProfileLoadFailure(store.Directory, ex.Message)]);
        }

        foreach (Profile p in loaded.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Profiles.Add(p);
        foreach (ProfileLoadFailure f in loaded.Failures)
            Failures.Add(f);

        if (Profiles.Count == 0)
        {
            // Every file failed to load: work with an in-memory default; nothing on disk is overwritten.
            Profiles.Add(ProfilePresets.Default());
        }

        _active = Profiles.FirstOrDefault(p => p.Id == activeId) ?? Profiles.FirstOrDefault(p => p.Name == "Default") ?? Profiles[0];
        _input.SetProfile(_active);
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ObservableCollection<ProfileLoadFailure> Failures { get; } = [];

    public string Directory => _store.Directory;

    public Profile Active => _active;

    /// <summary>Raised after the active profile was switched or edited.</summary>
    public event Action? ActiveChanged;

    public void Activate(Guid id)
    {
        Profile? p = Profiles.FirstOrDefault(x => x.Id == id);
        if (p is null || p.Id == _active.Id)
            return;
        _active = p;
        _input.SetProfile(p);
        OnPropertyChanged(nameof(Active));
        ActiveChanged?.Invoke();
    }

    /// <summary>Applies an edit to the active profile. Returns an error message when the result is invalid.</summary>
    public string? UpdateActive(Func<Profile, Profile> change)
    {
        Profile updated;
        try
        {
            updated = change(_active);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        IReadOnlyList<string> errors = updated.Validate();
        if (errors.Count > 0)
            return string.Join(Environment.NewLine, errors);

        Replace(updated);
        _active = updated;
        _input.SetProfile(updated);
        OnPropertyChanged(nameof(Active));
        ActiveChanged?.Invoke();
        return null;
    }

    public Profile Create(string name)
    {
        Profile p = ProfilePresets.Default() with { Id = Guid.NewGuid(), Name = UniqueName(name) };
        Add(p);
        return p;
    }

    public Profile Duplicate(Profile source)
    {
        Profile p = source with { Id = Guid.NewGuid(), Name = UniqueName(source.Name + " (copy)") };
        Add(p);
        return p;
    }

    public string? Rename(Profile profile, string newName)
    {
        newName = newName.Trim();
        Profile renamed = profile with { Name = newName };
        IReadOnlyList<string> errors = renamed.Validate();
        if (errors.Count > 0)
            return string.Join(" ", errors);
        Replace(renamed);
        if (profile.Id == _active.Id)
        {
            _active = renamed;
            OnPropertyChanged(nameof(Active));
            ActiveChanged?.Invoke();
        }
        return null;
    }

    /// <summary>Deletes a profile file (caller must have asked for confirmation). The last profile cannot be deleted.</summary>
    public bool Delete(Profile profile)
    {
        if (Profiles.Count <= 1)
            return false;
        _dirty.Remove(profile.Id);
        _store.Delete(profile.Id);
        Profiles.Remove(profile);
        if (profile.Id == _active.Id)
        {
            _active = Profiles[0];
            _input.SetProfile(_active);
            OnPropertyChanged(nameof(Active));
            ActiveChanged?.Invoke();
        }
        return true;
    }

    public ProfileLoadResult Import(string path)
    {
        ProfileLoadResult r = ProfileStore.Import(path, Profiles.Select(p => p.Id));
        if (r.Profile is { } p)
            Add(p with { Name = UniqueName(p.Name) });
        return r;
    }

    public static void Export(Profile profile, string path) => ProfileStore.Export(profile, path);

    /// <summary>Resets a profile's settings to the passthrough defaults, keeping its id, name and associations.</summary>
    public string? ResetToDefaults(Profile profile)
    {
        Profile reset = ProfilePresets.Default() with
        {
            Id = profile.Id,
            Name = profile.Name,
            AssociatedExecutables = profile.AssociatedExecutables,
            AssociatedControllers = profile.AssociatedControllers,
        };
        if (profile.Id == _active.Id)
            return UpdateActive(_ => reset);
        Replace(reset);
        return null;
    }

    /// <summary>Re-adds any missing built-in preset (by name).</summary>
    public int RestoreBuiltInPresets()
    {
        int added = 0;
        foreach (Profile preset in ProfilePresets.All())
        {
            if (Profiles.Any(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            Add(preset);
            added++;
        }
        return added;
    }

    /// <summary>Explicit recovery of a corrupted file: renames it aside so it stops failing.</summary>
    public string Quarantine(ProfileLoadFailure failure)
    {
        string moved = ProfileStore.Quarantine(failure.FilePath, DateTimeOffset.Now);
        Failures.Remove(failure);
        return moved;
    }

    public void Replace(Profile updated)
    {
        int index = IndexOf(updated.Id);
        if (index >= 0)
            Profiles[index] = updated;
        MarkDirty(updated.Id);
    }

    public void FlushSaves()
    {
        _saveTimer.Stop();
        foreach (Guid id in _dirty.ToList())
        {
            Profile? p = Profiles.FirstOrDefault(x => x.Id == id);
            if (p is null)
                continue;
            try
            {
                _store.Save(p);
                _dirty.Remove(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                FileLog.Write($"Saving profile {p.Name} failed: {ex.Message}");
            }
        }
    }

    private void Add(Profile p)
    {
        Profiles.Add(p);
        MarkDirty(p.Id);
    }

    private void MarkDirty(Guid id)
    {
        _dirty.Add(id);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private int IndexOf(Guid id)
    {
        for (int i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Id == id)
                return i;
        }
        return -1;
    }

    private string UniqueName(string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Profile" : name.Trim();
        if (name.Length > 70)
            name = name[..70];
        string candidate = name;
        int n = 2;
        while (Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{name} {n++}";
        return candidate;
    }
}
