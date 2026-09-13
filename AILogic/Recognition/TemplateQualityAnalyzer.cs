using System.Drawing;
using OpenCvSharp;

namespace Aimmy2.AILogic.Recognition;

public sealed record TemplateQuality(bool CanSave, bool HasWarning, string Message);
public static class TemplateQualityAnalyzer
{
    public static TemplateQuality Analyze(Bitmap image, TemplateKind kind)
    {
        if (image.Width < 2 || image.Height < 2) return new(false, true, "ROI không hợp lệ");
        using var gray = ImagePreprocessor.Gray(image, false, kind == TemplateKind.Weapon);
        Cv2.MeanStdDev(gray, out _, out var deviation);
        if (deviation.Val0 < 1) return new(false, true, "Ảnh rỗng hoặc gần như một màu");
        using var laplace = new Mat(); Cv2.Laplacian(gray, laplace, MatType.CV_64F);
        Cv2.MeanStdDev(laplace, out _, out var sharpness);
        double whiteRatio = Cv2.CountNonZero(gray) / (double)(gray.Rows * gray.Cols);
        if (sharpness.Val0 < 8) return new(true, true, "Ảnh hơi mờ; bạn vẫn có thể lưu");
        if (kind == TemplateKind.Weapon && (whiteRatio < .005 || whiteRatio > .8)) return new(true, true, "Tỷ lệ pixel chữ trắng bất thường");
        if (kind == TemplateKind.Scope)
        {
            using var edge = new Mat(); Cv2.Canny(gray, edge, 60, 150);
            if (Cv2.CountNonZero(edge) < 8) return new(true, true, "Scope có quá ít cạnh/chi tiết");
        }
        return new(true, false, "Tốt");
    }
}
