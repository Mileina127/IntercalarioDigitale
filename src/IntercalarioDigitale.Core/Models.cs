namespace IntercalarioDigitale.Core;

public sealed class BinderManifest
{
    public string Version { get; init; } = "1.0";
    public CoverDocument Cover { get; set; } = new();

    /// <summary>
    /// Copertina d'introduzione della pratica, trattata come «allegato 0»: si vede da sola al centro,
    /// senza contropagina. Manca nei vecchi intercalari.
    /// </summary>
    public AttachmentDocument? Introduction { get; set; }

    /// <summary>Documenti allegati alla contropagina (etichette 1…n; la contropagina stessa è la 0).</summary>
    public List<AttachmentDocument> CoverAttachments { get; set; } = [];

    /// <summary>Allegati (1…n) e loro annessi (L1.1, L1.2…): un annesso ha <see cref="AttachmentDocument.ParentId"/> valorizzato.</summary>
    public List<AttachmentDocument> Attachments { get; set; } = [];
}

/// <summary>Un documento che può essere sostituito: si tiene la versione attuale e, a parte, la prima (l'originale).</summary>
public interface IRevisableDocument
{
    /// <summary>File attuale (PDF o Word). Il nome della proprietà resta «RelativePdfPath» per compatibilità con i manifest già salvati.</summary>
    string RelativePdfPath { get; set; }

    /// <summary>0 finché il documento non è mai stato sostituito; poi cresce a ogni sostituzione.</summary>
    int Revision { get; set; }

    DateTimeOffset? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }

    /// <summary>File com'era alla partenza, conservato alla prima sostituzione e mai più toccato.</summary>
    string? OriginalRelativePath { get; set; }
}

public sealed class CoverDocument : IRevisableDocument
{
    public string Title { get; set; } = "Copertina";
    public string RelativePdfPath { get; set; } = "cover.pdf";
    public int Revision { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public string? OriginalRelativePath { get; set; }
}

public sealed class AttachmentDocument : IRevisableDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string RelativePdfPath { get; set; } = string.Empty;
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Se valorizzato, il documento è un annesso dell'allegato con questo Id.</summary>
    public string? ParentId { get; set; }

    public int Revision { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public string? OriginalRelativePath { get; set; }
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
