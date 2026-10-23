using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;
using XboxControllerCenter.Profiles;

namespace XboxControllerCenter.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xcc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static Profile Sample() => ProfilePresets.Fps() with
    {
        RightStick = new StickSettings
        {
            InnerDeadzone = 0.07,
            Curve = ResponseCurve.Custom(new(0, 0), new(0.4, 0.2), new(1, 1)),
            InvertY = true,
            OffsetCorrection = true,
            OffsetX = 0.01,
            OffsetY = -0.02,
        },
        ButtonMap = new Dictionary<GamepadControl, MappingTarget>
        {
            [GamepadControl.A] = MappingTarget.ToControl(GamepadControl.B),
            [GamepadControl.LeftThumb] = MappingTarget.Disabled,
            [GamepadControl.View] = MappingTarget.ToKey(0x1B),
        },
        AssociatedExecutables = ["game.exe"],
        AssociatedControllers = ["045E:0B13"],
        VirtualOutput = new VirtualOutputSettings { AutoStart = true, ForwardRumble = false },
    };

    [Fact]
    public void SerializeDeserialize_RoundTripsAllFields()
    {
        Profile original = Sample();
        string json = ProfileSerializer.Serialize(original);
        ProfileLoadResult result = ProfileSerializer.Deserialize(json);
        Assert.True(result.Success, result.Error);
        Profile p = result.Profile!;
        Assert.Equal(original.Id, p.Id);
        Assert.Equal(original.Name, p.Name);
        Assert.Equal(original.LeftStick, p.LeftStick);
        Assert.Equal(original.RightStick with { Curve = ResponseCurve.Linear }, p.RightStick with { Curve = ResponseCurve.Linear });
        Assert.Equal(original.RightStick.Curve.Points, p.RightStick.Curve.Points);
        Assert.Equal(original.LeftTrigger, p.LeftTrigger);
        Assert.Equal(original.VirtualOutput, p.VirtualOutput);
        Assert.Equal(original.AssociatedExecutables, p.AssociatedExecutables);
        Assert.Equal(original.AssociatedControllers, p.AssociatedControllers);
        Assert.Equal(3, p.ButtonMap.Count);
        Assert.Equal(MappingTarget.ToControl(GamepadControl.B), p.ButtonMap[GamepadControl.A]);
        Assert.Equal(MappingTarget.Disabled, p.ButtonMap[GamepadControl.LeftThumb]);
        Assert.Equal(MappingTarget.ToKey(0x1B), p.ButtonMap[GamepadControl.View]);
        Assert.False(result.WasMigrated);
    }

    [Fact]
    public void Serialize_WritesSchemaVersionAndReadableEnums()
    {
        string json = ProfileSerializer.Serialize(Sample());
        Assert.Contains("\"schemaVersion\": " + ProfileSerializer.CurrentSchemaVersion, json);
        Assert.Contains("\"custom\"", json);
        Assert.Contains("\"kind\": \"keyboard\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"name\":\"x\"}")]                       // missing schemaVersion
    [InlineData("{\"schemaVersion\":\"two\"}")]            // wrong type
    [InlineData("{\"schemaVersion\":99,\"name\":\"x\"}")]  // future version
    public void Deserialize_RejectsCorruptOrUnknownDocuments(string json)
    {
        ProfileLoadResult r = ProfileSerializer.Deserialize(json);
        Assert.False(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void Deserialize_RejectsOutOfRangeSettings()
    {
        string json = ProfileSerializer.Serialize(Sample()).Replace("\"innerDeadzone\": 0.07", "\"innerDeadzone\": 5");
        ProfileLoadResult r = ProfileSerializer.Deserialize(json);
        Assert.False(r.Success);
        Assert.Contains("InnerDeadzone", r.Error);
    }

    [Fact]
    public void Deserialize_RejectsUnknownEnumValues()
    {
        string json = ProfileSerializer.Serialize(Sample()).Replace("\"keyboard\"", "\"teleport\"");
        Assert.False(ProfileSerializer.Deserialize(json).Success);
    }

    [Fact]
    public void Migration_V1ToCurrent()
    {
        string v1 = """
            {
              "schemaVersion": 1,
              "id": "2f0c39f4-6a2c-4c55-9a8e-0d2f2f6f1b11",
              "name": "Old",
              "leftDeadzone": 0.12,
              "rightDeadzone": 0.2,
              "triggerDeadzone": 0.05
            }
            """;
        ProfileLoadResult r = ProfileSerializer.Deserialize(v1);
        Assert.True(r.Success, r.Error);
        Assert.True(r.WasMigrated);
        Assert.Equal(0.12, r.Profile!.LeftStick.InnerDeadzone);
        Assert.Equal(0.2, r.Profile.RightStick.InnerDeadzone);
        Assert.Equal(0.05, r.Profile.LeftTrigger.Deadzone);
        Assert.Equal(0.05, r.Profile.RightTrigger.Deadzone);
        Assert.Equal(ProfileSerializer.CurrentSchemaVersion, r.Profile.SchemaVersion);
    }

    [Fact]
    public void Store_CreatesPresetsInEmptyDirectory()
    {
        var store = new ProfileStore(_dir);
        ProfileStoreLoadResult r = store.LoadAll();
        Assert.Equal(4, r.Profiles.Count);
        Assert.Empty(r.Failures);
        Assert.Equal(4, Directory.GetFiles(_dir, "*.json").Length);
        Assert.Equal(4, store.LoadAll().Profiles.Count);
    }

    [Fact]
    public void Store_ReportsCorruptFilesWithoutTouchingThem()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample());
        string corrupt = Path.Combine(_dir, "broken.json");
        File.WriteAllText(corrupt, "{ this is not json");

        ProfileStoreLoadResult r = store.LoadAll();
        Assert.Single(r.Profiles);
        ProfileLoadFailure failure = Assert.Single(r.Failures);
        Assert.Equal(corrupt, failure.FilePath);
        Assert.Equal("{ this is not json", File.ReadAllText(corrupt)); // untouched

        string moved = ProfileStore.Quarantine(corrupt, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Assert.False(File.Exists(corrupt));
        Assert.True(File.Exists(moved));
        Assert.EndsWith(".corrupt-20260102-030405", moved);
        Assert.Empty(store.LoadAll().Failures);
    }

    [Fact]
    public void Store_RefusesToSaveInvalidProfile()
    {
        var store = new ProfileStore(_dir);
        Profile bad = Sample() with { LeftStick = new StickSettings { InnerDeadzone = 2 } };
        Assert.Throws<InvalidOperationException>(() => store.Save(bad));
    }

    [Fact]
    public void Store_MigratedProfileIsBackedUpAndRewritten()
    {
        Directory.CreateDirectory(_dir);
        var id = Guid.NewGuid();
        string path = Path.Combine(_dir, id + ".json");
        File.WriteAllText(path, $$"""{"schemaVersion":1,"id":"{{id}}","name":"Old","leftDeadzone":0.1}""");
        var store = new ProfileStore(_dir);
        ProfileStoreLoadResult r = store.LoadAll();
        Assert.Single(r.Profiles);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Contains("\"schemaVersion\": " + ProfileSerializer.CurrentSchemaVersion, File.ReadAllText(path));
    }

    [Fact]
    public void ImportExport_AssignsNewIdOnCollision()
    {
        Profile p = Sample();
        string file = Path.Combine(_dir, "export", "p.json");
        ProfileStore.Export(p, file);
        ProfileLoadResult imported = ProfileStore.Import(file, [p.Id]);
        Assert.True(imported.Success);
        Assert.NotEqual(p.Id, imported.Profile!.Id);
        Assert.Equal(p.Name, imported.Profile.Name);
        Assert.Equal(p.Id, ProfileStore.Import(file, []).Profile!.Id);
    }

    [Fact]
    public void WithMapping_DefaultRemovesEntry()
    {
        Profile p = Sample().WithMapping(GamepadControl.A, MappingTarget.Default);
        Assert.False(p.ButtonMap.ContainsKey(GamepadControl.A));
        Assert.Equal(MappingTarget.Default, p.GetMapping(GamepadControl.A));
        Assert.True(p.UsesKeyboard);
    }

    [Fact]
    public void AllPresets_AreValid()
    {
        foreach (Profile p in ProfilePresets.All())
            Assert.Empty(p.Validate());
    }

    [Fact]
    public void AppSettings_CorruptFileIsReportedAndNotOverwritten()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "garbage");
        var store = new AppSettingsStore(path);
        AppSettingsLoadResult r = store.Load();
        Assert.NotNull(r.Error);
        Assert.False(store.Save(new AppSettings()));
        Assert.Equal("garbage", File.ReadAllText(path));
        store.ResetCorrupt(DateTimeOffset.Now);
        Assert.True(store.Save(new AppSettings { Language = "zh-TW" }));
        Assert.Equal("zh-TW", new AppSettingsStore(path).Load().Settings.Language);
    }

    [Fact]
    public void AppSettings_SanitizesOutOfRangeValues()
    {
        AppSettings s = new AppSettings { SamplingRateHz = 5, UiFps = 500, Language = "fr", BatteryPollSeconds = 1, Theme = "neon" }.Sanitized();
        Assert.Equal("dark", s.Theme);
        Assert.Equal("light", (new AppSettings { Theme = "light" }).Sanitized().Theme);
        Assert.Equal(60, s.SamplingRateHz);
        Assert.Equal(60, s.UiFps);
        Assert.Equal("en", s.Language);
        Assert.Equal(10, s.BatteryPollSeconds);
    }
}
