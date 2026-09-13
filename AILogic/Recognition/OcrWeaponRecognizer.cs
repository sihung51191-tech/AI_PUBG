using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Windows.Globalization;
using System.IO;

namespace Aimmy2.AILogic.Recognition;

public sealed class OcrWeaponRecognizer : IRegionRecognizer
{
    private readonly TemplateLibrary _library;
    private readonly RecognitionSettings _settings;
    private static readonly Lazy<OcrEngine?> SharedEngine = new(CreateEngine);
    private OcrEngine? Engine => SharedEngine.Value;
    public RecognitionMethod Method => RecognitionMethod.Ocr;
    public OcrWeaponRecognizer(TemplateLibrary library, RecognitionSettings settings) => (_library, _settings) = (library, settings);
    public async Task<RecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (Engine == null) return RecognitionResult.Unknown(Method, "Windows OCR language is unavailable");
        RecognitionResult best = RecognitionResult.Unknown(Method, "OCR found no weapon name");
        var labels = _library.Snapshot(TemplateKind.Weapon).Keys;
        foreach (int preparationMode in new[] { 1, 2, 0 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var prepared = ImagePreprocessor.PrepareWeaponForOcr(image, preparationMode);
            string text = await ReadTextAsync(prepared, cancellationToken);
            var match = FuzzyNameMatcher.Match(text, labels);
            bool reliable = match.Label != "Unknown" && match.Score >= _settings.OcrConfidenceThreshold;
            var candidate = new RecognitionResult(match.Label, Method, match.Score, reliable,
                reliable ? $"OCR: {text}" : $"OCR below threshold: {text}", timer.Elapsed.TotalMilliseconds);
            if (candidate.Score > best.Score) best = candidate;
            if (candidate.IsReliable) break;
        }
        timer.Stop();
        return best with { ProcessingTimeMs = timer.Elapsed.TotalMilliseconds };
    }

    private async Task<string> ReadTextAsync(Bitmap prepared, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(); prepared.Save(memory, ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(memory.ToArray()); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream(); }
        cancellationToken.ThrowIfCancellationRequested(); stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        var result = await Engine!.RecognizeAsync(software);
        return result.Text;
    }
    private static OcrEngine? CreateEngine()
    {
        try
        {
            var english = new Language("en-US");
            if (OcrEngine.IsLanguageSupported(english)) return OcrEngine.TryCreateFromLanguage(english);
        }
        catch { }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }
    public void Dispose() { }
}
