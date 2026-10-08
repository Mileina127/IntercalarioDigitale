using System.Text.Json;
using IntercalarioDigitale.Core.Pdf;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>
/// Archivio locale delle pratiche: una cartella radice con <c>workflow.json</c> (utenti, pratiche, registro)
/// e una sottocartella <c>pratiche/&lt;id&gt;</c> per ogni intercalario (manifest, PDF, annotazioni).
/// </summary>
public sealed class PracticeLibrary
{
    private const string StateFileName = "workflow.json";
    private const string PracticesFolderName = "pratiche";

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

        if (created)
        {
            DemoData.AddSamplePractices(this);
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
    /// Se i PDF non vengono indicati ne viene generato uno di esempio.
    /// L'anno decide il prefisso dell'ID (di default l'anno corrente).
    /// </summary>
    public Practice CreatePractice(string name, string holderId, string? coverPdfPath = null, string? attachmentPdfPath = null, int? year = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome documento obbligatorio", nameof(name));
        }

        Engine.GetUser(holderId);

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

            Binders.CreateNew(binderPath, cover, $"Contropagina {id}");
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

        var practice = new Practice { Id = id, Name = name.Trim(), HolderId = holderId };
        State.Practices.Add(practice);
        Engine.AddLog($"Creata la pratica {id} · {practice.Name}");
        Save();
        return practice;
    }
}
