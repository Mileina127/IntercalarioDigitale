namespace IntercalarioDigitale.Core;

/// <summary>Un documento mostrato nella colonna laterale di un riquadro del viewer.</summary>
public sealed record BinderItem(
    string DocumentId,
    string Label,
    string Title,
    string RelativePdfPath,
    bool IsAnnex,
    int Revision = 0,
    DateTimeOffset? ModifiedAtUtc = null,
    string? ModifiedBy = null,
    string? OriginalRelativePath = null)
{
    /// <summary>Documento Word: si legge in sola lettura, si modifica scaricando una copia e sostituendola.</summary>
    public bool IsWord => DocumentFormats.IsWord(RelativePdfPath);

    public bool HasOriginal => OriginalRelativePath is not null;
}

/// <summary>
/// Calcola quali documenti compaiono sui due lati del viewer e con quale etichetta:
/// contropagina 0, poi 1…n; allegati 0 (la copertina d'introduzione, che si vede da sola) e 1…n,
/// ciascuno seguito dai suoi annessi (L1.1, L1.2…).
/// </summary>
public static class BinderLayout
{
    public const string CoverDocumentId = "cover";

    /// <summary>Id della copertina d'introduzione (allegato 0).</summary>
    public const string IntroductionDocumentId = "intro";

    public static IReadOnlyList<BinderItem> CoverSide(BinderManifest manifest)
    {
        var items = new List<BinderItem>
        {
            Make(CoverDocumentId, "0", manifest.Cover.Title, manifest.Cover, false)
        };

        for (var i = 0; i < manifest.CoverAttachments.Count; i++)
        {
            var document = manifest.CoverAttachments[i];
            items.Add(Make(document.Id, (i + 1).ToString(), document.Title, document, false));
        }

        return items;
    }

    public static IReadOnlyList<BinderItem> AttachmentSide(BinderManifest manifest)
    {
        var all = manifest.Attachments;
        var ids = all.Select(a => a.Id).ToHashSet();
        bool IsTopLevel(AttachmentDocument a) => string.IsNullOrEmpty(a.ParentId) || !ids.Contains(a.ParentId);

        var items = new List<BinderItem>();
        if (manifest.Introduction is { } introduction)
        {
            items.Add(Make(IntroductionDocumentId, "0", introduction.Title, introduction, false));
        }

        var number = 0;
        foreach (var attachment in all.Where(IsTopLevel))
        {
            number++;
            items.Add(Make(attachment.Id, number.ToString(), attachment.Title, attachment, false));

            var annex = 0;
            foreach (var child in all.Where(a => a.ParentId == attachment.Id))
            {
                annex++;
                items.Add(Make(child.Id, $"L{number}.{annex}", child.Title, child, true));
            }
        }

        return items;
    }

    /// <summary>Cerca un documento su entrambi i lati, per Id.</summary>
    public static BinderItem? Find(BinderManifest manifest, string documentId) =>
        CoverSide(manifest).Concat(AttachmentSide(manifest)).FirstOrDefault(i => i.DocumentId == documentId);

    private static BinderItem Make(string id, string label, string title, IRevisableDocument document, bool isAnnex) =>
        new(id, label, title, document.RelativePdfPath, isAnnex, document.Revision, document.ModifiedAtUtc, document.ModifiedBy, document.OriginalRelativePath);
}
