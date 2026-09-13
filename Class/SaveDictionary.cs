using Newtonsoft.Json;
using Other;
using System.IO;
using MessageBox = System.Windows.MessageBox;

namespace Class
{
    internal class SaveDictionary
    {
        private static bool IsObsoleteRecoilSetting(string key)
        {
            if (!key.StartsWith("Recoil Scope ", StringComparison.OrdinalIgnoreCase)) return false;
            return key.EndsWith(" S5 Force", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(" S5 Time", StringComparison.OrdinalIgnoreCase);
        }

        // Ensure all required directories exist at startup
        public static void EnsureDirectoriesExist()
        {
            var requiredDirectories = new[]
            {
                "bin",
                "bin\\configs",
                "bin\\labels",
                "bin\\models"
            };

            foreach (var dir in requiredDirectories)
            {
                if (!Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, $"Failed to create directory {dir}: {ex.Message}", true);
                    }
                }
            }
        }
        public static void WriteJSON(Dictionary<string, dynamic> dictionary, string path = "bin\\configs\\Default.cfg", string SuggestedModel = "", string ExtraStrings = "")
        {
            try
            {
                // Ensure the directory exists
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                foreach (string key in dictionary.Keys.Where(IsObsoleteRecoilSetting).ToArray()) dictionary.Remove(key);
                var SavedJSONSettings = dictionary
                    .Where(pair => !IsObsoleteRecoilSetting(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                // Preserve unknown keys written by newer/custom versions.
                if (File.Exists(path))
                {
                    var previous = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(File.ReadAllText(path));
                    if (previous != null)
                    {
                        if (!previous.ContainsKey("ConfigVersion") && !File.Exists(path + ".v1.bak")) File.Copy(path, path + ".v1.bak", false);
                        foreach (var pair in previous)
                            if (!IsObsoleteRecoilSetting(pair.Key) && !SavedJSONSettings.ContainsKey(pair.Key)) SavedJSONSettings[pair.Key] = pair.Value;
                    }
                }
                SavedJSONSettings["ConfigVersion"] = 2;
                if (!string.IsNullOrEmpty(SuggestedModel) && SavedJSONSettings.ContainsKey("Suggested Model"))
                {
                    SavedJSONSettings["Suggested Model"] = SuggestedModel + ".onnx" + ExtraStrings;
                }

                string json = JsonConvert.SerializeObject(SavedJSONSettings, Formatting.Indented);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex)
            {
                // Only show error if it's not a directory creation issue
                global::Other.LocalizedMessageBox.Show($"Error writing JSON, please note:\n{ex}");
            }
        }

        public static void LoadJSON(Dictionary<string, dynamic> dictionary, string path = "bin\\configs\\Default.cfg", bool strict = true)
        {
            try
            {
                // Ensure the directory exists before checking for the file
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(path))
                {
                    WriteJSON(dictionary, path);
                    return;
                }

                var configuration = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(File.ReadAllText(path));
                if (configuration == null) return;

                foreach (string key in dictionary.Keys.Where(IsObsoleteRecoilSetting).ToArray()) dictionary.Remove(key);
                foreach (var (key, value) in configuration)
                {
                    if (IsObsoleteRecoilSetting(key)) continue;
                    if (dictionary.ContainsKey(key))
                    {
                        dictionary[key] = value;
                    }
                    else if (!strict)
                    {
                        dictionary.Add(key, value);
                    }
                }
            }
            catch (Exception ex)
            {
                // A malformed file may contain valuable settings. Never overwrite it with defaults.
                LogManager.Log(LogManager.LogLevel.Error, $"Không đọc được cấu hình {Path.GetFileName(path)}; file gốc được giữ nguyên. {ex.Message}");
            }
        }
    }
}
