using System.Collections.Concurrent;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using Other;
using System.IO;

namespace Aimmy2.AILogic.Recognition;

public enum TemplateKind { Weapon, Scope }

public sealed class TemplateEntry : IDisposable
{
    public required string Label { get; init; }
    public required string FilePath { get; init; }
    public required Mat Gray { get; init; }
    public Mat? Edge { get; init; }
    public Mat? AlphaMask { get; init; }
    private readonly object _featureGate = new();
    private Mat? _orbDescriptors, _siftDescriptors;
    private KeyPoint[] _orbKeypoints = Array.Empty<KeyPoint>(), _siftKeypoints = Array.Empty<KeyPoint>();
    private int _orbFeatureCount, _siftFeatureCount;
    public (KeyPoint[] Keys, Mat Descriptors) GetFeatures(RecognitionMethod method, int count)
    {
        lock (_featureGate)
        {
            if (method == RecognitionMethod.OrbFeatureMatching)
            {
                if (_orbDescriptors == null || _orbFeatureCount != count)
                {
                    _orbDescriptors?.Dispose();
                    using var prepared = FeatureRecognizer.PrepareFeatureImage(Gray);
                    using var detector = FeatureRecognizer.CreateDetector(method, count);
                    _orbDescriptors = new Mat(); detector.DetectAndCompute(prepared, null, out _orbKeypoints, _orbDescriptors);
                    _orbFeatureCount = count;
                }
                return (_orbKeypoints, _orbDescriptors);
            }
            if (_siftDescriptors == null || _siftFeatureCount != count)
            {
                _siftDescriptors?.Dispose();
                using var prepared = FeatureRecognizer.PrepareFeatureImage(Gray);
                using var detector = FeatureRecognizer.CreateDetector(method, count);
                _siftDescriptors = new Mat(); detector.DetectAndCompute(prepared, null, out _siftKeypoints, _siftDescriptors);
                _siftFeatureCount = count;
            }
            return (_siftKeypoints, _siftDescriptors);
        }
    }
    public void Dispose() { Gray.Dispose(); Edge?.Dispose(); AlphaMask?.Dispose(); _orbDescriptors?.Dispose(); _siftDescriptors?.Dispose(); }
}

public sealed class TemplateLibrary : IDisposable
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp" };
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    private readonly ReaderWriterLockSlim _gate = new();
    private readonly Dictionary<TemplateKind, Dictionary<string, List<TemplateEntry>>> _entries = new();
    private readonly List<TemplateEntry> _retiredEntries = new();
    private readonly ConcurrentDictionary<string, object> _labelLocks = new(StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; }
    public TemplateLibrary(string? root = null)
    {
        RootPath = Path.GetFullPath(root ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "templates"));
        Directory.CreateDirectory(GetKindRoot(TemplateKind.Weapon));
        Directory.CreateDirectory(GetKindRoot(TemplateKind.Scope));
        Reload();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<TemplateEntry>> Snapshot(TemplateKind kind)
    {
        _gate.EnterReadLock();
        try
        {
            return _entries.GetValueOrDefault(kind, new(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(x => x.Key, x => (IReadOnlyList<TemplateEntry>)x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        }
        finally { _gate.ExitReadLock(); }
    }

    public void Reload()
    {
        var loaded = new Dictionary<TemplateKind, Dictionary<string, List<TemplateEntry>>>();
        foreach (var kind in Enum.GetValues<TemplateKind>())
        {
            var labels = new Dictionary<string, List<TemplateEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in Directory.EnumerateDirectories(GetKindRoot(kind)))
            {
                string label = Path.GetFileName(directory);
                var list = new List<TemplateEntry>();
                foreach (string file in Directory.EnumerateFiles(directory).Where(x => Extensions.Contains(Path.GetExtension(x))))
                {
                    var entry = LoadEntry(label, file); if (entry != null) list.Add(entry);
                }
                if (list.Count > 0) labels[label] = list;
            }
            loaded[kind] = labels;
        }
        _gate.EnterWriteLock();
        try
        {
            // Snapshots may still be in an active matcher; retire entries until final disposal.
            _retiredEntries.AddRange(_entries.Values.SelectMany(x => x.Values).SelectMany(x => x));
            _entries.Clear(); foreach (var item in loaded) _entries[item.Key] = item.Value;
        }
        finally { _gate.ExitWriteLock(); }
    }

    public string Save(TemplateKind kind, string requestedLabel, Bitmap image)
    {
        string label = ResolveLabel(kind, ValidateLabel(requestedLabel));
        object gate = _labelLocks.GetOrAdd($"{kind}:{label}", _ => new object());
        lock (gate)
        {
            string directory = ResolveInside(GetKindRoot(kind), label);
            Directory.CreateDirectory(directory);
            string stem = label;
            string file = Path.Combine(directory, stem + ".png");
            for (int i = 1; File.Exists(file); i++) file = Path.Combine(directory, $"{stem}_{i:00}.png");
            string temporary = file + ".tmp.png";
            try { image.Save(temporary, System.Drawing.Imaging.ImageFormat.Png); File.Move(temporary, file); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            var entry = LoadEntry(label, file) ?? throw new InvalidDataException("Saved template could not be decoded.");
            _gate.EnterWriteLock();
            try
            {
                if (!_entries.TryGetValue(kind, out var labels)) _entries[kind] = labels = new(StringComparer.OrdinalIgnoreCase);
                if (!labels.TryGetValue(label, out var items)) labels[label] = items = new();
                items.Add(entry);
            }
            finally { _gate.ExitWriteLock(); }
            return file;
        }
    }
    private static TemplateEntry? LoadEntry(string label, string file)
    {
        try
        {
            using var source = Cv2.ImRead(file, ImreadModes.Unchanged);
            if (source.Empty()) throw new InvalidDataException("Empty image");
            var gray = new Mat();
            if (source.Channels() == 4) Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
            else if (source.Channels() == 3) Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            else source.CopyTo(gray);
            var edge = new Mat(); Cv2.Canny(gray, edge, 60, 150);
            Mat? alpha = null;
            if (source.Channels() == 4)
            {
                alpha = new Mat(); Cv2.ExtractChannel(source, alpha, 3);
                if (Cv2.CountNonZero(alpha) == alpha.Rows * alpha.Cols) { alpha.Dispose(); alpha = null; }
            }
            return new TemplateEntry { Label = label, FilePath = file, Gray = gray, Edge = edge, AlphaMask = alpha };
        }
        catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warning, $"Skipped invalid template {file}: {ex.Message}"); return null; }
    }

    public void DeleteImage(TemplateKind kind, string label, string fileName)
    {
        label = ResolveLabel(kind, ValidateLabel(label));
        if (Path.GetFileName(fileName) != fileName || !Extensions.Contains(Path.GetExtension(fileName))) throw new ArgumentException("Invalid template filename.");
        string directory = ResolveInside(GetKindRoot(kind), label);
        string file = ResolveInside(directory, fileName);
        if (File.Exists(file)) File.Delete(file);
        Reload();
    }

    public void DeleteLabel(TemplateKind kind, string label)
    {
        label = ResolveLabel(kind, ValidateLabel(label));
        string root = GetKindRoot(kind); string directory = ResolveInside(root, label);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        Reload();
    }

    public string RenameLabel(TemplateKind kind, string oldLabel, string newLabel)
    {
        oldLabel = ResolveLabel(kind, ValidateLabel(oldLabel)); newLabel = ValidateLabel(newLabel);
        string root = GetKindRoot(kind), source = ResolveInside(root, oldLabel), destination = ResolveInside(root, newLabel);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (Directory.Exists(destination)) throw new IOException("The destination label already exists.");
        Directory.Move(source, destination);
        var files = Directory.EnumerateFiles(destination).Where(x => Extensions.Contains(Path.GetExtension(x))).OrderBy(x => x).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            string target = Path.Combine(destination, i == 0 ? newLabel + Path.GetExtension(files[i]) : $"{newLabel}_{i:00}{Path.GetExtension(files[i])}");
            if (!string.Equals(files[i], target, StringComparison.OrdinalIgnoreCase)) File.Move(files[i], target);
        }
        Reload(); return destination;
    }

    public static string ValidateLabel(string input)
    {
        string value = (input ?? "").Trim();
        if (value.Length == 0 || value.Length > 80 || value is "." or ".." || Path.IsPathRooted(value) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal) || value.EndsWith('.') || Reserved.Contains(value.Split('.')[0]))
            throw new ArgumentException("Invalid template label.", nameof(input));
        return value;
    }

    private string ResolveLabel(TemplateKind kind, string requested)
        => Directory.EnumerateDirectories(GetKindRoot(kind)).Select(Path.GetFileName)
            .FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase)) ?? requested;
    private string GetKindRoot(TemplateKind kind) => ResolveInside(RootPath, kind == TemplateKind.Weapon ? "weapons" : "scopes");
    private static string ResolveInside(string root, string child)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, child));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Path escaped template root.");
        return candidate;
    }
    public void Dispose()
    {
        _gate.EnterWriteLock();
        try { foreach (var item in _entries.Values.SelectMany(x => x.Values).SelectMany(x => x).Concat(_retiredEntries)) item.Dispose(); _entries.Clear(); _retiredEntries.Clear(); }
        finally { _gate.ExitWriteLock(); _gate.Dispose(); }
    }
}
