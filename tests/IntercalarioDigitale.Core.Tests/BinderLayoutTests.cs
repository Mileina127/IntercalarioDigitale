using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Pdf;
using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class BinderLayoutTests
{
    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gerarchia-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreatePdf(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, SamplePdfFactory.Create(name, "test", 1));
        return path;
    }

    [Fact]
    public void CoverSide_StartsWithTheCoverAsZero()
    {
        var manifest = new BinderManifest();
        manifest.CoverAttachments.Add(new AttachmentDocument { Id = "c1", Title = "Convocazione" });
        manifest.CoverAttachments.Add(new AttachmentDocument { Id = "c2", Title = "Ordine del giorno" });

        var items = BinderLayout.CoverSide(manifest);

        Assert.Equal(new[] { "0", "1", "2" }, items.Select(i => i.Label));
        Assert.Equal(BinderLayout.CoverDocumentId, items[0].DocumentId);
        Assert.All(items, i => Assert.False(i.IsAnnex));
    }

    [Fact]
    public void AttachmentSide_PutsAnnexesRightAfterTheirAttachment()
    {
        var manifest = new BinderManifest();
        manifest.Attachments.Add(new AttachmentDocument { Id = "a1", Title = "Primo" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "a2", Title = "Secondo" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "x1", Title = "Annesso al primo", ParentId = "a1" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "a3", Title = "Terzo" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "x2", Title = "Altro annesso al primo", ParentId = "a1" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "x3", Title = "Annesso al secondo", ParentId = "a2" });

        var items = BinderLayout.AttachmentSide(manifest);

        Assert.Equal(new[] { "1", "L1.1", "L1.2", "2", "L2.1", "3" }, items.Select(i => i.Label));
        Assert.Equal(new[] { "a1", "x1", "x2", "a2", "x3", "a3" }, items.Select(i => i.DocumentId));
        Assert.Equal(new[] { false, true, true, false, true, false }, items.Select(i => i.IsAnnex));
    }

    [Fact]
    public void AttachmentSide_TreatsAnAnnexWithoutParentAsAnAttachment()
    {
        var manifest = new BinderManifest();
        manifest.Attachments.Add(new AttachmentDocument { Id = "a1", Title = "Primo" });
        manifest.Attachments.Add(new AttachmentDocument { Id = "x1", Title = "Orfano", ParentId = "non-esiste" });

        var items = BinderLayout.AttachmentSide(manifest);

        Assert.Equal(new[] { "1", "2" }, items.Select(i => i.Label));
    }

    [Fact]
    public void Repository_AddsAnnexesAndCoverAttachments()
    {
        var root = CreateTempFolder();
        var repo = new BinderRepository();
        var binder = Path.Combine(root, "binder");
        repo.CreateNew(binder, CreatePdf(root, "cover.pdf"));

        var attachment = repo.AddAttachment(binder, CreatePdf(root, "a.pdf"), "Allegato");
        var annex = repo.AddAttachment(binder, CreatePdf(root, "x.pdf"), "Annesso", attachment.Id);
        var coverExtra = repo.AddCoverAttachment(binder, CreatePdf(root, "c.pdf"), "Convocazione");

        var manifest = repo.LoadManifest(binder);
        Assert.Equal(2, manifest.Attachments.Count);
        Assert.Equal(attachment.Id, manifest.Attachments.Single(a => a.Id == annex.Id).ParentId);
        Assert.Null(manifest.Attachments.Single(a => a.Id == attachment.Id).ParentId);
        Assert.Equal(coverExtra.Id, Assert.Single(manifest.CoverAttachments).Id);
        Assert.True(File.Exists(Path.Combine(binder, coverExtra.RelativePdfPath)));
    }

    [Fact]
    public void Repository_RejectsAnAnnexOfAnAnnexOrOfAMissingAttachment()
    {
        var root = CreateTempFolder();
        var repo = new BinderRepository();
        var binder = Path.Combine(root, "binder");
        repo.CreateNew(binder, CreatePdf(root, "cover.pdf"));
        var attachment = repo.AddAttachment(binder, CreatePdf(root, "a.pdf"));
        var annex = repo.AddAttachment(binder, CreatePdf(root, "x.pdf"), null, attachment.Id);

        Assert.Throws<ArgumentException>(() => repo.AddAttachment(binder, CreatePdf(root, "y.pdf"), null, annex.Id));
        Assert.Throws<ArgumentException>(() => repo.AddAttachment(binder, CreatePdf(root, "z.pdf"), null, "non-esiste"));
    }

    [Fact]
    public void OldManifestsWithoutNewFieldsStillLoad()
    {
        var root = CreateTempFolder();
        var binder = Path.Combine(root, "binder");
        Directory.CreateDirectory(binder);
        File.WriteAllText(
            Path.Combine(binder, "manifest.json"),
            """{ "version": "1.0", "cover": { "title": "Vecchia", "relativePdfPath": "cover.pdf" }, "attachments": [ { "id": "a1", "title": "Uno", "relativePdfPath": "attachments/a1.pdf" } ] }""");

        var manifest = new BinderRepository().LoadManifest(binder);

        Assert.Empty(manifest.CoverAttachments);
        Assert.Equal(new[] { "0" }, BinderLayout.CoverSide(manifest).Select(i => i.Label));
        Assert.Equal(new[] { "1" }, BinderLayout.AttachmentSide(manifest).Select(i => i.Label));
    }

    [Fact]
    public void DemoPractice3_ShowsCoverAttachmentsAndAnnexes()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var manifest = library.Binders.LoadManifest(library.GetBinderPath("2.026_3"));

        Assert.Equal(new[] { "0", "1", "2" }, BinderLayout.CoverSide(manifest).Select(i => i.Label));
        Assert.Equal(new[] { "1", "L1.1", "L1.2", "2", "3" }, BinderLayout.AttachmentSide(manifest).Select(i => i.Label));
        Assert.All(
            BinderLayout.CoverSide(manifest).Concat(BinderLayout.AttachmentSide(manifest)),
            i => Assert.True(File.Exists(Path.Combine(library.GetBinderPath("2.026_3"), i.RelativePdfPath))));
    }
}
