namespace IntercalarioDigitale.Core;

/// <summary>Formati di documento che l'intercalario accetta: PDF e Word (questi ultimi si leggono in sola lettura).</summary>
public static class DocumentFormats
{
    private static readonly string[] WordExtensions = [".doc", ".docx"];

    /// <summary>Filtro per le finestre «Apri file» di Windows.</summary>
    public const string OpenFileFilter =
        "Documenti PDF e Word (*.pdf;*.docx;*.doc)|*.pdf;*.docx;*.doc|PDF (*.pdf)|*.pdf|Word (*.docx;*.doc)|*.docx;*.doc";

    public static string ExtensionOf(string path) => Path.GetExtension(path).ToLowerInvariant();

    public static bool IsPdf(string path) => ExtensionOf(path) == ".pdf";

    public static bool IsWord(string path) => WordExtensions.Contains(ExtensionOf(path));

    public static bool IsSupported(string path) => IsPdf(path) || IsWord(path);
}
