namespace IntercalarioDigitale.Core;

public sealed class BinderManifest
{
    public string Version { get; init; } = "1.0";
    public CoverDocument Cover { get; set; } = new();
    public List<AttachmentDocument> Attachments { get; set; } = [];
}

public sealed class CoverDocument
{
    public string Title { get; set; } = "Copertina";
    public string RelativePdfPath { get; set; } = "cover.pdf";
}

public sealed class AttachmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string RelativePdfPath { get; set; } = string.Empty;
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InkStrokePoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Pressure { get; set; }
}

public sealed class InkStroke
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ColorHex { get; set; } = "#1E88E5";
    public float Thickness { get; set; } = 2.0f;
    public List<InkStrokePoint> Points { get; set; } = [];
}

public sealed class PageAnnotation
{
    public int PageIndex { get; set; }
    public List<InkStroke> Strokes { get; set; } = [];
}
