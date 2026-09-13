using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using System.IO;

namespace Aimmy2.AILogic.Recognition;

internal static class ImagePreprocessor
{
    public static Mat ToMat(Bitmap bitmap)
    {
        using var converted = bitmap.PixelFormat == PixelFormat.Format32bppArgb
            ? null : new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        Bitmap source = converted ?? bitmap;
        if (converted != null)
            using (var g = Graphics.FromImage(converted)) g.DrawImageUnscaled(bitmap, 0, 0);
        var data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var wrapped = Mat.FromPixelData(source.Height, source.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally { source.UnlockBits(data); }
    }

    public static Mat Gray(Bitmap bitmap, bool edge, bool whiteText)
    {
        using var bgra = ToMat(bitmap);
        var result = new Mat();
        Cv2.CvtColor(bgra, result, ColorConversionCodes.BGRA2GRAY);
        if (whiteText)
        {
            using var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            using var mask = new Mat();
            Cv2.InRange(bgr, new Scalar(150, 150, 150), new Scalar(255, 255, 255), mask);
            Cv2.BitwiseAnd(result, mask, result);
            Cv2.Threshold(result, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }
        if (edge) Cv2.Canny(result, result, 60, 150);
        return result;
    }

    public static Bitmap PrepareWeaponForOcr(Bitmap bitmap, int mode = 1)
    {
        using var source = ToMat(bitmap);
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
        Cv2.Resize(gray, gray, new OpenCvSharp.Size(Math.Max(1, gray.Width * 6), Math.Max(1, gray.Height * 6)), 0, 0, InterpolationFlags.Cubic);
        Cv2.Normalize(gray, gray, 0, 255, NormTypes.MinMax);
        if (mode == 1)
        {
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(3, 3), 0);
            Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            // Windows OCR is more reliable with dark glyphs on a light background.
            if (Cv2.Mean(gray).Val0 < 127) Cv2.BitwiseNot(gray, gray);
        }
        else if (mode == 2)
        {
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(3, 3), 0);
            Cv2.AdaptiveThreshold(gray, gray, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 7);
            if (Cv2.Mean(gray).Val0 < 127) Cv2.BitwiseNot(gray, gray);
        }
        Cv2.CopyMakeBorder(gray, gray, 28, 28, 28, 28, BorderTypes.Constant, Scalar.White);
        Cv2.ImEncode(".png", gray, out var bytes);
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }
}
