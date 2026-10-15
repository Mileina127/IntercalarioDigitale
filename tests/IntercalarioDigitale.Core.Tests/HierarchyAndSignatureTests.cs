using System.IO.Compression;
using IntercalarioDigitale.Core.Signatures;
using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class HierarchyAndSignatureTests
{
    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gerarchia-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static PracticeLibrary NewLibrary() => new(CreateTempFolder());

    // ---------- percorso ----------

    [Theory]
    [InlineData(0, "verdi,rossi,moretti")]
    [InlineData(4, "verdi,rossi,moretti,sezv4,uffv4,autv4")]
    [InlineData(3, "verdi,rossi,moretti,sezv4,uffv4,autv4,sezv3,uffv3,autv3")]
    [InlineData(2, "verdi,rossi,moretti,sezv4,uffv4,autv4,sezv3,uffv3,autv3,sezv2,uffv2,autv2")]
    [InlineData(1, "verdi,rossi,moretti,sezv4,uffv4,autv4,sezv3,uffv3,autv3,sezv2,uffv2,autv2,sezv1,uffv1,neri")]
    public void Route_RepeatsTheClimbForEveryLevelFromC4(int level, string expected)
    {
        var library = NewLibrary();

        var route = library.Engine.BuildRoute("verdi", level);

        Assert.Equal(expected.Split(','), route);
    }

    [Fact]
    public void Route_StartsFromTheSectionOfTheOtherDepartment()
    {
        var library = NewLibrary();

        Assert.Equal(new[] {"bianchi", "colombo", "gallo", "sezv4", "uffv4", "autv4"}, library.Engine.BuildRoute("bianchi", 4));
    }

    [Fact]
    public void Route_RejectsWrongLevelsAndVerticeStarts()
    {
        var library = NewLibrary();

        Assert.Throws<ArgumentOutOfRangeException>(() => library.Engine.BuildRoute("verdi", 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => library.Engine.BuildRoute("verdi", -1));
        Assert.Throws<InvalidOperationException>(() => library.Engine.BuildRoute("neri", 0));
    }

    [Fact]
    public void Practice_WalksTheWholeRouteOneStepAtATime()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 3);
        Assert.Equal(3, practice.DecisionLevel);
        Assert.Equal(9, practice.Route.Count);

        var holders = new List<string> { practice.HolderId };
        while (library.Engine.CanForward(practice, practice.HolderId))
        {
            var targets = library.Engine.GetForwardTargets(practice, practice.HolderId)
                .Where(t => t.Mode == TransferMode.Forward)
                .ToList();
            Assert.Single(targets);

            library.Engine.Forward(practice.Id, practice.HolderId, targets[0]);
            holders.Add(practice.HolderId);
        }

        Assert.Equal(practice.Route, holders);
        Assert.Equal("autv3", practice.HolderId);
    }

    [Fact]
    public void Forward_CannotSkipALevel()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 1);

        Assert.Throws<InvalidOperationException>(
            () => library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "moretti")));
        Assert.Throws<InvalidOperationException>(
            () => library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "neri")));
    }

    [Fact]
    public void Return_GoesBackOneStepAndTheRouteFollows()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 4);
        library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        library.Engine.Forward(practice.Id, "rossi", new ForwardTarget(TransferMode.Forward, "moretti"));
        Assert.Equal(2, practice.RouteIndex);

        library.Engine.Return(practice.Id, "moretti");

        Assert.Equal("rossi", practice.HolderId);
        Assert.Equal(1, practice.RouteIndex);
        Assert.Equal("moretti", library.Engine.GetForwardTargets(practice, "rossi").Single().RecipientId);
    }

    [Fact]
    public void Forward_ThroughADelegate_KeepsTheRoute()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 0);
        library.Engine.SetLeave("rossi", true, "colombo");

        library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        Assert.Equal("colombo", practice.HolderId);

        library.Engine.Forward(practice.Id, "colombo", new ForwardTarget(TransferMode.Forward, "moretti"));
        Assert.Equal("moretti", practice.HolderId);

        library.Engine.Return(practice.Id, "moretti");
        Assert.Equal("colombo", practice.HolderId);
        Assert.Equal(1, practice.RouteIndex);
    }

    [Fact]
    public void Tramite_PassesThroughTheOtherDepartmentThenKeepsClimbing()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 4);
        library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        library.Engine.Forward(practice.Id, "rossi", new ForwardTarget(TransferMode.Forward, "moretti"));

        library.Engine.Forward(practice.Id, "moretti", new ForwardTarget(TransferMode.Tramite, "bianchi", "log"));

        Assert.Equal(new[] {"verdi", "rossi", "moretti", "bianchi", "colombo", "gallo", "sezv4", "uffv4", "autv4"},
            practice.Route);

        library.Engine.Forward(practice.Id, "bianchi", new ForwardTarget(TransferMode.Forward, "colombo"));
        library.Engine.Forward(practice.Id, "colombo", new ForwardTarget(TransferMode.Forward, "gallo"));
        library.Engine.Forward(practice.Id, "gallo", new ForwardTarget(TransferMode.Forward, "sezv4"));
        Assert.Equal("sezv4", practice.HolderId);
    }

    [Fact]
    public void GetRoute_MarksDoneCurrentAndUpcomingSteps()
    {
        var library = NewLibrary();
        var practice = library.Engine.GetPractice("2.026_3"); // C2, in mano a Moretti

        var steps = library.Engine.GetRoute(practice);

        Assert.Equal(12, steps.Count);
        Assert.Equal(new[] {"verdi", "rossi"}, steps.Where(s => s.State == RouteStepState.Done).Select(s => s.User.Id));
        Assert.Equal("moretti", steps.Single(s => s.State == RouteStepState.Current).User.Id);
        Assert.Equal(9, steps.Count(s => s.State == RouteStepState.Upcoming));
        Assert.Equal("Reparto", steps[0].Stage);
        Assert.Equal("C4", steps[3].Stage);
        Assert.Equal("C2", steps[^1].Stage);
    }

    [Fact]
    public void Leave_NeedsAPeerOfTheSameLevel()
    {
        var library = NewLibrary();

        Assert.Throws<InvalidOperationException>(() => library.Engine.SetLeave("autv4", true, "neri"));
        Assert.Throws<InvalidOperationException>(() => library.Engine.SetLeave("autv4", true, "moretti"));
    }

    [Theory]
    [InlineData("verdi", false)]
    [InlineData("bianchi", false)]
    [InlineData("rossi", true)]
    [InlineData("colombo", true)]
    [InlineData("moretti", true)]
    [InlineData("gallo", true)]
    [InlineData("neri", true)]
    [InlineData("autv2", true)]
    [InlineData("autv3", true)]
    [InlineData("autv4", true)]
    [InlineData("sezv2", false)]
    [InlineData("uffv2", false)]
    [InlineData("uffv4", false)]
    public void Signature_IsForCapiUfficioCapiRepartoAndAutorita(string userId, bool expected)
    {
        var library = NewLibrary();

        Assert.Equal(expected, WorkflowEngine.CanHaveSignature(library.Engine.GetUser(userId)));
    }

    // ---------- aggiornamento di vecchi dati ----------

    [Fact]
    public void Upgrade_AddsTheVerticeLineToAnOldState()
    {
        var root = CreateTempFolder();
        var first = new PracticeLibrary(root);
        first.State.Users.RemoveAll(u => u.Level > 0 && u.Id != "neri");
        var neri = first.Engine.GetUser("neri");
        neri.Level = 0;
        foreach (var user in first.State.Users)
        {
            user.SuperiorId = null;
        }

        foreach (var practice in first.State.Practices)
        {
            practice.Route = [];
            practice.RouteIndex = 0;
        }

        first.Save();

        var second = new PracticeLibrary(root);

        Assert.Equal(18, second.State.Users.Count);
        Assert.Equal(1, second.Engine.GetUser("neri").Level);
        Assert.Equal("rossi", second.Engine.GetUser("verdi").SuperiorId);
        Assert.Equal(new[] {"verdi", "rossi", "moretti"}, second.Engine.BuildRoute("verdi", 0));
        Assert.False(DemoData.UpgradeState(second.State));
    }

    // ---------- firme ----------

    [Fact]
    public void SampleSignature_IsAValidPng()
    {
        var bytes = SampleSignatureFactory.Create("L. Rossi");

        Assert.True(PngFile.IsPng(bytes));
        Assert.Equal((360, 120), PngFile.Size(bytes));

        // Ogni blocco: lunghezza, tipo, dati, CRC; il PNG finisce con IEND (CRC noto AE426082).
        var position = 8;
        var types = new List<string>();
        var idat = new MemoryStream();
        while (position < bytes.Length)
        {
            var length = (bytes[position] << 24) | (bytes[position + 1] << 16) | (bytes[position + 2] << 8) | bytes[position + 3];
            var type = System.Text.Encoding.ASCII.GetString(bytes, position + 4, 4);
            var data = bytes.AsSpan(position + 8, length).ToArray();
            var crcBytes = bytes.AsSpan(position + 8 + length, 4);
            var crc = ((uint)crcBytes[0] << 24) | ((uint)crcBytes[1] << 16) | ((uint)crcBytes[2] << 8) | crcBytes[3];

            Assert.Equal(SampleSignatureFactory.Crc32(System.Text.Encoding.ASCII.GetBytes(type), data), crc);
            types.Add(type);
            if (type == "IDAT")
            {
                idat.Write(data);
            }

            position += 12 + length;
        }

        Assert.Equal(new[] {"IHDR", "IDAT", "IEND"}, types);
        Assert.Equal(0xAE426082u, SampleSignatureFactory.Crc32(System.Text.Encoding.ASCII.GetBytes("IEND"), []));

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal(120 * (1 + 360 * 4), raw.Length);

        var pixels = raw.ToArray();
        var inked = 0;
        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 360; x++)
            {
                if (pixels[y * (1 + 360 * 4) + 1 + x * 4 + 3] > 0)
                {
                    inked++;
                }
            }
        }

        Assert.InRange(inked, 500, 20000);
    }

    [Fact]
    public void SampleSignature_DependsOnTheName()
    {
        Assert.NotEqual(SampleSignatureFactory.Create("L. Rossi"), SampleSignatureFactory.Create("A. Moretti"));
        Assert.Equal(SampleSignatureFactory.Create("L. Rossi"), SampleSignatureFactory.Create("L. Rossi"));
    }

    [Fact]
    public void Library_GivesSampleSignaturesOnlyToWhoCanSign()
    {
        var library = NewLibrary();

        Assert.NotNull(library.GetSignaturePath("rossi"));
        Assert.NotNull(library.GetSignaturePath("moretti"));
        Assert.NotNull(library.GetSignaturePath("neri"));
        Assert.NotNull(library.GetSignaturePath("autv4"));
        Assert.Null(library.GetSignaturePath("verdi"));
        Assert.Null(library.GetSignaturePath("sezv3"));
        Assert.Null(library.GetSignaturePath("uffv3"));
    }

    [Fact]
    public void SetUserSignature_AcceptsOnlyPngForWhoCanSign()
    {
        var library = NewLibrary();
        var png = Path.Combine(library.RootFolder, "mia-firma.png");
        File.WriteAllBytes(png, SampleSignatureFactory.Create("Altro nome"));
        var notPng = Path.Combine(library.RootFolder, "finta.png");
        File.WriteAllText(notPng, "non sono un'immagine");

        library.SetUserSignature("rossi", png);
        Assert.Equal(File.ReadAllBytes(png), File.ReadAllBytes(library.GetSignaturePath("rossi")!));

        Assert.Throws<InvalidDataException>(() => library.SetUserSignature("rossi", notPng));
        Assert.Throws<InvalidOperationException>(() => library.SetUserSignature("verdi", png));
        Assert.Throws<InvalidOperationException>(() => library.SetUserSignature("uffv2", png));

        library.RemoveUserSignature("rossi");
        Assert.Null(library.GetSignaturePath("rossi"));
    }

    [Fact]
    public void ApplyValidation_CopiesTheSignatureIntoTheBinder()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026);
        library.Engine.Forward(practice.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        var placement = new SignaturePlacement { DocumentId = "cover", PageIndex = 0, X = 0.55, Y = 0.8, Width = 0.3 };

        var stamp = library.ApplyValidation(practice.Id, "rossi", ValidationKind.Concordato, placement);

        var saved = library.GetStampSignaturePath(practice.Id, stamp);
        Assert.NotNull(saved);
        Assert.StartsWith(library.GetBinderPath(practice.Id), saved);
        Assert.Equal(0.3, stamp.Placement!.Width);

        // Anche se Rossi cambia o toglie la firma, il timbro già apposto resta com'era.
        library.RemoveUserSignature("rossi");
        Assert.True(File.Exists(saved));

        var reloaded = new PracticeLibrary(library.RootFolder);
        var again = reloaded.Engine.GetPractice(practice.Id).Validations.Single();
        Assert.Equal(stamp.SignatureFile, again.SignatureFile);
        Assert.Equal(0.8, again.Placement!.Y);
    }

    [Fact]
    public void ApplyValidation_WithoutPlacement_IsAPlainStamp()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026);

        var stamp = library.ApplyValidation(practice.Id, "verdi", ValidationKind.Concordato);

        Assert.Null(stamp.SignatureFile);
        Assert.Null(stamp.Placement);
    }

    [Fact]
    public void ApplyValidation_RejectsSignaturesForWhoCannotSignOrHasNone()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026);
        var placement = new SignaturePlacement { DocumentId = "cover", PageIndex = 0, X = 0.1, Y = 0.1, Width = 0.3 };

        Assert.Throws<InvalidOperationException>(
            () => library.ApplyValidation(practice.Id, "verdi", ValidationKind.Concordato, placement));
        Assert.Throws<InvalidOperationException>(
            () => library.ApplyValidation(practice.Id, "uffv4", ValidationKind.Concordato, placement));

        library.RemoveUserSignature("rossi");
        Assert.Throws<InvalidOperationException>(
            () => library.ApplyValidation(practice.Id, "rossi", ValidationKind.Concordato, placement));
        Assert.Empty(library.Engine.GetPractice(practice.Id).Validations);
    }

    [Fact]
    public void ApplyValidation_DuplicateLeavesNoOrphanSignatureFile()
    {
        var library = NewLibrary();
        var practice = library.CreatePractice("Prova", "verdi", year: 2026);
        var placement = new SignaturePlacement { DocumentId = "cover", PageIndex = 0, X = 0.1, Y = 0.1, Width = 0.3 };
        library.ApplyValidation(practice.Id, "rossi", ValidationKind.Concordato, placement);

        Assert.Throws<InvalidOperationException>(
            () => library.ApplyValidation(practice.Id, "rossi", ValidationKind.Concordato, placement));

        var folder = Path.Combine(library.GetBinderPath(practice.Id), "firme");
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void CreatePractice_RejectsAVerticeStartAndBadLevel()
    {
        var library = NewLibrary();

        Assert.Throws<InvalidOperationException>(() => library.CreatePractice("Prova", "neri", year: 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => library.CreatePractice("Prova", "verdi", year: 2026, decisionLevel: 9));
        Assert.Equal(5, library.State.Practices.Count);
    }
}
