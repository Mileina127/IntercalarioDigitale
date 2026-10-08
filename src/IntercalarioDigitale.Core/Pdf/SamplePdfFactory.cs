using System.Text;

namespace IntercalarioDigitale.Core.Pdf;

/// <summary>
/// Crea PDF minimali (A4, testo e qualche riquadro) per provare l'applicazione senza documenti reali.
/// Nessuna dipendenza esterna: il file viene scritto a mano nel formato PDF 1.4.
/// </summary>
public static class SamplePdfFactory
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;

    private static readonly string[] BodyLines =
    [
        "Il presente documento e' un esempio generato dall'applicativo",
        "per verificare la lettura, la navigazione tra le pagine",
        "e le annotazioni a penna sul layer separato.",
        "",
        "1. Oggetto",
        "   Esame e concordanza dell'appunto da parte della linea gerarchica.",
        "2. Riferimenti",
        "   Allegati tecnici e relativa documentazione di supporto.",
        "3. Proposta",
        "   Si propone di procedere secondo quanto indicato nelle pagine seguenti.",
        "",
        "Scrivi sulla pagina con il pulsante Annotazioni: i tratti restano",
        "salvati a parte e non modificano il PDF originale."
    ];

    public static byte[] Create(string title, string subtitle, int pageCount)
    {
        if (pageCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), "Almeno una pagina.");
        }

        // Oggetti: 1 catalogo, 2 elenco pagine, 3 font, poi per ogni pagina (pagina, contenuto).
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty, // riempito dopo, serve l'elenco dei figli
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"
        };

        var kids = new StringBuilder();
        for (var page = 0; page < pageCount; page++)
        {
            var pageObjectNumber = 4 + page * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            kids.Append(pageObjectNumber).Append(" 0 R ");

            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumber} 0 R >>");

            var content = BuildPageContent(title, subtitle, page + 1, pageCount);
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [ {kids}] /Count {pageCount} >>";

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");

        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefOffset = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        // Tutti i caratteri sono stati ridotti a Latin-1: un carattere = un byte, quindi gli offset sono esatti.
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static string BuildPageContent(string title, string subtitle, int pageNumber, int pageCount)
    {
        var sb = new StringBuilder();

        // Intestazione e filetto
        sb.Append("BT /F1 10 Tf 50 800 Td (").Append(Escape(subtitle)).Append(") Tj ET\n");
        sb.Append("0.6 g 50 792 495 0.8 re f 0 g\n");

        // Titolo
        sb.Append("BT /F1 22 Tf 50 750 Td (").Append(Escape(title)).Append(") Tj ET\n");
        sb.Append("BT /F1 11 Tf 50 728 Td (Pagina ").Append(pageNumber).Append(" di ").Append(pageCount).Append(") Tj ET\n");

        // Testo
        var y = 690;
        foreach (var line in BodyLines)
        {
            if (line.Length > 0)
            {
                sb.Append("BT /F1 12 Tf 50 ").Append(y).Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
            }

            y -= 20;
        }

        // Un riquadro con una croce, per avere qualcosa da annotare
        sb.Append("0.4 G 1 w 50 ").Append(y - 190).Append(" 495 160 re S\n");
        sb.Append("50 ").Append(y - 190).Append(" m 545 ").Append(y - 30).Append(" l S\n");
        sb.Append("50 ").Append(y - 30).Append(" m 545 ").Append(y - 190).Append(" l S 0 G\n");

        // Piè di pagina
        sb.Append("BT /F1 9 Tf 270 30 Td (").Append(pageNumber).Append(") Tj ET\n");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '(' || ch == ')' || ch == '\\')
            {
                sb.Append('\\').Append(ch);
            }
            else if (ch > 255 || ch < 32)
            {
                sb.Append('?');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}
