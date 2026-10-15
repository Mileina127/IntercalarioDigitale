using System.Text.Json;

namespace IntercalarioDigitale.Core;

/// <summary>Esito di <see cref="BinderRepository.ReplaceDocument"/>.</summary>
public sealed record ReplaceResult(string DocumentId, int Revision, bool OriginalPreserved, string? OriginalRelativePath);

public sealed class BinderRepository
{
    private const string ManifestFileName = "manifest.json";
    private const string AttachmentsFolderName = "attachments";
    private const string AnnotationsFolderName = "annotations";
    private const string OriginalsFolderName = "originals";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BinderManifest CreateNew(string binderRoot, string coverPdfFilePath, string? coverTitle = null)
    {
        EnsureSupportedFile(coverPdfFilePath);

        Directory.CreateDirectory(binderRoot);
        Directory.CreateDirectory(Path.Combine(binderRoot, AttachmentsFolderName));
        Directory.CreateDirectory(Path.Combine(binderRoot, AnnotationsFolderName));

        var coverRelativePath = "cover" + DocumentFormats.ExtensionOf(coverPdfFilePath);
        File.Copy(coverPdfFilePath, Path.Combine(binderRoot, coverRelativePath), overwrite: true);

        var manifest = new BinderManifest
        {
            Cover = new CoverDocument
            {
                Title = string.IsNullOrWhiteSpace(coverTitle) ? "Copertina" : coverTitle,
                RelativePdfPath = coverRelativePath
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
        EnsureSupportedFile(sourcePdfPath);
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
        EnsureSupportedFile(sourcePdfPath);
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
            RelativePdfPath = Path.Combine(AttachmentsFolderName, $"{Guid.NewGuid():N}{DocumentFormats.ExtensionOf(sourcePdfPath)}")
        };

        File.Copy(sourcePdfPath, Path.Combine(binderRoot, document.RelativePdfPath), overwrite: true);
        return document;
    }

    /// <summary>
    /// Sostituisce un documento (la contropagina, un allegato o un annesso) con una nuova versione.
    /// Alla prima sostituzione il file di partenza viene conservato in <c>originals/</c> e non viene più toccato;
    /// le versioni intermedie non si conservano: restano solo l'originale e quella attuale.
    /// Le annotazioni a penna del documento non vengono modificate.
    /// </summary>
    public ReplaceResult ReplaceDocument(string binderRoot, string documentId, string newFilePath, string modifiedBy)
    {
        EnsureSupportedFile(newFilePath);
        if (string.IsNullOrWhiteSpace(modifiedBy))
        {
            throw new ArgumentException("Chi sostituisce il documento è obbligatorio", nameof(modifiedBy));
        }

        var manifest = LoadManifest(binderRoot);
        var document = FindDocument(manifest, documentId)
            ?? throw new ArgumentException("Documento non trovato", nameof(documentId));

        var oldRelativePath = document.RelativePdfPath;
        var oldFullPath = Path.Combine(binderRoot, oldRelativePath);
        var newExtension = DocumentFormats.ExtensionOf(newFilePath);

        var preserved = false;
        if (document.OriginalRelativePath is null)
        {
            Directory.CreateDirectory(Path.Combine(binderRoot, OriginalsFolderName));
            var originalRelativePath = Path.Combine(OriginalsFolderName, documentId + DocumentFormats.ExtensionOf(oldRelativePath));
            File.Copy(oldFullPath, Path.Combine(binderRoot, originalRelativePath), overwrite: false);
            document.OriginalRelativePath = originalRelativePath;
            preserved = true;
        }

        var newRelativePath = documentId == BinderLayout.CoverDocumentId
            ? "cover" + newExtension
            : Path.Combine(AttachmentsFolderName, $"{Guid.NewGuid():N}{newExtension}");

        File.Copy(newFilePath, Path.Combine(binderRoot, newRelativePath), overwrite: true);

        document.RelativePdfPath = newRelativePath;
        document.Revision++;
        document.ModifiedAtUtc = DateTimeOffset.UtcNow;
        document.ModifiedBy = modifiedBy.Trim();
        SaveManifest(binderRoot, manifest);

        if (!string.Equals(oldRelativePath, newRelativePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldFullPath))
        {
            try
            {
                File.Delete(oldFullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // la versione intermedia resta sul disco ma non è più nel manifest: non è un errore
            }
        }

        return new ReplaceResult(documentId, document.Revision, preserved, document.OriginalRelativePath);
    }

    private static IRevisableDocument? FindDocument(BinderManifest manifest, string documentId)
    {
        if (documentId == BinderLayout.CoverDocumentId)
        {
            return manifest.Cover;
        }

        return manifest.CoverAttachments.FirstOrDefault(a => a.Id == documentId)
            ?? manifest.Attachments.FirstOrDefault(a => a.Id == documentId);
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

    private static void EnsureSupportedFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File non trovato", filePath);
        }

        if (!DocumentFormats.IsSupported(filePath))
        {
            throw new ArgumentException("Il file deve essere un PDF o un documento Word (.docx, .doc)", nameof(filePath));
        }
    }
}
