using Aimmy2.Theme;
using Class;
using InputLogic;
using System.Windows;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Aimmy2
{
    public partial class App : Application
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint uMode);

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Resolve all existing relative config/model paths beside this executable,
            // including when it is launched from a shortcut with another working folder.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            // Set up environment paths for CUDA and TensorRT DLLs
            ConfigureDllPaths();
            global::Other.UiLanguage.Initialize();

            if (e.Args.Length >= 2 && e.Args[0] == "--validate-engine")
            {
                // Suppress Windows Error Reporting dialogs for silent verification
                SetErrorMode(0x0002 | 0x0001 | 0x8000); // SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX

                string enginePath = e.Args[1];
                try
                {
                    using (var engine = new Aimmy2.AILogic.TensorRTEngine(enginePath))
                    {
                        // Warm-up validates buffers, output adapters and actual inference as well as deserialization.
                        int width = engine.InputDims[3], height = engine.InputDims[2];
                        var pixels = Enumerable.Repeat(0.5f, checked(width * height * 3)).ToArray();
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        var output = engine.RunDetections(pixels, new System.Drawing.Rectangle(0, 0, width, height), .25f);
                        timer.Stop();
                        if (e.Args.Length >= 3)
                            File.WriteAllText(e.Args[2], Newtonsoft.Json.JsonConvert.SerializeObject(new {
                                Passed = true, engine.Metadata, engine.InputDims, engine.ProfileMin, engine.ProfileMax,
                                DetectionShape = output.Dimensions.ToArray(), WarmupMilliseconds = timer.Elapsed.TotalMilliseconds
                            }, Newtonsoft.Json.Formatting.Indented));
                    }
                    System.Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    if (e.Args.Length >= 3)
                        File.WriteAllText(e.Args[2], Newtonsoft.Json.JsonConvert.SerializeObject(new { Passed = false, Error = ex.ToString() }, Newtonsoft.Json.Formatting.Indented));
                    System.Environment.Exit(1);
                }
            }

            // Initialize the application theme from saved settings
            InitializeTheme();

            // Set shutdown mode to prevent app from closing when startup window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

#if DEBUG
            var _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            _mainWindow.Show();
            return;
#endif
            // code IS reachable, only in release though
            try
            {
                // Create and show startup window
                var startupWindow = new StartupWindow();
                startupWindow.Show();

                // Reset shutdown mode after startup window is shown
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                // If startup window fails, launch main window directly
                global::Other.LocalizedMessageBox.Show($"Startup animation failed: {ex.Message}\nLaunching main application...",
                              "Aimmy AI", MessageBoxButton.OK, MessageBoxImage.Information);

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Some third-party WPF themes try to animate a shared/frozen brush when
            // the pointer is already over a control as the window appears. Do not
            // let that cosmetic visual-state failure terminate the whole program.
            if (e.Exception is InvalidOperationException invalidOperation &&
                invalidOperation.Message.Contains("Cannot animate", StringComparison.OrdinalIgnoreCase) &&
                invalidOperation.Message.Contains("immutable object", StringComparison.OrdinalIgnoreCase))
            {
                global::Other.LogManager.Log(global::Other.LogManager.LogLevel.Warning,
                    "Ignored an invalid frozen-brush visual-state animation: " + invalidOperation.Message);
                e.Handled = true;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LootManager.StopExternalLootProcesses();
            base.OnExit(e);
        }

        private void InitializeTheme()
        {
            try
            {
                // Load the color state configuration
                var colorState = new Dictionary<string, dynamic>
                {
                    { "Theme Color", "#FF722ED1" }
                };

                // Load saved colors
                SaveDictionary.LoadJSON(colorState, "bin\\colors.cfg");

                // Apply theme color if found
                if (colorState.TryGetValue("Theme Color", out var themeColor) && themeColor is string colorString)
                {
                    ThemeManager.SetThemeColor(colorString);
                }
                else
                {
                    // Use default purple if no saved color
                    ThemeManager.SetThemeColor("#FF722ED1");
                }
            }
            catch (Exception ex)
            {
                // Log error and use default color
                ThemeManager.SetThemeColor("#FF722ED1");
            }
        }

        private void ConfigureDllPaths()
        {
            try
            {
                var searchPaths = new List<string>();

                // 1. Search for CUDA path
                // Check CUDA_PATH environment variables first (set by official installer on any drive)
                foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
                {
                    string key = env.Key.ToString() ?? "";
                    if (key.StartsWith("CUDA_PATH", StringComparison.OrdinalIgnoreCase))
                    {
                        string? val = env.Value?.ToString();
                        if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
                        {
                            string binPath = Path.Combine(val, "bin");
                            if (Directory.Exists(binPath)) searchPaths.Add(binPath);
                        }
                    }
                }

                // Fallback to default path on C: if environment variables didn't resolve
                if (searchPaths.Count == 0)
                {
                    string cudaRoot = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
                    if (Directory.Exists(cudaRoot))
                    {
                        var cudaDirs = Directory.GetDirectories(cudaRoot);
                        var latestCudaBin = cudaDirs
                            .OrderByDescending(d => d)
                            .Select(d => Path.Combine(d, "bin"))
                            .FirstOrDefault(d => Directory.Exists(d));

                        if (latestCudaBin != null)
                        {
                            searchPaths.Add(latestCudaBin);
                        }
                    }
                }

                // 2. Search for TensorRT path
                // Check environment variables matching TENSORRT or TRT first
                foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
                {
                    string key = env.Key.ToString() ?? "";
                    if (key.Contains("TENSORRT", StringComparison.OrdinalIgnoreCase) || 
                        key.Contains("TRT_PATH", StringComparison.OrdinalIgnoreCase))
                    {
                        string? val = env.Value?.ToString();
                        if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
                        {
                            searchPaths.Add(val);
                            string binPath = Path.Combine(val, "bin");
                            if (Directory.Exists(binPath)) searchPaths.Add(binPath);
                        }
                    }
                }

                // Get all ready logical drives (C:\, D:\, E:\, etc.)
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                var potentialRoots = new List<string>(drives)
                {
                    @"C:\Program Files",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
                    AppDomain.CurrentDomain.BaseDirectory
                };

                // Add unique search root directories
                var uniqueRoots = potentialRoots.Distinct().Where(d => Directory.Exists(d));

                foreach (var root in uniqueRoots)
                {
                    try
                    {
                        var trtDirs = Directory.GetDirectories(root, "*TensorRT*", SearchOption.TopDirectoryOnly);
                        foreach (var trtDir in trtDirs)
                        {
                            string binPath = Path.Combine(trtDir, "bin");
                            if (Directory.Exists(binPath))
                            {
                                searchPaths.Add(binPath);
                            }
                            else if (Directory.Exists(trtDir))
                            {
                                searchPaths.Add(trtDir);
                            }
                        }
                    }
                    catch { }
                }

                // Prepend discovered paths to process PATH environment variable
                if (searchPaths.Count > 0)
                {
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    var existingPaths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var allPaths = searchPaths.Concat(existingPaths).Distinct();

                    string newPath = string.Join(";", allPaths);
                    Environment.SetEnvironmentVariable("PATH", newPath);
                }
            }
            catch
            {
                // Silent fallback
            }
        }
    }
}

