using System.Drawing;

namespace Aimmy2.AILogic;

/// <summary>
/// Canonical detection used after model-output decoding. Rectangle coordinates are
/// capture-relative physical pixels; screen coordinates include the capture origin.
/// </summary>
public sealed class Prediction
{
    public RectangleF Rectangle { get; set; }
    public RectangleF CaptureRectangle => Rectangle;
    public RectangleF ModelRectangle { get; set; }
    public float Confidence { get; set; }
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "Enemy";
    public float CenterXTranslated { get; set; }
    public float CenterYTranslated { get; set; }
    public float ScreenCenterX { get; set; }
    public float ScreenCenterY { get; set; }
    public long FrameId { get; set; }
    public long FrameTimestamp { get; set; }
    public bool IsSynthetic { get; set; }

    public float CenterX => Rectangle.X + Rectangle.Width * 0.5f;
    public float CenterY => Rectangle.Y + Rectangle.Height * 0.5f;
    public float Area => Math.Max(0, Rectangle.Width) * Math.Max(0, Rectangle.Height);

    internal Prediction CopyForMiss(float velocityX, float velocityY, int missedFrames)
    {
        return new Prediction
        {
            // Preserve the legacy non-WGC movement behaviour: aiming is still based
            // on the last observed box while screen-space metadata carries velocity.
            Rectangle = Rectangle,
            ModelRectangle = ModelRectangle,
            Confidence = Confidence * Math.Max(0, 1f - missedFrames * 0.2f),
            ClassId = ClassId,
            ClassName = ClassName,
            CenterXTranslated = CenterXTranslated,
            CenterYTranslated = CenterYTranslated,
            ScreenCenterX = ScreenCenterX + velocityX * missedFrames,
            ScreenCenterY = ScreenCenterY + velocityY * missedFrames,
            FrameId = FrameId,
            FrameTimestamp = FrameTimestamp,
            IsSynthetic = true
        };
    }
}
