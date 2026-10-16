using IntercalarioDigitale.Core.Pdf;
using IntercalarioDigitale.Core.Word;
using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class IntroductionTests
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
        File.WriteAllBytes(path, SamplePdfFactory.Create(name, "prova", 1));
        return path;
    }

    private static (BinderRepository Repo, string Binder, string Root) NewBinder()
    {
        var root = CreateTempFolder();
        var repo = new BinderRepository();
        var binder = Path.Combine(root, "binder");
        repo.CreateNew(binder, CreatePdf(root, "cover.pdf"));
        return (repo, binder, root);
    }

    [Fact]
    public void Introduction_IsLabelledZeroAndComesFirstOnTheAttachmentSide()
    {
        var (repo, binder, root) = NewBinder();
        repo.AddAttachment(binder, CreatePdf(root, "a.pdf"));
        repo.SetIntroduction(binder, CreatePdf(root, "intro.pdf"), "Copertina");

        var manifest = repo.LoadManifest(binder);
        var items = BinderLayout.AttachmentSide(manifest);

        Assert.Equal(new[] { "0", "1" }, items.Select(i => i.Label));
        Assert.Equal(BinderLayout.IntroductionDocumentId, items[0].DocumentId);
        Assert.False(items[0].IsAnnex);
        Assert.Equal(new[] { "0" }, BinderLayout.CoverSide(manifest).Select(i => i.Label));
        Assert.NotNull(BinderLayout.Find(manifest, BinderLayout.IntroductionDocumentId));
    }

    [Fact]
    public void Introduction_CannotBeSetTwiceAndAcceptsWord()
    {
        var (repo, binder, root) = NewBinder();
        var word = Path.Combine(root, "intro.docx");
        File.WriteAllBytes(word, SampleWordFactory.Create("Copertina", "Intro", "Testo"));

        var document = repo.SetIntroduction(binder, word);

        Assert.Equal("intro.docx", document.RelativePdfPath);
        Assert.True(File.Exists(Path.Combine(binder, "intro.docx")));
        Assert.Throws<InvalidOperationException>(() => repo.SetIntroduction(binder, CreatePdf(root, "altra.pdf")));
        Assert.True(BinderLayout.AttachmentSide(repo.LoadManifest(binder))[0].IsWord);
    }

    [Fact]
    public void Introduction_KeepsTheOriginalWhenReplaced()
    {
        var (repo, binder, root) = NewBinder();
        repo.SetIntroduction(binder, CreatePdf(root, "intro.pdf"));
        var first = File.ReadAllBytes(Path.Combine(binder, "intro.pdf"));

        var word = Path.Combine(root, "nuova.docx");
        File.WriteAllBytes(word, SampleWordFactory.Create("Copertina", "Nuova", "Testo"));
        var result = repo.ReplaceDocument(binder, BinderLayout.IntroductionDocumentId, word, "L. Rossi");

        Assert.True(result.OriginalPreserved);
        var item = BinderLayout.Find(repo.LoadManifest(binder), BinderLayout.IntroductionDocumentId)!;
        Assert.Equal("intro.docx", item.RelativePdfPath);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(binder, item.OriginalRelativePath!)));
        Assert.False(File.Exists(Path.Combine(binder, "intro.pdf")));
    }

    [Fact]
    public void Introduction_CanBeAnnotated()
    {
        var (repo, binder, root) = NewBinder();
        repo.SetIntroduction(binder, CreatePdf(root, "intro.pdf"));

        repo.SaveAnnotations(binder, BinderLayout.IntroductionDocumentId, [new PageAnnotation { PageIndex = 0, Strokes = [new InkStroke()] }]);

        Assert.Single(repo.LoadAnnotations(binder, BinderLayout.IntroductionDocumentId));
    }

    [Fact]
    public void OldManifestsHaveNoIntroduction()
    {
        var (repo, binder, root) = NewBinder();
        repo.AddAttachment(binder, CreatePdf(root, "a.pdf"));

        var manifest = repo.LoadManifest(binder);

        Assert.Null(manifest.Introduction);
        Assert.Equal(new[] { "1" }, BinderLayout.AttachmentSide(manifest).Select(i => i.Label));
    }

    [Fact]
    public void EveryNewPracticeGetsAnIntroduction()
    {
        var library = new PracticeLibrary(CreateTempFolder());

        foreach (var practice in library.State.Practices)
        {
            var manifest = library.Binders.LoadManifest(library.GetBinderPath(practice.Id));
            Assert.NotNull(manifest.Introduction);
            Assert.True(File.Exists(Path.Combine(library.GetBinderPath(practice.Id), manifest.Introduction!.RelativePdfPath)));
        }

        var created = library.CreatePractice("Nuova", "verdi", year: 2026);
        Assert.NotNull(library.Binders.LoadManifest(library.GetBinderPath(created.Id)).Introduction);
    }

    [Fact]
    public void NewPractice_UsesTheGivenIntroductionFile()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var word = Path.Combine(library.RootFolder, "mia-copertina.docx");
        File.WriteAllBytes(word, SampleWordFactory.Create("Copertina", "Mia", "Testo"));

        var practice = library.CreatePractice("Nuova", "verdi", year: 2026, introductionPath: word);

        var intro = library.Binders.LoadManifest(library.GetBinderPath(practice.Id)).Introduction!;
        Assert.Equal("intro.docx", intro.RelativePdfPath);
    }

    [Fact]
    public void DemoPractice2_HasAWordIntroduction()
    {
        var library = new PracticeLibrary(CreateTempFolder());

        var intro = BinderLayout.AttachmentSide(library.Binders.LoadManifest(library.GetBinderPath("2.026_2")))[0];

        Assert.Equal("0", intro.Label);
        Assert.True(intro.IsWord);
    }

    [Fact]
    public void AddIntroduction_FillsAnOldBinderOnce()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var binder = library.GetBinderPath("2.026_1");
        var manifest = library.Binders.LoadManifest(binder);
        File.Delete(Path.Combine(binder, manifest.Introduction!.RelativePdfPath));
        manifest.Introduction = null;
        File.WriteAllText(Path.Combine(binder, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));

        var added = library.AddIntroduction("2.026_1");

        Assert.Equal(BinderLayout.IntroductionDocumentId, added.Id);
        Assert.Throws<InvalidOperationException>(() => library.AddIntroduction("2.026_1"));
    }
}
