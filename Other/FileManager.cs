using Aimmy2.AILogic;
using Aimmy2.Class;
using Aimmy2.Other;
using Class;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using Visuality;

namespace Other
{
    internal class FileManager : IDisposable
    {
        public FileSystemWatcher? ModelFileWatcherSlot1;
        public FileSystemWatcher? ModelFileWatcherSlot2;
        public FileSystemWatcher? ConfigFileWatcher;

        public bool InQuittingState = false;

        public static AIManager? AIManager;

        private ListBox ModelListBox; // Slot 2 (Local)
        private Label SelectedModelNotifier; // Slot 2

        private ListBox Slot1ModelListBox; // Slot 1
        private Label Slot1Notifier; // Slot 1

        private ListBox ConfigListBox;
        public Label SelectedConfigNotifier;

        public FileManager(ListBox modelListBox, Label selectedModelNotifier, ListBox configListBox, Label selectedConfigNotifier, 
                           ListBox slot1ListBox, Label slot1Notifier)
        {
            ModelListBox = modelListBox; // Slot 2
            SelectedModelNotifier = selectedModelNotifier; // Slot 2
            
            Slot1ModelListBox = slot1ListBox;
            Slot1Notifier = slot1Notifier;

            ConfigListBox = configListBox;
            SelectedConfigNotifier = selectedConfigNotifier;

            ModelListBox.SelectionChanged += ModelListBox_SelectionChanged; // Slot 2
            Slot1ModelListBox.SelectionChanged += Slot1ModelListBox_SelectionChanged; // Slot 1

            ConfigListBox.SelectionChanged += ConfigListBox_SelectionChanged;

            ModelListBox.AllowDrop = true;
            ModelListBox.DragOver += ModelListBox_DragOver; 
            ModelListBox.Drop += ModelListBox_DragDrop;
            
            Slot1ModelListBox.AllowDrop = true;
            Slot1ModelListBox.DragOver += ModelListBox_DragOver; // Reuse same drag over
            Slot1ModelListBox.Drop += Slot1ModelListBox_DragDrop; // New handler for Slot 1

            ConfigListBox.AllowDrop = true;
            ConfigListBox.DragOver += ConfigListBox_DragDrop;
            ConfigListBox.Drop += ConfigListBox_DragDrop;

            CheckForRequiredFolders();
            InitializeFileWatchers();
            LoadSlot1Models(null, null);
            LoadSlot2Models(null, null);
            LoadConfigsIntoListBox(null, null);
        }

        private void CheckForRequiredFolders()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Add Slot1 and Slot2 specific folders
            string[] dirs = ["bin\\models", "bin\\models\\Slot1", "bin\\models\\Slot2", "bin\\images", "bin\\labels", "bin\\configs", "bin\\scope_models"];

            try
            {
                foreach (string dir in dirs)
                {
                    string fullPath = Path.Combine(baseDir, dir);
                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                global::Other.LocalizedMessageBox.Show($"Error creating a required directory: {ex}");
                Application.Current.Shutdown();
            }
        }

        public static bool CurrentlyLoadingModel = false;
        public static bool CurrentlyLoadingSecondaryModel = false;
        private static readonly SemaphoreSlim ModelLoadGate = new(1, 1);

        private static bool IsModelLoadedInSlot(int slot, string modelPath)
        {
            var metadata = AIManager?.GetModelMetadata(slot);
            return metadata != null && string.Equals(Path.GetFullPath(metadata.ModelPath), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string ModelLoadedMessage(int slot, ModelMetadata metadata)
        {
            int preferredSize = AIManager?.GetSlotImageSize(slot) ?? 640;
            var size = metadata.ResolveSize(preferredSize);
            string dimensions = $"{size.Width} × {size.Height}";
            string shape = metadata.Dynamic ? "kích thước động" : "kích thước cố định";
            return $"Model {slot} đã nạp thành công: {metadata.Backend}, {metadata.Input.DataType}, {dimensions}, {shape}";
        }

        private async void Slot1ModelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Slot1ModelListBox.SelectedItem == null) return;
            string selectedModel = Slot1ModelListBox.SelectedItem.ToString()!;
            
            // Slot 1 models are now in Slot1 folder
            string modelPath = Path.Combine("bin/models/Slot1", selectedModel);

            bool isEngine = modelPath.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                            modelPath.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

            if (isEngine && !AIManager.IsTensorRTAvaliable())
            {
                LogManager.Log(LogManager.LogLevel.Error, "TensorRT (.engine) is not supported on this machine. CUDA 12.x or TensorRT 10.x DLLs not found in PATH.", true, 8000);
                
                string? firstOnnx = Slot1ModelListBox.Items.Cast<object>()
                    .Select(x => x.ToString())
                    .FirstOrDefault(x => x != null && x.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

                if (firstOnnx != null)
                {
                    Slot1ModelListBox.SelectedItem = firstOnnx;
                }
                else
                {
                    Slot1ModelListBox.SelectedIndex = -1;
                }
                return;
            }

            await ModelLoadGate.WaitAsync();
            try
            {
                if (!string.Equals(Slot1ModelListBox.SelectedItem?.ToString(), selectedModel, StringComparison.Ordinal)) return;
                if (Dictionary.lastLoadedModel == selectedModel && IsModelLoadedInSlot(1, modelPath))
                {
                    Slot1Notifier.Content = "Đã tải Model 1: " + selectedModel;
                    var metadata = AIManager!.GetModelMetadata(1)!;
                    NoticeBar.Show(ModelLoadedMessage(1, metadata), 5000, NoticeType.Success, bypassThrottle: true);
                    return;
                }

                CurrentlyLoadingModel = true;
                string previousModel = Dictionary.lastLoadedModel;
                Dictionary.lastLoadedModel = selectedModel;
                LogManager.Log(LogManager.LogLevel.Info, $"Đang nạp Model Slot 1: {selectedModel}...", true, 2000);

                Dictionary<string, dynamic>? originalToggleStates = null;
                try
                {
                // Pause AI features
                var toggleKeys = new[] { "Aim Assist", "Constant AI Tracking", "Auto Trigger", "Show Detected Player", "Show AI Confidence", "Show Tracers" };
                originalToggleStates = toggleKeys.ToDictionary(key => key, key => Dictionary.toggleState[key]);
                foreach (var key in toggleKeys) Dictionary.toggleState[key] = false;

                await Task.Delay(150);

                // Dispose old AIManager if exists (full reload for Primary Slot)
                var oldAIManager = AIManager;
                AIManager = null;
                if (oldAIManager != null)
                {
                    await Task.Run(() => oldAIManager.Dispose());
                }
                // Load Slot 1 model from specific path
                // FileManager presents one consolidated completion toast. The loader still logs
                // failures, but does not emit a separate metadata INFO toast for Slot 1.
                AIManager = await Task.Run(() => new AIManager(modelPath, showLoadNotification: false));
                await AIManager.Initialization;
                var loaded = AIManager.GetModelMetadata(1);
                if (loaded == null)
                    throw new InvalidOperationException("Model Slot 1 không tải thành công.");

                // If Slot 2 was previously loaded, try to reload it into the new AIManager
                if (Dictionary.lastLoadedModelSlot2 != "N/A")
                {
                    string slot2Path = Path.Combine("bin/models/Slot2", Dictionary.lastLoadedModelSlot2);
                    if (File.Exists(slot2Path))
                    {
                        CurrentlyLoadingSecondaryModel = true;
                        try
                        {
                            await AIManager.LoadSecondaryModel(slot2Path, showNotification: false);
                        }
                        finally
                        {
                            CurrentlyLoadingSecondaryModel = false;
                        }
                    }
                }

                Dictionary.modelState["Slot1"] = selectedModel;
                SaveDictionary.WriteJSON(Dictionary.modelState, "bin\\models.cfg");
                Slot1Notifier.Content = "Đã tải Model 1: " + selectedModel;
                NoticeBar.Show(ModelLoadedMessage(1, loaded), 5000, NoticeType.Success, bypassThrottle: true);
                }
                catch (Exception ex)
                {
                    Dictionary.lastLoadedModel = previousModel;
                    Slot1Notifier.Content = "Không tải được Slot 1";
                    LogManager.Log(LogManager.LogLevel.Error, $"Không tải được model Slot 1: {ex.Message}", true, 8000);
                }
                finally
                {
                    if (originalToggleStates != null)
                        foreach (var pair in originalToggleStates) Dictionary.toggleState[pair.Key] = pair.Value;
                    CurrentlyLoadingModel = false;
                }
            }
            finally { ModelLoadGate.Release(); }
        }

        private async void ModelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Slot 2 Selection
            if (ModelListBox.SelectedItem == null) return;

            string selectedModel = ModelListBox.SelectedItem.ToString()!;
            // Slot 2 models are now in Slot2 folder
            string modelPath = Path.Combine("bin/models/Slot2", selectedModel);

            bool isEngine = modelPath.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                            modelPath.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

            if (isEngine && !AIManager.IsTensorRTAvaliable())
            {
                LogManager.Log(LogManager.LogLevel.Error, "TensorRT (.engine) is not supported on this machine. CUDA 12.x or TensorRT 10.x DLLs not found in PATH.", true, 8000);
                
                string? firstOnnx = ModelListBox.Items.Cast<object>()
                    .Select(x => x.ToString())
                    .FirstOrDefault(x => x != null && x.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

                if (firstOnnx != null)
                {
                    ModelListBox.SelectedItem = firstOnnx;
                }
                else
                {
                    ModelListBox.SelectedIndex = -1;
                }
                return;
            }

            await ModelLoadGate.WaitAsync();
            try
            {
                if (!string.Equals(ModelListBox.SelectedItem?.ToString(), selectedModel, StringComparison.Ordinal)) return;
                if (Dictionary.lastLoadedModelSlot2 == selectedModel && IsModelLoadedInSlot(2, modelPath))
                {
                    SelectedModelNotifier.Content = "Đã kích hoạt Model 2: " + selectedModel;
                    var metadata = AIManager!.GetModelMetadata(2)!;
                    NoticeBar.Show(ModelLoadedMessage(2, metadata), 5000, NoticeType.Success, bypassThrottle: true);
                    return;
                }

                LogManager.Log(LogManager.LogLevel.Info, $"Đang nạp Model Slot 2: {selectedModel}...", true, 2000);

                if (AIManager != null)
                {
                    CurrentlyLoadingSecondaryModel = true;
                    try
                    {
                        // FileManager owns the user-facing loading/result messages. Suppress the
                        // loader's duplicate completion toast so it cannot throttle this success toast.
                        await AIManager.LoadSecondaryModel(modelPath, showNotification: false);
                        var loaded = AIManager.GetModelMetadata(2);
                        if (loaded == null || !string.Equals(Path.GetFullPath(loaded.ModelPath), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Model Slot 2 không tải thành công.");
                        AIManager.SetActiveSlot(2);
                        if (AIManager.ActiveSlot != 2)
                            throw new InvalidOperationException("Model Slot 2 đã nạp nhưng không thể kích hoạt.");
                        Dictionary.lastLoadedModelSlot2 = selectedModel;
                        Dictionary.modelState["Slot2"] = selectedModel;
                        SaveDictionary.WriteJSON(Dictionary.modelState, "bin\\models.cfg");
                        string content = "Đã kích hoạt Model 2: " + selectedModel;
                        SelectedModelNotifier.Content = content;
                        NoticeBar.Show(ModelLoadedMessage(2, loaded), 5000, NoticeType.Success, bypassThrottle: true);
                    }
                    catch (Exception ex)
                    {
                        SelectedModelNotifier.Content = "Không tải được Slot 2";
                        LogManager.Log(LogManager.LogLevel.Error, $"Không tải được model Slot 2: {ex.Message}", true, 8000);
                    }
                    finally
                    {
                        CurrentlyLoadingSecondaryModel = false;
                    }
                }
                else
                {
                    SelectedModelNotifier.Content = "Hãy tải Model 1 trước";
                    LogManager.Log(LogManager.LogLevel.Warning, "Vui lòng tải Model 1 trước khi tải Model 2.", true, 5000);
                }
            }
            finally { ModelLoadGate.Release(); }
        }

        private void ConfigListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConfigListBox.SelectedItem == null) return;
            string selectedConfig = ConfigListBox.SelectedItem.ToString()!;

            string configPath = Path.Combine("bin/configs", selectedConfig);

            SaveDictionary.LoadJSON(Dictionary.sliderSettings, configPath, false);
            SaveDictionary.LoadJSON(Dictionary.dropdownState, configPath, false);
            PriorityAimingConfig.Load();
            Dictionary.lastLoadedConfig = selectedConfig;

            // Persist the active configuration name immediately to prevent loss on unexpected shutdown
            Dictionary.modelState["LastLoadedConfig"] = selectedConfig;
            SaveDictionary.WriteJSON(Dictionary.modelState, "bin\\models.cfg");

            PropertyChanger.PostNewConfig(configPath, true);

            SelectedConfigNotifier.Content = "Loaded Config: " + selectedConfig;
        }

        public void InitializeFileWatchers()
        {
            ModelFileWatcherSlot1 = new FileSystemWatcher();
            ModelFileWatcherSlot2 = new FileSystemWatcher();
            ConfigFileWatcher = new FileSystemWatcher();

            // Watch Slot 1
            InitializeWatcher(ref ModelFileWatcherSlot1, "bin/models/Slot1", "*", LoadSlot1Models);
            // Watch Slot 2
            InitializeWatcher(ref ModelFileWatcherSlot2, "bin/models/Slot2", "*", LoadSlot2Models);
            
            InitializeWatcher(ref ConfigFileWatcher, "bin/configs", "*.cfg", LoadConfigsIntoListBox);
        }

        private void InitializeWatcher(ref FileSystemWatcher watcher, string path, string filter, FileSystemEventHandler handler)
        {
            watcher.Path = path;
            watcher.Filter = filter;
            watcher.EnableRaisingEvents = true;

            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Renamed += (s, e) => handler(s, e);
        }

        public void Dispose()
        {
            InQuittingState = true;
            ModelListBox.SelectionChanged -= ModelListBox_SelectionChanged;
            Slot1ModelListBox.SelectionChanged -= Slot1ModelListBox_SelectionChanged;
            ConfigListBox.SelectionChanged -= ConfigListBox_SelectionChanged;
            foreach (var watcher in new[] { ModelFileWatcherSlot1, ModelFileWatcherSlot2, ConfigFileWatcher })
            {
                if (watcher == null) continue;
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            ModelFileWatcherSlot1 = ModelFileWatcherSlot2 = ConfigFileWatcher = null;
        }

        private void ModelListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        // Drag Drop for Slot 2 (ModelListBox)
        private void ModelListBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string targetFolder = "bin/models/Slot2"; // Target Slot 2 folder

                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".onnx" || ext == ".engine" || ext == ".trt")
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(targetFolder, fileName);
                        File.Move(file, destFile, true);
                    }
                }
            }
        }

        // Drag Drop for Slot 1 (Slot1ModelListBox)
        private void Slot1ModelListBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string targetFolder = "bin/models/Slot1"; // Target Slot 1 folder

                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".onnx" || ext == ".engine" || ext == ".trt")
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(targetFolder, fileName);
                        File.Move(file, destFile, true);
                    }
                }
            }
        }

        private void ConfigListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void ConfigListBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string targetFolder = "bin/configs";

                foreach (var file in files)
                {
                    if (Path.GetExtension(file) == ".cfg")
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(targetFolder, fileName);
                        File.Move(file, destFile, true);
                    }
                }
            }
        }

        // New method to load ONLY Slot 1 models
        public void LoadSlot1Models(object? sender, FileSystemEventArgs? e)
        {
             if (e != null)
             {
                 string ext = Path.GetExtension(e.FullPath).ToLower();
                 if (ext != ".onnx" && ext != ".engine" && ext != ".trt") return;
             }

             if (!InQuittingState)
             {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!Directory.Exists("bin/models/Slot1")) return;
                    string[] modelFiles = Directory.GetFiles("bin/models/Slot1", "*.*")
                        .Where(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".trt", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    Slot1ModelListBox.Items.Clear();

                    foreach (string filePath in modelFiles)
                    {
                        Slot1ModelListBox.Items.Add(Path.GetFileName(filePath));
                    }

                    if (Slot1ModelListBox.Items.Count > 0)
                    {
                        string? lastLoadedSlot1 = Dictionary.lastLoadedModel;
                        if (lastLoadedSlot1 != "N/A" && Slot1ModelListBox.Items.Contains(lastLoadedSlot1)) 
                        { 
                            Slot1ModelListBox.SelectedItem = lastLoadedSlot1; 
                        }
                        Slot1Notifier.Content = $"Loaded Slot 1: {lastLoadedSlot1}";
                    }
                });
            }
        }

        // New method to load ONLY Slot 2 models
        public void LoadSlot2Models(object? sender, FileSystemEventArgs? e)
        {
             if (e != null)
             {
                 string ext = Path.GetExtension(e.FullPath).ToLower();
                 if (ext != ".onnx" && ext != ".engine" && ext != ".trt") return;
             }

             if (!InQuittingState)
             {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!Directory.Exists("bin/models/Slot2")) return;
                    string[] modelFiles = Directory.GetFiles("bin/models/Slot2", "*.*")
                        .Where(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".trt", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    ModelListBox.Items.Clear();

                    foreach (string filePath in modelFiles)
                    {
                        ModelListBox.Items.Add(Path.GetFileName(filePath));
                    }

                    if (ModelListBox.Items.Count > 0)
                    {
                        string? lastLoadedSlot2 = Dictionary.lastLoadedModelSlot2;
                        if (lastLoadedSlot2 != "N/A" && ModelListBox.Items.Contains(lastLoadedSlot2)) 
                        { 
                            ModelListBox.SelectedItem = lastLoadedSlot2; 
                        }
                        SelectedModelNotifier.Content = $"Loaded Slot 2: {lastLoadedSlot2}";
                    }
                });
            }
        }

        public void LoadConfigsIntoListBox(object? sender, FileSystemEventArgs? e)
        {
            if (!InQuittingState)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    string[] configFiles = Directory.GetFiles("bin/configs", "*.cfg");
                    ConfigListBox.Items.Clear();

                    foreach (string filePath in configFiles)
                    {
                        ConfigListBox.Items.Add(Path.GetFileName(filePath));
                    }

                    if (ConfigListBox.Items.Count > 0)
                    {
                        string? lastLoadedConfig = Dictionary.lastLoadedConfig;
                        if (lastLoadedConfig != "N/A" && ConfigListBox.Items.Contains(lastLoadedConfig)) { ConfigListBox.SelectedItem = lastLoadedConfig; }

                        SelectedConfigNotifier.Content = "Loaded Config: " + lastLoadedConfig;
                    }
                });
            }
        }

        public static async Task<HashSet<string>> RetrieveAndAddFiles(string repoLink, string localPath, HashSet<string> allFiles)
        {
            try
            {
                GithubManager githubManager = new();

                var files = await githubManager.FetchGithubFilesAsync(repoLink);

                foreach (var file in files)
                {
                    if (file == null) continue;

                    if (!allFiles.Contains(file) && !File.Exists(Path.Combine(localPath, file)))
                    {
                        allFiles.Add(file);
                    }
                }

                githubManager.Dispose();

                return allFiles;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.ToString());
            }
        }
    }
}
