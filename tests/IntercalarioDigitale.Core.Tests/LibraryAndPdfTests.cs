using System.Text;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Assistant;
using IntercalarioDigitale.Core.Pdf;
using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class LibraryAndPdfTests
{
    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gerarchia-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData(2026, "2.026")]
    [InlineData(2027, "2.027")]
    [InlineData(2105, "2.105")]
    public void PracticeId_PrefixUsesThousandsSeparator(int year, string expected)
    {
        Assert.Equal(expected, PracticeIdGenerator.Prefix(year));
    }

    [Fact]
    public void PracticeId_NextContinuesTheSequence()
    {
        Assert.Equal("2.026_1", PracticeIdGenerator.Next([], 2026));
        Assert.Equal("2.026_4", PracticeIdGenerator.Next(["2.026_1", "2.026_3", "2.025_9", "abc"], 2026));
        Assert.Equal("2.027_1", PracticeIdGenerator.Next(["2.026_3"], 2027));
    }

    [Fact]
    public void SamplePdf_HasAValidStructure()
    {
        var bytes = SamplePdfFactory.Create("Titolo (prova) \\ test", "Sottotitolo", 3);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 3", text);

        var startxref = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        var declaredOffset = int.Parse(text[(startxref + "startxref\n".Length)..].Split('\n')[0]);
        Assert.Equal(text.IndexOf("xref\n0 ", StringComparison.Ordinal), declaredOffset);

        // Ogni voce della tabella xref punta all'inizio di "N 0 obj".
        var table = text[declaredOffset..].Split('\n');
        var count = int.Parse(table[1].Split(' ')[1]);
        for (var i = 1; i < count; i++)
        {
            var offset = int.Parse(table[2 + i].Split(' ')[0]);
            Assert.StartsWith($"{i} 0 obj", text[offset..]);
        }
    }

    [Fact]
    public void SamplePdf_RejectsZeroPages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SamplePdfFactory.Create("a", "b", 0));
    }

    [Fact]
    public void Library_SeedsDemoPracticesOnFirstRun()
    {
        var root = CreateTempFolder();

        var library = new PracticeLibrary(root);

        Assert.Equal(5, library.State.Practices.Count);
        Assert.Equal(7, library.State.Users.Count);
        Assert.Equal(5, library.Engine.GetPractice("2.026_3").Validations.Count);

        var practice = library.State.Practices[2];
        var manifest = library.Binders.LoadManifest(library.GetBinderPath(practice.Id));
        Assert.Single(manifest.Attachments);
        Assert.True(File.Exists(Path.Combine(library.GetBinderPath(practice.Id), manifest.Cover.RelativePdfPath)));
        Assert.True(File.Exists(Path.Combine(library.GetBinderPath(practice.Id), manifest.Attachments[0].RelativePdfPath)));
    }

    [Fact]
    public void Library_ReloadsTheSavedState()
    {
        var root = CreateTempFolder();
        var first = new PracticeLibrary(root);
        first.Engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        first.Engine.SetLeave("colombo", true, "rossi");
        first.Save();

        var second = new PracticeLibrary(root);

        Assert.Equal(5, second.State.Practices.Count);
        Assert.Equal("rossi", second.Engine.GetPractice("2.026_1").HolderId);
        Assert.True(second.Engine.GetUser("colombo").OnLeave);
        Assert.Equal("rossi", second.Engine.GetUser("colombo").DelegateId);
    }

    [Fact]
    public void Library_ReplacesACorruptStateFile()
    {
        var root = CreateTempFolder();
        File.WriteAllText(Path.Combine(root, "workflow.json"), "{ non e' json");

        var library = new PracticeLibrary(root);

        Assert.Equal(5, library.State.Practices.Count);
        Assert.True(File.Exists(Path.Combine(root, "workflow.json.bak")));
    }

    [Fact]
    public void Library_CreatesAPracticeWithGivenPdfs()
    {
        var root = CreateTempFolder();
        var library = new PracticeLibrary(root);
        var cover = Path.Combine(root, "mia-cover.pdf");
        var attachment = Path.Combine(root, "mio-allegato.pdf");
        File.WriteAllBytes(cover, SamplePdfFactory.Create("Cover", "c", 1));
        File.WriteAllBytes(attachment, SamplePdfFactory.Create("Allegato", "a", 2));

        var practice = library.CreatePractice("Nuovo documento", "verdi", cover, attachment, 2026);

        Assert.Equal("2.026_6", practice.Id);
        Assert.Equal("verdi", practice.HolderId);
        var manifest = library.Binders.LoadManifest(library.GetBinderPath(practice.Id));
        Assert.Equal("Nuovo documento", manifest.Attachments[0].Title);
    }

    [Fact]
    public void Assistant_AnswersFromThePracticeData()
    {
        var library = new PracticeLibrary(CreateTempFolder());
        var practice = library.Engine.GetPractice("2.026_3");
        var context = new AssistantContext(practice, library.State.Users, "Allegato corrente", "Verbale", 4, 1, 2);

        var concordanze = SimulatedAssistant.Reply("Controlla le concordanze", context);
        Assert.Contains("3 concordati", concordanze);
        Assert.Contains("G. Verdi (Amm.)", concordanze);

        var approvazioni = SimulatedAssistant.Reply("Verifica le approvazioni", context);
        Assert.Contains("A. Moretti", approvazioni);
        Assert.Contains("01 Nov 2023, 16:30", approvazioni);

        Assert.Contains("1 pagine della contropagina e 2", SimulatedAssistant.Reply("Quali pagine ho annotato?", context));
        Assert.Contains("4 pagine", SimulatedAssistant.Reply("Riassumi l'allegato", context));
    }
}
