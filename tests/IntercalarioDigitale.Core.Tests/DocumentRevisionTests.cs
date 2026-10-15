using System.IO.Compression;
using System.Xml.Linq;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Pdf;
using IntercalarioDigitale.Core.Word;
using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class DocumentRevisionTests
{
    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gerarchia-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WritePdf(string folder, string name, string title)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, SamplePdfFactory.Create(title, "test", 1));
        return path;
    }

    private static string WriteWord(string folder, string name, string subtitle)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, SampleWordFactory.Create("Titolo", subtitle, "Un paragrafo."));
        return path;
    }

    private static (BinderRepository Repo, string Binder, string Root) NewBinder()
    {
        var root = CreateTempFolder();
        var repo = new BinderRepository();
        var binder = Path.Combine(root, "binder");
        repo.CreateNew(binder, WritePdf(root, "cover.pdf", "Cover"));
        return (repo, binder, root);
    }

    [Theory]
    [InlineData("a.pdf", true, false)]
    [InlineData("A.PDF", true, false)]
    [InlineData("a.docx", false, true)]
    [InlineData("a.doc", false, true)]
    [InlineData("a.docm", false, false)]
    [InlineData("a.txt", false, false)]
    public void DocumentFormats_RecognisesPdfAndWord(string name, bool isPdf, bool isWord)
    {
        Assert.Equal(isPdf, DocumentFormats.IsPdf(name));
        Assert.Equal(isWord, DocumentFormats.IsWord(name));
        Assert.Equal(isPdf || isWord, DocumentFormats.IsSupported(name));
    }

    [Fact]
    public void SampleWord_IsAZipWithAWellFormedMainPart()
    {
        var bytes = SampleWordFactory.Create("Titolo & <prova>", "Sottotitolo", "Paragrafo uno", "Paragrafo due");

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
        Assert.Contains(zip.Entries, e => e.FullName == "_rels/.rels");

        var main = zip.GetEntry("word/document.xml");
        Assert.NotNull(main);
        using var reader = new StreamReader(main!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        Assert.Contains("Titolo & <prova>", xml.Root!.Value);
        Assert.Contains("Paragrafo due", xml.Root.Value);
    }

    [Fact]
    public void Repository_AcceptsWordFilesAndRejectsOtherFormats()
    {
        var (repo, binder, root) = NewBinder();

        var added = repo.AddAttachment(binder, WriteWord(root, "bozza.docx", "v1"), "Bozza");

        Assert.EndsWith(".docx", added.RelativePdfPath);
        Assert.True(File.Exists(Path.Combine(binder, added.RelativePdfPath)));
        var item = BinderLayout.Find(repo.LoadManifest(binder), added.Id);
        Assert.NotNull(item);
        Assert.True(item!.IsWord);

        var text = Path.Combine(root, "note.txt");
        File.WriteAllText(text, "ciao");
        Assert.Throws<ArgumentException>(() => repo.AddAttachment(binder, text));
    }

    [Fact]
    public void Replace_KeepsTheFirstVersionAsOriginal()
    {
        var (repo, binder, root) = NewBinder();
        var added = repo.AddAttachment(binder, WriteWord(root, "v1.docx", "versione 1"), "Bozza");
        var firstBytes = File.ReadAllBytes(Path.Combine(binder, added.RelativePdfPath));
        var firstRelativePath = added.RelativePdfPath;

        var result = repo.ReplaceDocument(binder, added.Id, WriteWord(root, "v2.docx", "versione 2"), "G. Verdi");

        Assert.True(result.OriginalPreserved);
        Assert.Equal(1, result.Revision);
        var manifest = repo.LoadManifest(binder);
        var document = manifest.Attachments.Single();
        Assert.Equal(1, document.Revision);
        Assert.Equal("G. Verdi", document.ModifiedBy);
        Assert.NotNull(document.ModifiedAtUtc);
        Assert.NotNull(document.OriginalRelativePath);
        Assert.Equal(firstBytes, File.ReadAllBytes(Path.Combine(binder, document.OriginalRelativePath!)));
        Assert.NotEqual(firstBytes, File.ReadAllBytes(Path.Combine(binder, document.RelativePdfPath)));
        Assert.False(File.Exists(Path.Combine(binder, firstRelativePath)));
    }

    [Fact]
    public void Replace_DiscardsIntermediateVersionsAndNeverTouchesTheOriginal()
    {
        var (repo, binder, root) = NewBinder();
        var added = repo.AddAttachment(binder, WriteWord(root, "v1.docx", "versione 1"), "Bozza");
        var firstBytes = File.ReadAllBytes(Path.Combine(binder, added.RelativePdfPath));

        repo.ReplaceDocument(binder, added.Id, WriteWord(root, "v2.docx", "versione 2"), "G. Verdi");
        var afterFirst = repo.LoadManifest(binder).Attachments.Single();
        var intermediate = Path.Combine(binder, afterFirst.RelativePdfPath);
        var originalPath = afterFirst.OriginalRelativePath;

        var second = repo.ReplaceDocument(binder, added.Id, WriteWord(root, "v3.docx", "versione 3"), "L. Rossi");

        Assert.False(second.OriginalPreserved);
        Assert.Equal(2, second.Revision);
        var document = repo.LoadManifest(binder).Attachments.Single();
        Assert.Equal(originalPath, document.OriginalRelativePath);
        Assert.Equal(firstBytes, File.ReadAllBytes(Path.Combine(binder, document.OriginalRelativePath!)));
        Assert.Equal("L. Rossi", document.ModifiedBy);
        Assert.False(File.Exists(intermediate));
        Assert.True(File.Exists(Path.Combine(binder, document.RelativePdfPath)));
        Assert.Single(Directory.GetFiles(Path.Combine(binder, "originals")));
    }

    [Fact]
    public void Replace_CanChangeTheFormatOfTheCover()
    {
        var (repo, binder, root) = NewBinder();

        repo.ReplaceDocument(binder, BinderLayout.CoverDocumentId, WriteWord(root, "nuova.docx", "contropagina"), "G. Verdi");

        var manifest = repo.LoadManifest(binder);
        Assert.Equal("cover.docx", manifest.Cover.RelativePdfPath);
        Assert.Equal(1, manifest.Cover.Revision);
        Assert.False(File.Exists(Path.Combine(binder, "cover.pdf")));
        Assert.EndsWith(".pdf", manifest.Cover.OriginalRelativePath);
        Assert.True(File.Exists(Path.Combine(binder, manifest.Cover.OriginalRelativePath!)));
    }

    [Fact]
    public void Replace_KeepsTheAnnotationsOfTheDocument()
    {
        var (repo, binder, root) = NewBinder();
        var added = repo.AddAttachment(binder, WritePdf(root, "a.pdf", "A"), "Allegato");
        repo.SaveAnnotations(binder, added.Id, [new PageAnnotation { PageIndex = 0, Strokes = [new InkStroke()] }]);

        repo.ReplaceDocument(binder, added.Id, WritePdf(root, "b.pdf", "B"), "G. Verdi");

        Assert.Single(repo.LoadAnnotations(binder, added.Id));
    }

    [Fact]
    public void Replace_RejectsUnknownDocumentsAndEmptyAuthor()
    {
        var (repo, binder, root) = NewBinder();
        var file = WritePdf(root, "b.pdf", "B");

        Assert.Throws<ArgumentException>(() => repo.ReplaceDocument(binder, "non-esiste", file, "G. Verdi"));
        Assert.Throws<ArgumentException>(() => repo.ReplaceDocument(binder, BinderLayout.CoverDocumentId, file, " "));
    }

    [Fact]
    public void Library_OnlyTheHolderCanReplaceAndTheLogRecordsIt()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var holder = library.Engine.GetPractice("2.026_1").HolderId;
        var other = library.State.Users.First(u => u.Id != holder).Id;
        var attachmentId = library.Binders.LoadManifest(library.GetBinderPath("2.026_1")).Attachments[0].Id;
        var file = WriteWord(CreateTempFolder(), "nuovo.docx", "nuovo");

        Assert.False(library.CanReplace("2.026_1", other));
        Assert.Throws<InvalidOperationException>(() => library.ReplaceDocument("2.026_1", attachmentId, file, other));

        var result = library.ReplaceDocument("2.026_1", attachmentId, file, holder);

        Assert.True(result.OriginalPreserved);
        Assert.Contains(library.State.Log, entry => entry.Text.Contains("originale conservato") && entry.Text.Contains("2.026_1"));
    }

    [Fact]
    public void DemoPractice2_HasAWordAttachmentAlreadyReplacedOnce()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var binder = library.GetBinderPath("2.026_2");
        var manifest = library.Binders.LoadManifest(binder);

        Assert.Equal(new[] { "1", "L1.1", "2" }, BinderLayout.AttachmentSide(manifest).Select(i => i.Label));
        var word = BinderLayout.AttachmentSide(manifest).Single(i => i.IsWord);
        Assert.Equal(1, word.Revision);
        Assert.True(word.HasOriginal);
        Assert.True(File.Exists(Path.Combine(binder, word.OriginalRelativePath!)));
        Assert.True(File.Exists(Path.Combine(binder, word.RelativePdfPath)));
    }
}
