using System.IO.Compression;
using System.Security;
using System.Text;

namespace IntercalarioDigitale.Core.Word;

/// <summary>
/// Crea documenti Word (.docx) minimali per provare l'applicazione senza file reali.
/// Un .docx è un archivio ZIP: qui basta scrivere le tre parti indispensabili, senza librerie esterne.
/// </summary>
public static class SampleWordFactory
{
    public static byte[] Create(string title, string subtitle, params string[] paragraphs)
    {
        var body = new StringBuilder();
        body.Append(Paragraph(title, bold: true, halfPoints: 40, spaceAfter: 120));
        body.Append(Paragraph(subtitle, bold: false, halfPoints: 24, spaceAfter: 360));
        foreach (var text in paragraphs)
        {
            body.Append(Paragraph(text, bold: false, halfPoints: 22, spaceAfter: 160));
        }

        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            body +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>" +
            "</w:body></w:document>";

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";

        const string rootRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", contentTypes);
            AddEntry(zip, "_rels/.rels", rootRelationships);
            AddEntry(zip, "word/document.xml", document);
        }

        return stream.ToArray();
    }

    private static string Paragraph(string text, bool bold, int halfPoints, int spaceAfter) =>
        $"<w:p><w:pPr><w:spacing w:after=\"{spaceAfter}\"/></w:pPr><w:r><w:rPr>{(bold ? "<w:b/>" : string.Empty)}<w:sz w:val=\"{halfPoints}\"/></w:rPr>" +
        $"<w:t xml:space=\"preserve\">{SecurityElement.Escape(text)}</w:t></w:r></w:p>";

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
