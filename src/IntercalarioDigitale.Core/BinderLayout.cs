namespace IntercalarioDigitale.Core;

/// <summary>Un documento mostrato nella colonna laterale di un riquadro del viewer.</summary>
public sealed record BinderItem(string DocumentId, string Label, string Title, string RelativePdfPath, bool IsAnnex);

/// <summary>
/// Calcola quali documenti compaiono sui due lati del viewer e con quale etichetta:
/// contropagina 0, poi 1…n; allegati 1…n, ciascuno seguito dai suoi annessi (L1.1, L1.2…).
/// </summary>
public static class BinderLayout
{
    public const string CoverDocumentId = "cover";

    public static IReadOnlyList<BinderItem> CoverSide(BinderManifest manifest)
    {
        var items = new List<BinderItem>
        {
            new(CoverDocumentId, "0", manifest.Cover.Title, manifest.Cover.RelativePdfPath, false)
        };

        for (var i = 0; i < manifest.CoverAttachments.Count; i++)
        {
            var document = manifest.CoverAttachments[i];
            items.Add(new BinderItem(document.Id, (i + 1).ToString(), document.Title, document.RelativePdfPath, false));
        }

        return items;
    }

    public static IReadOnlyList<BinderItem> AttachmentSide(BinderManifest manifest)
    {
        var all = manifest.Attachments;
        var ids = all.Select(a => a.Id).ToHashSet();
        bool IsTopLevel(AttachmentDocument a) => string.IsNullOrEmpty(a.ParentId) || !ids.Contains(a.ParentId);

        var items = new List<BinderItem>();
        var number = 0;
        foreach (var attachment in all.Where(IsTopLevel))
        {
            number++;
            items.Add(new BinderItem(attachment.Id, number.ToString(), attachment.Title, attachment.RelativePdfPath, false));

            var annex = 0;
            foreach (var child in all.Where(a => a.ParentId == attachment.Id))
            {
                annex++;
                items.Add(new BinderItem(child.Id, $"L{number}.{annex}", child.Title, child.RelativePdfPath, true));
            }
        }

        return items;
    }
}
