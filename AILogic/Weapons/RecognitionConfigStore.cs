using Aimmy2.Class;
using Aimmy2.AILogic.Recognition;
using Newtonsoft.Json;
using Other;
using System.IO;

namespace Aimmy2.AILogic.Weapons;

public sealed class RecognitionConfigStore
{
    public string PathName { get; }
    public bool LoadedMalformedFile { get; private set; }
    public RecognitionConfigStore(string? path = null) => PathName = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs", "recognition.json");
    public RecognitionConfiguration Load()
    {
        try
        {
            if (File.Exists(PathName))
            {
                var config = JsonConvert.DeserializeObject<RecognitionConfiguration>(File.ReadAllText(PathName)) ?? new();
                if (config.ConfigVersion < 3)
                {
                    config.WeaponTemplateSettings = TemplateMatchingSettings.FromLegacy(config.Settings);
                    config.ScopeTemplateSettings = TemplateMatchingSettings.FromLegacy(config.Settings);
                }
                if (config.ConfigVersion < 4)
                {
                    config.WeaponFeatureSettings = FeatureMatchingSettings.FromLegacy(config.Settings);
                    config.ScopeFeatureSettings = FeatureMatchingSettings.FromLegacy(config.Settings);
                }
                if (config.ConfigVersion < 4) { config.ConfigVersion = 4; Save(config); }
                return config;
            }
        }
        catch (Exception ex)
        {
            LoadedMalformedFile = true;
            LogManager.Log(LogManager.LogLevel.Error, $"Recognition config is invalid; original file preserved: {ex.Message}");
            return new();
        }
        var migratedConfig = MigrateLegacy();
        BackupLegacy("recognition");
        Save(migratedConfig);
        return migratedConfig;
    }
    private void BackupLegacy(string suffix)
    {
        string legacy = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs", "Default.cfg");
        string backup = legacy + "." + suffix + ".bak";
        if (File.Exists(legacy) && !File.Exists(backup)) File.Copy(legacy, backup);
    }
    private static RecognitionConfiguration MigrateLegacy()
    {
        var config = new RecognitionConfiguration();
        if (Dictionary.filelocationState.TryGetValue("Scope Model Location", out var scopeModel)) config.ScopeModelPath = Convert.ToString(scopeModel) ?? config.ScopeModelPath;
        if (Dictionary.filelocationState.TryGetValue("Weapon Model Location", out var weaponModel)) config.WeaponModelPath = Convert.ToString(weaponModel) ?? "";
        RegionConfiguration Read(int slot)
        {
            string p = $"Weapon {slot} ";
            if (!Dictionary.sliderSettings.TryGetValue(p + "Width", out var width) || Convert.ToInt32(width) <= 0) return new();
            var region = new System.Drawing.Rectangle(Convert.ToInt32(Dictionary.sliderSettings[p + "X"]),
                Convert.ToInt32(Dictionary.sliderSettings[p + "Y"]), Convert.ToInt32(width), Convert.ToInt32(Dictionary.sliderSettings[p + "Height"]));
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => x.Bounds.IntersectsWith(region)) ?? System.Windows.Forms.Screen.PrimaryScreen;
            return screen == null ? new() : RegionConfiguration.FromPixels(region, screen.DeviceName, screen.Bounds);
        }
        // These legacy keys were always scope icon regions despite their names.
        config.ScopeSlot1Region = Read(1); config.ScopeSlot2Region = Read(2);
        return config;
    }
    public void Save(RecognitionConfiguration config)
    {
        if (LoadedMalformedFile) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
        if (File.Exists(PathName) && !File.Exists(PathName + ".v1.bak")) File.Copy(PathName, PathName + ".v1.bak");
        string temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonConvert.SerializeObject(config, Formatting.Indented)); File.Move(temp, PathName, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
