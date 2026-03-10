using IntercalarioDigitale.Core;

namespace IntercalarioDigitale.Core.Tests;

public sealed class BinderRepositoryTests
{
    [Fact]
    public void CreateNew_ShouldGenerateManifestAndCover()
    {
        var root = CreateTempFolder();
        var cover = CreateDummyPdf(root, "cover.pdf");
        var binderFolder = Path.Combine(root, "binder");

        var repo = new BinderRepository();
        var manifest = repo.CreateNew(binderFolder, cover, "Cover Test");

        Assert.Equal("Cover Test", manifest.Cover.Title);
        Assert.True(File.Exists(Path.Combine(binderFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(binderFolder, "cover.pdf")));
    }

    [Fact]
    public void AddAttachment_ShouldUpdateManifest()
    {
        var root = CreateTempFolder();
        var cover = CreateDummyPdf(root, "cover.pdf");
        var attachment = CreateDummyPdf(root, "attachment.pdf");
        var binderFolder = Path.Combine(root, "binder");

        var repo = new BinderRepository();
        repo.CreateNew(binderFolder, cover);

        var added = repo.AddAttachment(binderFolder, attachment, "Allegato 1");
        var manifest = repo.LoadManifest(binderFolder);

        Assert.Contains(manifest.Attachments, a => a.Id == added.Id && a.Title == "Allegato 1");
        Assert.True(File.Exists(Path.Combine(binderFolder, added.RelativePdfPath)));
    }

    [Fact]
    public void SaveAndLoadAnnotations_ShouldRoundTrip()
    {
        var root = CreateTempFolder();
        var cover = CreateDummyPdf(root, "cover.pdf");
        var binderFolder = Path.Combine(root, "binder");

        var repo = new BinderRepository();
        repo.CreateNew(binderFolder, cover);

        var expected = new List<PageAnnotation>
        {
            new()
            {
                PageIndex = 1,
                Strokes =
                [
                    new InkStroke
                    {
                        Points = [new InkStrokePoint { X = 10, Y = 20, Pressure = 0.7f }]
                    }
                ]
            }
        };

        repo.SaveAnnotations(binderFolder, "doc-123", expected);
        var loaded = repo.LoadAnnotations(binderFolder, "doc-123");

        Assert.Single(loaded);
        Assert.Equal(1, loaded[0].PageIndex);
        Assert.Single(loaded[0].Strokes);
        Assert.Single(loaded[0].Strokes[0].Points);
        Assert.Equal(10, loaded[0].Strokes[0].Points[0].X);
    }

    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "intercalario-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateDummyPdf(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, "%PDF-1.1\n% Dummy file for tests\n");
        return path;
    }
}
