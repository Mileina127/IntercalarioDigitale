using System.Text.Json;

namespace IntercalarioDigitale.Core;

public sealed class BinderRepository
{
    private const string ManifestFileName = "manifest.json";
    private const string AttachmentsFolderName = "attachments";
    private const string AnnotationsFolderName = "annotations";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BinderManifest CreateNew(string binderRoot, string coverPdfFilePath, string? coverTitle = null)
    {
        EnsurePdfExists(coverPdfFilePath);

        Directory.CreateDirectory(binderRoot);
        Directory.CreateDirectory(Path.Combine(binderRoot, AttachmentsFolderName));
        Directory.CreateDirectory(Path.Combine(binderRoot, AnnotationsFolderName));

        var coverDestination = Path.Combine(binderRoot, "cover.pdf");
        File.Copy(coverPdfFilePath, coverDestination, overwrite: true);

        var manifest = new BinderManifest
        {
            Cover = new CoverDocument
            {
                Title = string.IsNullOrWhiteSpace(coverTitle) ? "Copertina" : coverTitle,
                RelativePdfPath = "cover.pdf"
            }
        };

        SaveManifest(binderRoot, manifest);
        return manifest;
    }

    public BinderManifest LoadManifest(string binderRoot)
    {
        var fullPath = Path.Combine(binderRoot, ManifestFileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Manifest non trovato", fullPath);
        }

        var json = File.ReadAllText(fullPath);
        var manifest = JsonSerializer.Deserialize<BinderManifest>(json, JsonOptions);
        return manifest ?? throw new InvalidOperationException("Manifest non valido");
    }

    /// <summary>Aggiunge un allegato; con <paramref name="parentId"/> lo aggiunge come annesso di un allegato esistente.</summary>
    public AttachmentDocument AddAttachment(string binderRoot, string sourcePdfPath, string? title = null, string? parentId = null)
    {
        EnsurePdfExists(sourcePdfPath);
        var manifest = LoadManifest(binderRoot);

        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = manifest.Attachments.FirstOrDefault(a => a.Id == parentId)
                ?? throw new ArgumentException("L'allegato a cui collegare l'annesso non esiste", nameof(parentId));
            if (!string.IsNullOrEmpty(parent.ParentId))
            {
                throw new ArgumentException("Un annesso non può avere a sua volta degli annessi", nameof(parentId));
            }
        }

        var document = CopyIntoBinder(binderRoot, sourcePdfPath, title);
        document.ParentId = string.IsNullOrEmpty(parentId) ? null : parentId;

        manifest.Attachments.Add(document);
        SaveManifest(binderRoot, manifest);
        return document;
    }

    /// <summary>Aggiunge un documento allegato alla contropagina (compare nella colonna di sinistra del viewer).</summary>
    public AttachmentDocument AddCoverAttachment(string binderRoot, string sourcePdfPath, string? title = null)
    {
        EnsurePdfExists(sourcePdfPath);
        var manifest = LoadManifest(binderRoot);

        var document = CopyIntoBinder(binderRoot, sourcePdfPath, title);
        manifest.CoverAttachments.Add(document);
        SaveManifest(binderRoot, manifest);
        return document;
    }

    private static AttachmentDocument CopyIntoBinder(string binderRoot, string sourcePdfPath, string? title)
    {
        Directory.CreateDirectory(Path.Combine(binderRoot, AttachmentsFolderName));

        var document = new AttachmentDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(sourcePdfPath) : title,
            RelativePdfPath = Path.Combine(AttachmentsFolderName, $"{Guid.NewGuid():N}.pdf")
        };

        File.Copy(sourcePdfPath, Path.Combine(binderRoot, document.RelativePdfPath), overwrite: true);
        return document;
    }

    public void SaveAnnotations(string binderRoot, string documentId, IReadOnlyCollection<PageAnnotation> annotations)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("DocumentId obbligatorio", nameof(documentId));
        }

        var annotationPath = GetAnnotationPath(binderRoot, documentId);
        var json = JsonSerializer.Serialize(annotations, JsonOptions);
        File.WriteAllText(annotationPath, json);
    }

    public IReadOnlyList<PageAnnotation> LoadAnnotations(string binderRoot, string documentId)
    {
        var annotationPath = GetAnnotationPath(binderRoot, documentId);
        if (!File.Exists(annotationPath))
        {
            return [];
        }

        var json = File.ReadAllText(annotationPath);
        return JsonSerializer.Deserialize<List<PageAnnotation>>(json, JsonOptions) ?? [];
    }

    private static string GetAnnotationPath(string binderRoot, string documentId)
    {
        var annotationsFolder = Path.Combine(binderRoot, AnnotationsFolderName);
        Directory.CreateDirectory(annotationsFolder);
        return Path.Combine(annotationsFolder, $"{documentId}.json");
    }

    private static void SaveManifest(string binderRoot, BinderManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(Path.Combine(binderRoot, ManifestFileName), json);
    }

    private static void EnsurePdfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("PDF non trovato", filePath);
        }

        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Il file deve essere un PDF", nameof(filePath));
        }
    }
}
