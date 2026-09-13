using Aimmy2.Class;
using Newtonsoft.Json;
using Other;
using System.IO;

namespace Aimmy2.AILogic.Weapons;

public sealed class ContinuousStage { public double Force { get; set; } public double DurationSeconds { get; set; } }
public sealed class TapSettings
{
    public bool Enabled { get; set; }
    public double ResetSeconds { get; set; } = 1;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<double> ShotDistances { get; set; } = new() { 40, 40, 40, 40, 40 };
}
public enum AutoFireMode { None, Sr, Dmr, Ar, Shotgun }
public sealed class AutoFireSettings
{
    public AutoFireMode Mode { get; set; }
    public double DmrIntervalMs { get; set; } = 120;
    public double ShotgunIntervalMs { get; set; } = 450;
}
public sealed class WeaponScopeRecoilProfile
{
    public const int ContinuousStageCount = 4;
    public string WeaponName { get; set; } = "Default Weapon";
    public string ScopeName { get; set; } = "Default Scope";
    public bool Enabled { get; set; } = true;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ContinuousStage> ContinuousStages { get; set; } = Enumerable.Range(0, ContinuousStageCount).Select(_ => new ContinuousStage()).ToList();
    public TapSettings Tap { get; set; } = new();
    public AutoFireSettings AutoFire { get; set; } = new();
    public double LastMeasuredBurstSeconds { get; set; }
    public double Step { get; set; } = 1;
    public double Delay { get; set; } = 2;
    public double Multiplier { get; set; } = 5;
    public double SensitivityMultiplier { get; set; } = 1;
}
public sealed class WeaponScopeRecoilConfiguration
{
    public int ConfigVersion { get; set; } = 5;
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WeaponScopeRecoilProfile> Profiles { get; set; } = new();
}

public sealed class WeaponScopeProfileStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private WeaponScopeRecoilConfiguration _config = new();
    private bool _malformed;
    public WeaponScopeProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs", "weapon_scope_recoil.json");
        Load();
    }
    public WeaponScopeRecoilProfile? Resolve(string? weapon, string? scope)
    {
        lock (_gate)
        {
            foreach (var key in new[] { (weapon, scope), (weapon, "Default Scope"), ("Default Weapon", scope), ("Default Weapon", "Default Scope") })
            {
                var candidates = _config.Profiles.Where(x => Eq(x.WeaponName, key.Item1));
                var found = candidates.FirstOrDefault(x => Eq(x.ScopeName, key.Item2))
                    ?? candidates.FirstOrDefault(x => ScopeNamesEqual(x.ScopeName, key.Item2));
                if (found?.Enabled == true) return found;
            }
            return null;
        }
    }
    public IReadOnlyList<WeaponScopeRecoilProfile> Snapshot() { lock (_gate) return _config.Profiles.ToArray(); }
    public WeaponScopeRecoilProfile GetOrCreateDefaultScopeForEditing(int scopeNumber, string scopeLabel)
    {
        lock (_gate)
        {
            var profile = _config.Profiles.FirstOrDefault(x => Eq(x.WeaponName, "Default Weapon") && ScopeNamesEqual(x.ScopeName, scopeLabel));
            if (profile == null)
            {
                profile = CreateLegacyProfile(scopeNumber, scopeLabel);
                _config.Profiles.Add(profile);
                SaveLocked();
            }
            return JsonConvert.DeserializeObject<WeaponScopeRecoilProfile>(JsonConvert.SerializeObject(profile))!;
        }
    }
    public double GetDefaultScopeSensitivity(IEnumerable<string> scopeLabels)
    {
        lock (_gate)
        {
            foreach (string label in scopeLabels)
            {
                var profile = _config.Profiles.FirstOrDefault(x => Eq(x.WeaponName, "Default Weapon") && ScopeNamesEqual(x.ScopeName, label));
                if (profile != null) return Math.Max(0, profile.SensitivityMultiplier);
            }
            return 1;
        }
    }
    public void SetDefaultScopeSensitivity(int scopeNumber, IEnumerable<string> scopeLabels, double sensitivity)
    {
        lock (_gate)
        {
            foreach (string label in scopeLabels)
            {
                var profile = _config.Profiles.FirstOrDefault(x => Eq(x.WeaponName, "Default Weapon") && ScopeNamesEqual(x.ScopeName, label));
                if (profile == null)
                {
                    profile = CreateLegacyProfile(scopeNumber, label);
                    _config.Profiles.Add(profile);
                }
                profile.Enabled = true;
                profile.SensitivityMultiplier = Math.Max(0, sensitivity);
            }
            SaveLocked();
        }
    }
    public void Upsert(WeaponScopeRecoilProfile profile, bool persist = true)
    {
        lock (_gate)
        {
            _config.Profiles.RemoveAll(x => Eq(x.WeaponName, profile.WeaponName) && Eq(x.ScopeName, profile.ScopeName));
            _config.Profiles.Add(profile);
            if (persist) SaveLocked();
        }
    }
    public void RenameLabel(bool scope, string oldLabel, string newLabel)
    {
        lock (_gate)
        {
            foreach (var profile in _config.Profiles)
            {
                if (scope && Eq(profile.ScopeName, oldLabel)) profile.ScopeName = newLabel;
                if (!scope && Eq(profile.WeaponName, oldLabel)) profile.WeaponName = newLabel;
            }
            SaveLocked();
        }
    }
    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                _config = JsonConvert.DeserializeObject<WeaponScopeRecoilConfiguration>(File.ReadAllText(_path)) ?? new();
                bool repaired = _config.ConfigVersion < 5;
                foreach (var profile in _config.Profiles)
                {
                    profile.ContinuousStages ??= new();
                    if (profile.ContinuousStages.Count > WeaponScopeRecoilProfile.ContinuousStageCount)
                    {
                        bool hasInjectedPrefix = profile.ContinuousStages.Count > 5
                            && profile.ContinuousStages.Take(5).All(stage => stage.Force == 0 && stage.DurationSeconds == 0)
                            && profile.ContinuousStages.Skip(5).Any(stage => stage.Force != 0 || stage.DurationSeconds != 0);
                        profile.ContinuousStages = (hasInjectedPrefix ? profile.ContinuousStages.Skip(5) : profile.ContinuousStages)
                            .Take(WeaponScopeRecoilProfile.ContinuousStageCount).ToList();
                        repaired = true;
                    }
                    while (profile.ContinuousStages.Count < WeaponScopeRecoilProfile.ContinuousStageCount)
                    {
                        double previousForce = profile.ContinuousStages.LastOrDefault()?.Force ?? 0;
                        profile.ContinuousStages.Add(new ContinuousStage { Force = previousForce });
                    }
                    profile.ContinuousStages[^1].DurationSeconds = 0;
                    profile.Tap ??= new();
                    profile.AutoFire ??= new();
                    profile.AutoFire.DmrIntervalMs = Math.Clamp(profile.AutoFire.DmrIntervalMs, 25, 2000);
                    profile.AutoFire.ShotgunIntervalMs = Math.Clamp(profile.AutoFire.ShotgunIntervalMs, 25, 2000);
                    profile.Tap.ShotDistances ??= new();
                    if (profile.Tap.ShotDistances.Count > 5)
                    {
                        profile.Tap.ShotDistances = profile.Tap.ShotDistances.TakeLast(5).ToList();
                        repaired = true;
                    }
                    while (profile.Tap.ShotDistances.Count < 5) profile.Tap.ShotDistances.Add(40);
                    if (Math.Abs(profile.SensitivityMultiplier - 1) > .000001)
                    {
                        double multiplier = Math.Max(0, profile.SensitivityMultiplier);
                        foreach (var stage in profile.ContinuousStages) stage.Force *= multiplier;
                        for (int i = 0; i < profile.Tap.ShotDistances.Count; i++) profile.Tap.ShotDistances[i] *= multiplier;
                        profile.SensitivityMultiplier = 1;
                        repaired = true;
                    }
                }
                _config.ConfigVersion = 5;
                if (repaired) SaveLocked();
            }
            else
            {
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs", "Default.cfg");
                string backup = legacy + ".recoil-migration.bak";
                if (File.Exists(legacy) && !File.Exists(backup)) File.Copy(legacy, backup);
                _config = MigrateLegacy(); SaveLocked();
            }
        }
        catch (Exception ex) { _malformed = true; LogManager.Log(LogManager.LogLevel.Error, $"Recoil profile config is invalid; original preserved: {ex.Message}"); }
    }
    private static WeaponScopeRecoilConfiguration MigrateLegacy()
    {
        string[][] scopes = { new[] { "chamdo", "morong" }, new[] { "2x" }, new[] { "3x" }, new[] { "4x" }, new[] { "6x" }, new[] { "8x" } };
        var result = new WeaponScopeRecoilConfiguration();
        for (int scope = 1; scope <= 6; scope++)
        {
            foreach (string label in scopes[scope - 1])
                result.Profiles.Add(CreateLegacyProfile(scope, label));
        }
        return result;
    }
    private static WeaponScopeRecoilProfile CreateLegacyProfile(int scope, string label)
    {
        double Get(string suffix, double fallback) => Dictionary.sliderSettings.TryGetValue($"Recoil Scope {scope} {suffix}", out var value) ? Convert.ToDouble(value) : fallback;
        var profile = new WeaponScopeRecoilProfile { WeaponName = "Default Weapon", ScopeName = label, Step = Get("Step", 1), Delay = Get("Delay", 2), Multiplier = Get("Multi", 5) };
        profile.Tap.Enabled = Dictionary.toggleState.TryGetValue($"Recoil Scope {scope} Tap", out var tap) && (bool)tap;
        profile.Tap.ResetSeconds = Get("Tap Reset Time", 1);
        profile.Tap.ShotDistances = Enumerable.Range(1, 5).Select(i => Get($"Tap Shot {i}", Get("Tap Distance", 40))).ToList();
        profile.ContinuousStages = Enumerable.Range(1, WeaponScopeRecoilProfile.ContinuousStageCount).Select(i => new ContinuousStage
        {
            Force = Get($"S{i} Force", 0),
            DurationSeconds = i < WeaponScopeRecoilProfile.ContinuousStageCount ? Get($"S{i} Time", 0) : 0
        }).ToList();
        return profile;
    }
    private void SaveLocked()
    {
        if (_malformed) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonConvert.SerializeObject(_config, Formatting.Indented)); File.Move(temp, _path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    internal static bool ScopeNamesEqual(string? a, string? b) => Eq(CanonicalScope(a), CanonicalScope(b));
    private static string CanonicalScope(string? value)
    {
        string compact = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        return compact is "chamdo" or "chấmđỏ" or "reddot" or "holo" or "holographic" or "1x" or "morong" ? "chamdo" : compact;
    }
}
