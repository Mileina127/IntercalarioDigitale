using System.Text.Json;
using IntercalarioDigitale.Core.Pdf;
using IntercalarioDigitale.Core.Signatures;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>
/// Archivio locale delle pratiche: una cartella radice con <c>workflow.json</c> (utenti, pratiche, registro)
/// e una sottocartella <c>pratiche/&lt;id&gt;</c> per ogni intercalario (manifest, PDF, annotazioni).
/// </summary>
public sealed class PracticeLibrary
{
    private const string StateFileName = "workflow.json";
    private const string PracticesFolderName = "pratiche";
    private const string SignaturesFolderName = "firme";
    private const long MaxSignatureBytes = 2 * 1024 * 1024;

    public PracticeLibrary(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            throw new ArgumentException("Cartella radice obbligatoria", nameof(rootFolder));
        }

        RootFolder = rootFolder;
        Directory.CreateDirectory(rootFolder);
        StatePath = Path.Combine(rootFolder, StateFileName);

        var created = false;
        WorkflowState? state = null;
        if (File.Exists(StatePath))
        {
            try
            {
                state = WorkflowStore.Load(StatePath);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                File.Move(StatePath, StatePath + ".bak", overwrite: true);
            }
        }

        if (state is null)
        {
            state = DemoData.CreateBaseState();
            created = true;
        }

        State = state;
        Engine = new WorkflowEngine(State);

        var upgraded = !created && DemoData.UpgradeState(State);

        if (created)
        {
            DemoData.AddSamplePractices(this);
        }

        // Le firme di esempio servono per provare l'apposizione senza dover cercare dei PNG.
        var signed = DemoData.AddSampleSignatures(this);

        if (created || upgraded || signed)
        {
            Save();
        }
    }

    public string RootFolder { get; }
    public string StatePath { get; }
    public WorkflowState State { get; }
    public WorkflowEngine Engine { get; }
    public BinderRepository Binders { get; } = new();

    public string GetBinderPath(string practiceId) => Path.Combine(RootFolder, PracticesFolderName, practiceId);

    public void Save() => WorkflowStore.Save(StatePath, State);

    /// <summary>
    /// Crea una pratica con il suo intercalario (contropagina + un allegato).
    /// Se i documenti non vengono indicati ne viene generato uno di esempio.
    /// L'anno decide il prefisso dell'ID (di default l'anno corrente).
    /// Il livello decisionale (0 = si ferma al Reparto, 4 = C4 … 1 = C1) fissa il percorso di salita.
    /// </summary>
    public Practice CreatePractice(
        string name,
        string holderId,
        string? coverPdfPath = null,
        string? attachmentPdfPath = null,
        int? year = null,
        int decisionLevel = 0,
        string? introductionPath = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome documento obbligatorio", nameof(name));
        }

        Engine.GetUser(holderId);
        var route = Engine.BuildRoute(holderId, decisionLevel);

        var id = PracticeIdGenerator.Next(State.Practices.Select(p => p.Id), year ?? DateTime.Now.Year);
        var binderPath = GetBinderPath(id);
        var tempFolder = Path.Combine(Path.GetTempPath(), "gerarchia-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempFolder);

            var cover = coverPdfPath;
            if (string.IsNullOrWhiteSpace(cover))
            {
                cover = Path.Combine(tempFolder, "cover.pdf");
                File.WriteAllBytes(cover, SamplePdfFactory.Create("Contropagina", $"Pratica {id} · {name}", 3));
            }

            var attachment = attachmentPdfPath;
            if (string.IsNullOrWhiteSpace(attachment))
            {
                attachment = Path.Combine(tempFolder, "allegato.pdf");
                File.WriteAllBytes(attachment, SamplePdfFactory.Create(name, $"Allegato alla pratica {id}", 4));
            }

            // Ogni pratica ha la copertina d'introduzione (allegato 0), che si vede da sola, senza contropagina.
            var introduction = introductionPath;
            if (string.IsNullOrWhiteSpace(introduction))
            {
                introduction = Path.Combine(tempFolder, "copertina.pdf");
                File.WriteAllBytes(introduction, SamplePdfFactory.Create("Copertina", $"Pratica {id} · {name}", 1));
            }

            Binders.CreateNew(binderPath, cover, $"Contropagina {id}");
            Binders.SetIntroduction(binderPath, introduction, "Copertina");
            Binders.AddAttachment(binderPath, attachment, name);
        }
        finally
        {
            try
            {
                Directory.Delete(tempFolder, recursive: true);
            }
            catch (IOException)
            {
                // cartella temporanea: se resta lì non è un problema
            }
        }

        var practice = new Practice
        {
            Id = id,
            Name = name.Trim(),
            HolderId = holderId,
            DecisionLevel = decisionLevel,
            Route = route,
            RouteIndex = 0
        };
        State.Practices.Add(practice);
        Engine.AddLog($"Creata la pratica {id} · {practice.Name} (livello decisionale: {WorkflowText.LevelName(decisionLevel)})");
        Save();
        return practice;
    }

    /// <summary>Aggiunge un allegato (o un annesso, con <paramref name="parentId"/>) alla pratica; senza PDF ne genera uno di esempio.</summary>
    public AttachmentDocument AddAttachment(string practiceId, string title, string? filePath = null, string? parentId = null)
    {
        var binderPath = GetBinderPath(practiceId);
        return WithPdf(filePath, title, $"Allegato alla pratica {practiceId}", path => Binders.AddAttachment(binderPath, path, title, parentId));
    }

    /// <summary>Aggiunge la copertina d'introduzione (allegato 0) a una pratica che non ce l'ha; senza file ne genera una di esempio.</summary>
    public AttachmentDocument AddIntroduction(string practiceId, string? filePath = null)
    {
        var binderPath = GetBinderPath(practiceId);
        return WithPdf(filePath, "Copertina", $"Pratica {practiceId}", path => Binders.SetIntroduction(binderPath, path, "Copertina"));
    }

    /// <summary>Aggiunge un documento alla contropagina della pratica; senza PDF ne genera uno di esempio.</summary>
    public AttachmentDocument AddCoverAttachment(string practiceId, string title, string? filePath = null)
    {
        var binderPath = GetBinderPath(practiceId);
        return WithPdf(filePath, title, $"Contropagina della pratica {practiceId}", path => Binders.AddCoverAttachment(binderPath, path, title));
    }

    /// <summary>Solo chi ha in carico l'appunto può sostituire i suoi documenti.</summary>
    public bool CanReplace(string practiceId, string userId) => Engine.GetPractice(practiceId).HolderId == userId;

    /// <summary>
    /// Sostituisce un documento con una nuova versione (per esempio un Word modificato e ricaricato).
    /// Si conservano solo l'originale e la versione attuale; l'operazione resta nel registro dei movimenti.
    /// </summary>
    public ReplaceResult ReplaceDocument(string practiceId, string documentId, string newFilePath, string userId)
    {
        var user = Engine.GetUser(userId);
        if (!CanReplace(practiceId, userId))
        {
            throw new InvalidOperationException("Solo chi ha in carico l'appunto può sostituire i documenti.");
        }

        var binderPath = GetBinderPath(practiceId);
        var item = BinderLayout.Find(Binders.LoadManifest(binderPath), documentId)
            ?? throw new ArgumentException("Documento non trovato", nameof(documentId));

        var result = Binders.ReplaceDocument(binderPath, documentId, newFilePath, user.Name);

        Engine.AddLog($"{user.Name} ha sostituito «{item.Title}» ({item.Label}) nella pratica {practiceId}" +
            (result.OriginalPreserved ? " · originale conservato" : string.Empty));
        Save();
        return result;
    }

    // ---------- firme ----------

    public string SignaturesFolder => Path.Combine(RootFolder, SignaturesFolderName);

    /// <summary>Percorso della firma preimpostata dell'Ufficiale, se ne ha una.</summary>
    public string? GetSignaturePath(string userId)
    {
        var user = Engine.GetUser(userId);
        if (string.IsNullOrEmpty(user.SignatureFile))
        {
            return null;
        }

        var path = Path.Combine(RootFolder, user.SignatureFile);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Carica il PNG con la firma di un Capo Ufficio, Capo Reparto o Autorità di vertice.</summary>
    public void SetUserSignature(string userId, string pngPath)
    {
        var user = Engine.GetUser(userId);
        if (!WorkflowEngine.CanHaveSignature(user))
        {
            throw new InvalidOperationException($"{user.Role.DisplayName()} non ha una firma preimpostata.");
        }

        if (new FileInfo(pngPath).Length > MaxSignatureBytes)
        {
            throw new InvalidDataException("Il file della firma è troppo grande (massimo 2 MB).");
        }

        var bytes = File.ReadAllBytes(pngPath);
        if (!PngFile.IsPng(bytes))
        {
            throw new InvalidDataException("La firma deve essere un'immagine PNG.");
        }

        Directory.CreateDirectory(SignaturesFolder);
        var relative = Path.Combine(SignaturesFolderName, user.Id + ".png");
        File.WriteAllBytes(Path.Combine(RootFolder, relative), bytes);
        user.SignatureFile = relative;
        Save();
    }

    public void RemoveUserSignature(string userId)
    {
        var user = Engine.GetUser(userId);
        if (!string.IsNullOrEmpty(user.SignatureFile))
        {
            var path = Path.Combine(RootFolder, user.SignatureFile);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        user.SignatureFile = null;
        Save();
    }

    /// <summary>
    /// Appone un timbro (Concordato, Approvato, Visto). Se l'Ufficiale ha una firma e viene indicato dove metterla,
    /// la firma viene copiata nell'intercalario (<c>firme/</c>), così il timbro resta com'era anche se la firma cambia.
    /// </summary>
    public ValidationStamp ApplyValidation(string practiceId, string userId, ValidationKind kind, SignaturePlacement? placement = null)
    {
        var practice = Engine.GetPractice(practiceId);
        var user = Engine.GetUser(userId);

        // Prima del timbro: se la firma non si può copiare non si lascia un timbro a metà.
        string? copied = null;
        if (placement is not null)
        {
            if (!WorkflowEngine.CanHaveSignature(user))
            {
                throw new InvalidOperationException($"{user.Role.DisplayName()} non appone una firma.");
            }

            var source = GetSignaturePath(userId)
                ?? throw new InvalidOperationException($"{user.Name} non ha ancora caricato la firma.");
            if (!Engine.CanStamp(user, kind))
            {
                throw new InvalidOperationException($"{user.Role.DisplayName()} non può apporre «{kind.Title()}».");
            }

            var folder = Path.Combine(GetBinderPath(practiceId), SignaturesFolderName);
            Directory.CreateDirectory(folder);
            copied = Path.Combine(SignaturesFolderName, $"{Guid.NewGuid():N}.png");
            File.Copy(source, Path.Combine(GetBinderPath(practiceId), copied), overwrite: false);
        }

        try
        {
            var stamp = Engine.AddValidation(practiceId, userId, kind);
            if (copied is not null)
            {
                stamp.SignatureFile = copied;
                stamp.Placement = placement;
            }

            Save();
            return stamp;
        }
        catch
        {
            if (copied is not null)
            {
                var path = Path.Combine(GetBinderPath(practiceId), copied);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            throw;
        }
    }

    /// <summary>Percorso della firma copiata nell'intercalario per un timbro (vuoto se il timbro è senza firma).</summary>
    public string? GetStampSignaturePath(string practiceId, ValidationStamp stamp)
    {
        if (string.IsNullOrEmpty(stamp.SignatureFile))
        {
            return null;
        }

        var path = Path.Combine(GetBinderPath(practiceId), stamp.SignatureFile);
        return File.Exists(path) ? path : null;
    }

    private static AttachmentDocument WithPdf(string? pdfPath, string title, string subtitle, Func<string, AttachmentDocument> add)
    {
        if (!string.IsNullOrWhiteSpace(pdfPath))
        {
            return add(pdfPath);
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "gerarchia-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempFolder);
            var file = Path.Combine(tempFolder, "documento.pdf");
            File.WriteAllBytes(file, SamplePdfFactory.Create(title, subtitle, 2));
            return add(file);
        }
        finally
        {
            try
            {
                Directory.Delete(tempFolder, recursive: true);
            }
            catch (IOException)
            {
                // cartella temporanea: se resta lì non è un problema
            }
        }
    }
}
