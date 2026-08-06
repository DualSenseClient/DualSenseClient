using System.Text.Json;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class SpecialActionServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpecialActionServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // cleanup best-effort
        }
    }

    private string ActionsPath => Path.Combine(_tempDir, "Config", "special_actions.json");

    /// <summary>
    /// Writes raw JSON to the actions path, creating the directory first.
    /// </summary>
    private void WriteActionsJson(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ActionsPath)!);
        File.WriteAllText(ActionsPath, json);
    }

    private SpecialActionService CreateService() => new SpecialActionService(actionsPath: ActionsPath);

    [Test]
    public void Constructor_CreatesActionsDirectory()
    {
        string path = Path.Combine(_tempDir, "nested", "special_actions.json");
        SpecialActionService service = new SpecialActionService(actionsPath: path);
        _ = service.Settings;
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void Load_MissingFile_FallsBackToDefaults()
    {
        SpecialActionService service = CreateService();
        Assert.That(service.Settings.Actions, Is.Empty);
    }

    [Test]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        string path = Path.Combine(_tempDir, "special_actions.json");
        File.WriteAllText(path, "not valid json {{{");
        SpecialActionService service = new SpecialActionService(actionsPath: path);
        Assert.That(service.Settings.Actions, Is.Empty);
    }

    [Test]
    public void CreateAction_AddsAndPersists()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, null);
        Assert.That(action.Name, Is.EqualTo(SpecialActionService.DefaultActionName));
        Assert.That(File.Exists(ActionsPath), Is.True);

        SpecialActionService reloaded = CreateService();
        Assert.That(reloaded.Settings.Actions, Has.Count.EqualTo(1));
        Assert.That(reloaded.Settings.Actions[0].Id, Is.EqualTo(action.Id));
    }

    [Test]
    public void CreateAction_DerivesUniqueName_WhenBaseTaken()
    {
        SpecialActionService service = CreateService();
        service.CreateAction("My Action", null);
        SpecialAction second = service.CreateAction("My Action", null);
        Assert.That(second.Name, Is.EqualTo("My Action 2"));
    }

    [Test]
    public void CreateAction_WithControllerId_EnablesActionForIt()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, "AA:BB:CC:DD:EE:FF");
        Assert.That(action.EnabledControllers, Is.EqualTo(new[] { "AA:BB:CC:DD:EE:FF" }));
    }

    [Test]
    public void CreateAction_WithoutControllerId_CreatesDisabledAction()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, null);
        Assert.That(action.EnabledControllers, Is.Empty);
    }

    [Test]
    public void DeleteAction_RemovesAndPersists()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction("One", null);

        bool deleted = service.DeleteAction(action.Id);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(service.Settings.Actions, Is.Empty);
        });

        SpecialActionService reloaded = CreateService();
        Assert.That(reloaded.Settings.Actions, Is.Empty);
    }

    [Test]
    public void DeleteAction_MissingId_ReturnsFalse()
    {
        SpecialActionService service = CreateService();
        Assert.That(service.DeleteAction(Guid.NewGuid()), Is.False);
    }

    [Test]
    public void SetEnabledForController_AddsAndPersists()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, null);

        bool changed = service.SetEnabledForController(action.Id, "aa:bb:cc:dd:ee:ff", true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(action.EnabledControllers, Is.EqualTo(new[] { "aa:bb:cc:dd:ee:ff" }));
        });

        SpecialActionService reloaded = CreateService();
        Assert.That(reloaded.Settings.Actions[0].EnabledControllers, Is.EqualTo(new[] { "aa:bb:cc:dd:ee:ff" }));
    }

    [Test]
    public void SetEnabledForController_RemovesAndPersists()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, "AA:BB:CC:DD:EE:FF");

        bool changed = service.SetEnabledForController(action.Id, "aa:bb:cc:dd:ee:ff", false);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(action.EnabledControllers, Is.Empty);
        });
    }

    [Test]
    public void SetEnabledForController_SameState_ReturnsFalse()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, "AA:BB:CC:DD:EE:FF");
        Assert.Multiple(() =>
        {
            Assert.That(service.SetEnabledForController(action.Id, "aa:bb:cc:dd:ee:ff", true), Is.False);
            Assert.That(service.SetEnabledForController(action.Id, "00:00:00:00:00:00", false), Is.False);
        });
    }

    [Test]
    public void SetEnabledForController_MissingAction_ReturnsFalse()
    {
        SpecialActionService service = CreateService();
        Assert.That(service.SetEnabledForController(Guid.NewGuid(), "AA:BB", true), Is.False);
    }

    [Test]
    public void GetControllerId_PrefersMacOverPath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpecialActionService.GetControllerId("aa:bb:cc:dd:ee:ff", "/path/1"), Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(SpecialActionService.GetControllerId(null, "/path/1"), Is.EqualTo("/path/1"));
            Assert.That(SpecialActionService.GetControllerId("", null), Is.Null);
            Assert.That(SpecialActionService.GetControllerId(null, null), Is.Null);
        });
    }

    [Test]
    public void IsEnabledFor_IsCaseInsensitiveAndNullSafe()
    {
        SpecialAction action = new SpecialAction();
        action.EnabledControllers.Add("AA:BB:CC:DD:EE:FF");
        Assert.Multiple(() =>
        {
            Assert.That(SpecialActionService.IsEnabledFor(action, "aa:bb:cc:dd:ee:ff"), Is.True);
            Assert.That(SpecialActionService.IsEnabledFor(action, "AA:BB:CC:DD:EE:FF"), Is.True);
            Assert.That(SpecialActionService.IsEnabledFor(action, "00:00:00:00:00:00"), Is.False);
            Assert.That(SpecialActionService.IsEnabledFor(action, null), Is.False);
            Assert.That(SpecialActionService.IsEnabledFor(action, ""), Is.False);
        });
    }

    [Test]
    public void Save_FiresSpecialActionsChangedEvent()
    {
        SpecialActionService service = CreateService();
        bool eventFired = false;
        service.SpecialActionsChanged += (_, _) => eventFired = true;
        service.CreateAction(null, null);
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void CreateAction_SeedsDefaultLightbarEffect()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction(null, null);
        Assert.That(action.Effects, Has.Count.EqualTo(1));
        Assert.That(action.Effects[0].Type, Is.EqualTo(SpecialActionTypes.SetLightbarColor));
    }

    [Test]
    public void RoundTrip_PreservesActionData()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction("Multi", "AA:BB:CC:DD:EE:FF");
        action.Buttons.Add("L1");
        action.Buttons.Add("R1");
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetLightbarColor,
            Red = 0xAA,
            Green = 0xBB,
            Blue = 0xCC
        });
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetPlayerLeds,
            PlayerLedMask = 0x07
        });
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.PlaySound,
            SoundPath = @"C:\sounds\beep.mp3",
            SoundVolume = 0x7F,
            HapticFeedback = true,
            HapticStrength = 150
        });
        action.Effects.RemoveAt(0);
        action.HoldTimeMs = 1500;
        action.ApplyWhileHeld = true;
        action.DurationMs = 3000;
        service.Save();

        SpecialActionService reloaded = CreateService();
        SpecialAction? loaded = reloaded.Settings.Actions.FirstOrDefault(a => a.Id == action.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.EqualTo("Multi"));
            Assert.That(loaded.Buttons, Is.EqualTo(new[] { "L1", "R1" }));
            Assert.That(loaded.Effects, Has.Count.EqualTo(3));
            Assert.That(loaded.Effects[0].Type, Is.EqualTo(SpecialActionTypes.SetLightbarColor));
            Assert.That(loaded.Effects[0].Red, Is.EqualTo(0xAA));
            Assert.That(loaded.Effects[0].Green, Is.EqualTo(0xBB));
            Assert.That(loaded.Effects[0].Blue, Is.EqualTo(0xCC));
            Assert.That(loaded.Effects[1].Type, Is.EqualTo(SpecialActionTypes.SetPlayerLeds));
            Assert.That(loaded.Effects[1].PlayerLedMask, Is.EqualTo(0x07));
            Assert.That(loaded.Effects[2].Type, Is.EqualTo(SpecialActionTypes.PlaySound));
            Assert.That(loaded.Effects[2].SoundPath, Is.EqualTo(@"C:\sounds\beep.mp3"));
            Assert.That(loaded.Effects[2].SoundVolume, Is.EqualTo(0x7F));
            Assert.That(loaded.Effects[2].HapticFeedback, Is.True);
            Assert.That(loaded.Effects[2].HapticStrength, Is.EqualTo(150));
            Assert.That(loaded.HoldTimeMs, Is.EqualTo(1500));
            Assert.That(loaded.ApplyWhileHeld, Is.True);
            Assert.That(loaded.DurationMs, Is.EqualTo(3000));
            Assert.That(loaded.EnabledControllers, Is.EqualTo(new[] { "AA:BB:CC:DD:EE:FF" }));
        });
    }

    [Test]
    public void RoundTrip_PreservesBatteryEffectColors()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction("Battery", null);
        action.Buttons.Add("L1");
        action.Effects[0].Type = SpecialActionTypes.ShowBatteryLevel;
        action.Effects[0].BatteryColors = Enumerable.Range(0, 10)
            .Select(i => new BatteryLevelColor { Red = (byte)(255 - i * 20), Green = (byte)i, Blue = (byte)(i * 10) })
            .ToList();
        service.Save();

        SpecialActionService reloaded = CreateService();
        SpecialAction? loaded = reloaded.Settings.Actions.FirstOrDefault(a => a.Id == action.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Effects, Has.Count.EqualTo(1));
            Assert.That(loaded.Effects[0].Type, Is.EqualTo(SpecialActionTypes.ShowBatteryLevel));
            Assert.That(loaded.Effects[0].BatteryColors, Has.Count.EqualTo(10));
            Assert.That(loaded.Effects[0].BatteryColors![0].Red, Is.EqualTo(255));
            Assert.That(loaded.Effects[0].BatteryColors![0].Green, Is.EqualTo(0));
            Assert.That(loaded.Effects[0].BatteryColors![9].Red, Is.EqualTo(75));
            Assert.That(loaded.Effects[0].BatteryColors![9].Blue, Is.EqualTo(90));
        });
    }

    [Test]
    public void BatteryEffect_MissingColors_FallBackToDefaults()
    {
        SpecialActionService service = CreateService();
        SpecialAction action = service.CreateAction("Battery", null);
        action.Effects[0].Type = SpecialActionTypes.ShowBatteryLevel;
        service.Save();

        SpecialActionService reloaded = CreateService();
        SpecialActionEffect effect = reloaded.Settings.Actions[0].Effects[0];
        Assert.Multiple(() =>
        {
            Assert.That(effect.BatteryColors, Is.Null);
            Assert.That(effect.GetBatteryColor(0).Red, Is.EqualTo(255));
            Assert.That(effect.GetBatteryColor(0).Green, Is.EqualTo(60));
            Assert.That(effect.GetBatteryColor(9).Blue, Is.EqualTo(110));
            Assert.That(effect.GetBatteryColor(99).Red, Is.EqualTo(40)); // out of range clamps to highest
        });
    }

    [Test]
    public void Load_MigratesLegacySingleTypeActions()
    {
        Guid lightbarId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid soundId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        WriteActionsJson($$"""
                           {"actions":[
                             {"id":"{{lightbarId}}","name":"Legacy Lightbar","buttons":["L1","R1"],"type":"SetLightbarColor","red":10,"green":20,"blue":30},
                             {"id":"{{soundId}}","name":"Legacy Sound","type":"PlaySound","sound_path":"C:\\sounds\\beep.mp3","sound_volume":127,"haptic_feedback":true,"haptic_strength":150}
                           ]}
                           """);

        SpecialActionService service = CreateService();
        Assert.That(service.Settings.Actions, Has.Count.EqualTo(2));

        SpecialAction? lightbar = service.Settings.Actions.FirstOrDefault(a => a.Id == lightbarId);
        Assert.Multiple(() =>
        {
            Assert.That(lightbar, Is.Not.Null);
            Assert.That(lightbar!.Effects, Has.Count.EqualTo(1));
            Assert.That(lightbar.Effects[0].Type, Is.EqualTo(SpecialActionTypes.SetLightbarColor));
            Assert.That(lightbar.Effects[0].Red, Is.EqualTo(10));
            Assert.That(lightbar.Effects[0].Green, Is.EqualTo(20));
            Assert.That(lightbar.Effects[0].Blue, Is.EqualTo(30));
            Assert.That(lightbar.Buttons, Is.EqualTo(new[] { "L1", "R1" }));
        });

        SpecialAction? sound = service.Settings.Actions.FirstOrDefault(a => a.Id == soundId);
        Assert.Multiple(() =>
        {
            Assert.That(sound, Is.Not.Null);
            Assert.That(sound!.Effects, Has.Count.EqualTo(1));
            Assert.That(sound.Effects[0].Type, Is.EqualTo(SpecialActionTypes.PlaySound));
            Assert.That(sound.Effects[0].SoundPath, Is.EqualTo(@"C:\sounds\beep.mp3"));
            Assert.That(sound.Effects[0].SoundVolume, Is.EqualTo(127));
            Assert.That(sound.Effects[0].HapticFeedback, Is.True);
            Assert.That(sound.Effects[0].HapticStrength, Is.EqualTo(150));
        });

        Assert.That(File.ReadAllText(ActionsPath), Does.Not.Contain("\"type\":\"SetLightbarColor\""));
    }

    [Test]
    public void Load_LegacyMigration_KeepsExistingEffects()
    {
        Guid modernId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid legacyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        WriteActionsJson($$"""
                           {"actions":[
                             {"id":"{{modernId}}","name":"Modern","type":"Disconnect","effects":[{"type":"SetPlayerLeds","player_leds":5}]},
                             {"id":"{{legacyId}}","name":"Legacy","type":"SetLightbarColor","red":9,"green":8,"blue":7}
                           ]}
                           """);

        SpecialActionService service = CreateService();
        SpecialAction? modern = service.Settings.Actions.FirstOrDefault(a => a.Id == modernId);
        Assert.Multiple(() =>
        {
            Assert.That(modern, Is.Not.Null);
            Assert.That(modern!.Effects, Has.Count.EqualTo(1));
            Assert.That(modern.Effects[0].Type, Is.EqualTo(SpecialActionTypes.SetPlayerLeds));
        });

        SpecialAction? legacy = service.Settings.Actions.FirstOrDefault(a => a.Id == legacyId);
        Assert.Multiple(() =>
        {
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy!.Effects, Has.Count.EqualTo(1));
            Assert.That(legacy.Effects[0].Type, Is.EqualTo(SpecialActionTypes.SetLightbarColor));
            Assert.That(legacy.Effects[0].Red, Is.EqualTo(9));
        });
    }

    [Test]
    public void Load_LegacyMigration_MissingType_IsSkipped()
    {
        Guid legacyId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        WriteActionsJson($$"""
                           {"actions":[{"id":"{{legacyId}}","name":"No Type","red":1,"green":2,"blue":3}]}
                           """);

        SpecialActionService service = CreateService();
        SpecialAction? action = service.Settings.Actions.FirstOrDefault(a => a.Id == legacyId);
        Assert.Multiple(() =>
        {
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.Effects, Is.Empty);
        });
    }

    [Test]
    public void Load_UnknownProperties_DoesNotLoseActions()
    {
        string path = Path.Combine(_tempDir, "special_actions.json");
        File.WriteAllText(path, """{"actions":[{"name":"One","unknown_field":123},{"name":"Two"}]}""");
        SpecialActionService service = new SpecialActionService(actionsPath: path);
        Assert.That(service.Settings.Actions.Select(a => a.Name), Is.EquivalentTo(new[] { "One", "Two" }));
    }

    [Test]
    public void ExportThenImport_RoundTripsActionsAndEffects()
    {
        SpecialActionService source = CreateService();
        SpecialAction action = source.CreateAction("Beep", "AA:BB:CC:DD:EE:FF");
        action.Effects.Clear();
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetLightbarColor,
            Red = 12,
            Green = 34,
            Blue = 56
        });
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.PlaySound,
            SoundPath = @"C:\sounds\beep.wav",
            SoundVolume = 0x7F,
            SoundOutputDevice = SoundOutputDevices.Headset,
            HapticFeedback = true,
            HapticStrength = 150
        });
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.ShowBatteryLevel,
            BatteryColors = [new BatteryLevelColor { Red = 1, Green = 2, Blue = 3 }]
        });

        string exportPath = Path.Combine(_tempDir, "export.json");
        source.ExportActions(exportPath);

        SpecialActionService target = new SpecialActionService(actionsPath: Path.Combine(_tempDir, "target", "special_actions.json"));
        int count = target.ImportActions(exportPath);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(target.Settings.Actions, Has.Count.EqualTo(1));
        });

        SpecialAction imported = target.Settings.Actions[0];
        Assert.Multiple(() =>
        {
            Assert.That(imported.Name, Is.EqualTo("Beep"));
            Assert.That(imported.EnabledControllers, Is.Empty);
            Assert.That(imported.Id, Is.Not.EqualTo(action.Id));
            Assert.That(imported.Effects, Has.Count.EqualTo(3));
        });

        SpecialActionEffect lightbar = imported.Effects[0];
        Assert.Multiple(() =>
        {
            Assert.That(lightbar.Type, Is.EqualTo(SpecialActionTypes.SetLightbarColor));
            Assert.That(lightbar.Red, Is.EqualTo(12));
            Assert.That(lightbar.Green, Is.EqualTo(34));
            Assert.That(lightbar.Blue, Is.EqualTo(56));
        });

        SpecialActionEffect sound = imported.Effects[1];
        Assert.Multiple(() =>
        {
            Assert.That(sound.Type, Is.EqualTo(SpecialActionTypes.PlaySound));
            Assert.That(sound.SoundPath, Is.EqualTo(@"C:\sounds\beep.wav"));
            Assert.That(sound.SoundVolume, Is.EqualTo(0x7F));
            Assert.That(sound.SoundOutputDevice, Is.EqualTo(SoundOutputDevices.Headset));
            Assert.That(sound.HapticFeedback, Is.True);
            Assert.That(sound.HapticStrength, Is.EqualTo(150));
        });

        SpecialActionEffect battery = imported.Effects[2];
        Assert.Multiple(() =>
        {
            Assert.That(battery.Type, Is.EqualTo(SpecialActionTypes.ShowBatteryLevel));
            Assert.That(battery.BatteryColors, Has.Count.EqualTo(1));
            Assert.That(battery.BatteryColors![0].Red, Is.EqualTo(1));
            Assert.That(battery.BatteryColors[0].Blue, Is.EqualTo(3));
        });
    }

    [Test]
    public void ExportActions_WritesSettingsShape()
    {
        SpecialActionService source = CreateService();
        source.CreateAction("Beep", null);

        string exportPath = Path.Combine(_tempDir, "export.json");
        source.ExportActions(exportPath);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(exportPath));
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(doc.RootElement.TryGetProperty("actions", out JsonElement actions), Is.True);
            Assert.That(actions.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(actions.GetArrayLength(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ImportActions_AssignsFreshIdsAndUniqueNames()
    {
        SpecialActionService source = CreateService();
        source.CreateAction("Beep", null);
        string exportPath = Path.Combine(_tempDir, "export.json");
        source.ExportActions(exportPath);

        SpecialActionService target = new SpecialActionService(actionsPath: Path.Combine(_tempDir, "target", "special_actions.json"));
        Assert.That(target.ImportActions(exportPath), Is.EqualTo(1));
        Assert.That(target.ImportActions(exportPath), Is.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(target.Settings.Actions, Has.Count.EqualTo(2));
            Assert.That(target.Settings.Actions.Select(a => a.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(target.Settings.Actions.Select(a => a.Name), Is.EquivalentTo(new[] { "Beep", "Beep 2" }));
        });
    }

    [Test]
    public void ExportAction_WritesOnlyThatAction()
    {
        SpecialActionService source = CreateService();
        SpecialAction beep = source.CreateAction("Beep", null);
        source.CreateAction("Bop", null);

        string exportPath = Path.Combine(_tempDir, "export.json");
        source.ExportAction(beep.Id, exportPath);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(exportPath));
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("actions", out JsonElement actions), Is.True);
            Assert.That(actions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(actions[0].GetProperty("name").GetString(), Is.EqualTo("Beep"));
        });

        SpecialActionService target = new SpecialActionService(actionsPath: Path.Combine(_tempDir, "target", "special_actions.json"));
        Assert.That(target.ImportActions(exportPath), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(target.Settings.Actions, Has.Count.EqualTo(1));
            Assert.That(target.Settings.Actions[0].Name, Is.EqualTo("Beep"));
        });
    }

    [Test]
    public void ExportAction_MissingId_Throws()
    {
        SpecialActionService source = CreateService();

        Assert.Throws<ArgumentException>(() => source.ExportAction(Guid.NewGuid(), Path.Combine(_tempDir, "export.json")));
    }

    [Test]
    public void ExportActions_OmitsControllers()
    {
        SpecialActionService source = CreateService();
        SpecialAction action = source.CreateAction("Beep", "AA:BB:CC:DD:EE:FF");

        string fullPath = Path.Combine(_tempDir, "full.json");
        source.ExportActions(fullPath);
        string singlePath = Path.Combine(_tempDir, "single.json");
        source.ExportAction(action.Id, singlePath);

        using JsonDocument full = JsonDocument.Parse(File.ReadAllText(fullPath));
        using JsonDocument single = JsonDocument.Parse(File.ReadAllText(singlePath));
        Assert.Multiple(() =>
        {
            Assert.That(full.RootElement.GetProperty("actions")[0].TryGetProperty("controllers", out _), Is.False);
            Assert.That(single.RootElement.GetProperty("actions")[0].TryGetProperty("controllers", out _), Is.False);
        });

        SpecialActionService target = new SpecialActionService(actionsPath: Path.Combine(_tempDir, "target", "special_actions.json"));
        Assert.That(target.ImportActions(fullPath), Is.EqualTo(1));
        Assert.That(target.Settings.Actions[0].EnabledControllers, Is.Empty);
    }

    [Test]
    public void ImportActions_BareArray_IsSupported()
    {
        string path = Path.Combine(_tempDir, "import.json");
        File.WriteAllText(path, """[{"name":"Bare","buttons":["Cross"],"effects":[{"type":"Disconnect"}]}]""");

        SpecialActionService target = CreateService();
        int count = target.ImportActions(path);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(target.Settings.Actions[0].Name, Is.EqualTo("Bare"));
            Assert.That(target.Settings.Actions[0].Effects[0].Type, Is.EqualTo(SpecialActionTypes.Disconnect));
        });
    }

    [Test]
    public void ImportActions_InvalidFile_ReturnsZero()
    {
        string path = Path.Combine(_tempDir, "import.json");
        File.WriteAllText(path, "not valid json {{{");

        SpecialActionService target = CreateService();
        Assert.That(target.ImportActions(path), Is.EqualTo(0));
        Assert.That(target.Settings.Actions, Is.Empty);
    }

    [Test]
    public void ImportActions_UnrecognizedShape_ReturnsZero()
    {
        string path = Path.Combine(_tempDir, "import.json");
        File.WriteAllText(path, """{"profiles":[{"name":"Not actions"}]}""");

        SpecialActionService target = CreateService();
        Assert.That(target.ImportActions(path), Is.EqualTo(0));
        Assert.That(target.Settings.Actions, Is.Empty);
    }
}