using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.Platform.Apps;
using XboxControllerCenter.Profiles;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>The Profiles tab: manage, import/export, associate and recover profiles.</summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<ControllerItemViewModel?> _selectedController;
    private Profile? _selected;
    private string _newName = "";
    private string _executables = "";
    private string? _message;

    public ProfilesViewModel(AppServices services, Func<ControllerItemViewModel?> selectedController)
    {
        _services = services;
        _selectedController = selectedController;
        _selected = Manager.Active;
        LoadSelectionFields();

        ActivateCommand = new RelayCommand(() => { if (_selected is { } p) ActivateManually(p.Id); }, () => _selected is not null);
        NewCommand = new RelayCommand(() => Select(Manager.Create(Loc.T("Profiles.NewName"))));
        DuplicateCommand = new RelayCommand(() => { if (_selected is { } p) Select(Manager.Duplicate(p)); }, () => _selected is not null);
        RenameCommand = new RelayCommand(Rename, () => _selected is not null && !string.IsNullOrWhiteSpace(_newName));
        DeleteCommand = new RelayCommand(Delete, () => _selected is not null && Manager.Profiles.Count > 1);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export, () => _selected is not null);
        ResetCommand = new RelayCommand(Reset, () => _selected is not null);
        RestorePresetsCommand = new RelayCommand(() => Message = Loc.T("Profiles.PresetsRestored", Manager.RestoreBuiltInPresets()));
        OpenFolderCommand = new RelayCommand(() => OpenFolder(Manager.Directory));
        SaveExecutablesCommand = new RelayCommand(SaveExecutables, () => _selected is not null);
        AssociateControllerCommand = new RelayCommand(AssociateController,
            () => _selected is not null && _selectedController()?.Info.IdentityKey is not null);
        ClearControllersCommand = new RelayCommand(ClearControllers, () => _selected?.AssociatedControllers.Count > 0);
        QuarantineCommand = new RelayCommand(Quarantine);
        RecoverSettingsCommand = new RelayCommand(RecoverSettings, () => _services.SettingsError is not null);

        Manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileManager.Active))
                OnPropertyChanged(nameof(ActiveName));
        };
        Manager.Failures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFailures));
        Manager.Profiles.CollectionChanged += (_, _) =>
        {
            // Records are replaced on edit; keep the selection pointing at the current instance.
            if (_selected is { } s && !Manager.Profiles.Contains(s))
            {
                _selected = Manager.Profiles.FirstOrDefault(p => p.Id == s.Id);
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(SelectedAssociations));
            }
        };
    }

    public ProfileManager Manager => _services.Profiles;
    public ObservableCollection<Profile> Profiles => Manager.Profiles;
    public ObservableCollection<ProfileLoadFailure> Failures => Manager.Failures;
    public bool HasFailures => Manager.Failures.Count > 0;
    public string ActiveName => Manager.Active.Name;
    public string DataFolder => _services.Paths.Root + (_services.Paths.IsPortable ? "  (portable)" : "");
    public string? SettingsError => _services.SettingsError;
    public bool HasSettingsError => _services.SettingsError is not null;

    /// <summary>Id of the profile the user chose manually (auto-switching falls back to it).</summary>
    public Guid ManualProfileId { get; private set; }

    public Profile? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                LoadSelectionFields();
        }
    }

    public string NewName { get => _newName; set => Set(ref _newName, value); }
    public string Executables { get => _executables; set => Set(ref _executables, value); }
    public string? Message { get => _message; private set => Set(ref _message, value); }

    public string SelectedAssociations => _selected is null
        ? ""
        : Loc.T("Profiles.Associations",
            _selected.AssociatedExecutables.Count == 0 ? "—" : string.Join(", ", _selected.AssociatedExecutables),
            _selected.AssociatedControllers.Count == 0 ? "—" : string.Join(", ", _selected.AssociatedControllers));

    public ICommand ActivateCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RestorePresetsCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SaveExecutablesCommand { get; }
    public ICommand AssociateControllerCommand { get; }
    public ICommand ClearControllersCommand { get; }
    public ICommand QuarantineCommand { get; }
    public ICommand RecoverSettingsCommand { get; }

    public void ActivateManually(Guid id)
    {
        ManualProfileId = id;
        Manager.Activate(id);
        Message = Loc.T("Profiles.Activated", Manager.Active.Name);
    }

    public void InitializeManual(Guid id) => ManualProfileId = id;

    private void Select(Profile p)
    {
        Selected = p;
        Message = null;
    }

    private void LoadSelectionFields()
    {
        _newName = _selected?.Name ?? "";
        _executables = _selected is null ? "" : string.Join(", ", _selected.AssociatedExecutables);
        OnPropertyChanged(nameof(NewName));
        OnPropertyChanged(nameof(Executables));
        OnPropertyChanged(nameof(SelectedAssociations));
    }

    private void Rename()
    {
        if (_selected is null)
            return;
        Message = Manager.Rename(_selected, _newName) ?? Loc.T("Profiles.Renamed");
    }

    private void Delete()
    {
        if (_selected is not { } p)
            return;
        if (MessageBox.Show(Loc.T("Profiles.DeleteConfirm", p.Name), Loc.T("Profiles.DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (Manager.Delete(p))
        {
            Selected = Manager.Active;
            Message = Loc.T("Profiles.Deleted", p.Name);
        }
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Filter = "Profile (*.json)|*.json|All files|*.*" };
        if (dlg.ShowDialog() != true)
            return;
        ProfileLoadResult r = Manager.Import(dlg.FileName);
        if (r.Profile is null)
        {
            Message = Loc.T("Profiles.ImportFailed", r.Error ?? "");
            return;
        }
        Select(Manager.Profiles[^1]);
        Message = Loc.T("Profiles.Imported", Manager.Profiles[^1].Name) + (r.WasMigrated ? " " + Loc.T("Profiles.Migrated") : "");
    }

    private void Export()
    {
        if (_selected is not { } p)
            return;
        var dlg = new SaveFileDialog { Filter = "Profile (*.json)|*.json", FileName = MakeFileName(p.Name) + ".json" };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            ProfileManager.Export(p, dlg.FileName);
            Message = Loc.T("Profiles.Exported", dlg.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }

    private void Reset()
    {
        if (_selected is not { } p)
            return;
        if (MessageBox.Show(Loc.T("Profiles.ResetConfirm", p.Name), Loc.T("Profiles.ResetTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        Message = Manager.ResetToDefaults(p) ?? Loc.T("Profiles.ResetDone");
    }

    private void SaveExecutables()
    {
        if (_selected is not { } p)
            return;
        string[] exes = _executables.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ForegroundAppWatcher.NormalizeExecutableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        UpdateSelected(p with { AssociatedExecutables = exes });
    }

    private void AssociateController()
    {
        if (_selected is not { } p || _selectedController()?.Info.IdentityKey is not { } key)
            return;
        if (!p.AssociatedControllers.Contains(key))
            UpdateSelected(p with { AssociatedControllers = [.. p.AssociatedControllers, key] });
    }

    private void ClearControllers()
    {
        if (_selected is { } p)
            UpdateSelected(p with { AssociatedControllers = [] });
    }

    private void UpdateSelected(Profile updated)
    {
        string? error = updated.Id == Manager.Active.Id ? Manager.UpdateActive(_ => updated) : Validate(updated);
        if (error is null && updated.Id != Manager.Active.Id)
            Manager.Replace(updated);
        Message = error ?? Loc.T("Profiles.Saved");
        _selected = Manager.Profiles.FirstOrDefault(x => x.Id == updated.Id);
        OnPropertyChanged(nameof(Selected));
        LoadSelectionFields();
    }

    private static string? Validate(Profile p)
    {
        IReadOnlyList<string> e = p.Validate();
        return e.Count == 0 ? null : string.Join(" ", e);
    }

    private void Quarantine(object? parameter)
    {
        if (parameter is not ProfileLoadFailure f)
            return;
        if (MessageBox.Show(Loc.T("Profiles.QuarantineConfirm", f.FilePath), Loc.T("Profiles.QuarantineTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            Message = Loc.T("Profiles.Quarantined", Manager.Quarantine(f));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }

    private void RecoverSettings()
    {
        if (MessageBox.Show(Loc.T("Settings.RecoverConfirm"), Loc.T("Settings.RecoverTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        string? moved = _services.RecoverSettingsFile();
        Message = Loc.T("Settings.Recovered", moved ?? "—");
        OnPropertyChanged(nameof(SettingsError));
        OnPropertyChanged(nameof(HasSettingsError));
    }

    internal static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            FileLog.Write($"Opening folder failed: {ex.Message}");
        }
    }

    private static string MakeFileName(string name) =>
        string.Concat(name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    public void Relocalize() => RaiseAll();
}
