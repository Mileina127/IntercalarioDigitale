using IntercalarioDigitale.Core;

var repo = new BinderRepository();

if (args.Length == 0)
{
    PrintHelp();
    return;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "init":
            RunInit(args, repo);
            break;
        case "add-attachment":
            RunAddAttachment(args, repo);
            break;
        case "list":
            RunList(args, repo);
            break;
        case "annotate-demo":
            RunAnnotateDemo(args, repo);
            break;
        default:
            Console.WriteLine("Comando non riconosciuto.");
            PrintHelp();
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Errore: {ex.Message}");
    Environment.ExitCode = 1;
}

static void RunInit(string[] args, BinderRepository repo)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Uso: init <binderRoot> <coverPdfPath> [coverTitle]");
        return;
    }

    var manifest = repo.CreateNew(args[1], args[2], args.Length >= 4 ? args[3] : null);
    Console.WriteLine($"Intercalario creato con copertina: {manifest.Cover.Title}");
}

static void RunAddAttachment(string[] args, BinderRepository repo)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Uso: add-attachment <binderRoot> <pdfPath> [title]");
        return;
    }

    var attachment = repo.AddAttachment(args[1], args[2], args.Length >= 4 ? args[3] : null);
    Console.WriteLine($"Allegato aggiunto: {attachment.Id} | {attachment.Title}");
}

static void RunList(string[] args, BinderRepository repo)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: list <binderRoot>");
        return;
    }

    var manifest = repo.LoadManifest(args[1]);
    Console.WriteLine($"Copertina: {manifest.Cover.Title}");
    foreach (var item in manifest.Attachments)
    {
        Console.WriteLine($"- {item.Id} | {item.Title} | {item.RelativePdfPath}");
    }
}

static void RunAnnotateDemo(string[] args, BinderRepository repo)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Uso: annotate-demo <binderRoot> <documentId>");
        return;
    }

    var annotations = new List<PageAnnotation>
    {
        new()
        {
            PageIndex = 0,
            Strokes =
            [
                new InkStroke
                {
                    ColorHex = "#E53935",
                    Thickness = 3,
                    Points =
                    [
                        new InkStrokePoint { X = 16, Y = 24, Pressure = 0.55f },
                        new InkStrokePoint { X = 64, Y = 70, Pressure = 0.65f },
                        new InkStrokePoint { X = 92, Y = 110, Pressure = 0.75f }
                    ]
                }
            ]
        }
    };

    repo.SaveAnnotations(args[1], args[2], annotations);
    Console.WriteLine("Annotazioni demo salvate.");
}

static void PrintHelp()
{
    Console.WriteLine("IntercalarioDigitale MVP CLI (PDF-based)");
    Console.WriteLine("Comandi:");
    Console.WriteLine("  init <binderRoot> <coverPdfPath> [coverTitle]");
    Console.WriteLine("  add-attachment <binderRoot> <pdfPath> [title]");
    Console.WriteLine("  list <binderRoot>");
    Console.WriteLine("  annotate-demo <binderRoot> <documentId>");
}
