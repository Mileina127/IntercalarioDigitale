using IntercalarioDigitale.Core.Signatures;
using IntercalarioDigitale.Core.Word;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>Utenti, Reparti e pratiche di esempio per la prima apertura dell'applicazione.</summary>
public static class DemoData
{
    /// <summary>Le pratiche di esempio usano sempre l'anno delle slide (ID "2.026_n").</summary>
    public const int DemoYear = 2026;

    public static WorkflowState CreateBaseState()
    {
        var state = new WorkflowState
        {
            Users =
            [
                User("verdi", "G. Verdi", Role.CapoSezione, "Sezione Amministrazione", "Amm.", superiorId: "rossi"),
                User("bianchi", "D. Bianchi", Role.CapoSezione, "Sezione Legale", "Legal", superiorId: "colombo"),
                User("rossi", "L. Rossi", Role.CapoUfficio, "Ufficio Controllo Qualità", "QC", superiorId: "moretti"),
                User("colombo", "P. Colombo", Role.CapoUfficio, "Ufficio Pianificazione", "Pian.", superiorId: "gallo"),
                User("moretti", "A. Moretti", Role.CapoReparto, "Reparto Operazioni", "Resp. Area"),
                User("gallo", "R. Gallo", Role.CapoReparto, "Reparto Logistico", "Log.")
            ],
            Departments =
            [
                new Department { Id = "op", Name = "Reparto Operazioni", HeadId = "moretti", SectionHeadId = "verdi" },
                new Department { Id = "log", Name = "Reparto Logistico", HeadId = "gallo", SectionHeadId = "bianchi" }
            ]
        };

        UpgradeState(state);
        return state;
    }

    /// <summary>
    /// Linea di vertice: per ogni livello decisionale (C1 più elevato … C4 meno elevato) un Capo Sezione di vertice,
    /// un Capo Ufficio di vertice e l'Autorità. Id, nome, livello.
    /// </summary>
    private static readonly (int Level, string SectionId, string SectionName, string OfficeId, string OfficeName, string AuthorityId, string AuthorityName)[] Vertice =
    [
        (4, "sezv4", "C. Esposito", "uffv4", "A. Ricci", "autv4", "R. Bruno"),
        (3, "sezv3", "T. Conti", "uffv3", "N. De Luca", "autv3", "V. Greco"),
        (2, "sezv2", "G. Costa", "uffv2", "L. Marino", "autv2", "S. Ferri"),
        (1, "sezv1", "M. Fontana", "uffv1", "E. Barbieri", "neri", "F. Neri")
    ];

    /// <summary>
    /// Porta lo stato salvato all'ultima versione: collega Sezione → Ufficio → Reparto, aggiunge la linea di vertice
    /// (C4…C1) se manca e assegna il livello alle Autorità già presenti. Si può ripetere senza effetti.
    /// </summary>
    public static bool UpgradeState(WorkflowState state)
    {
        var changed = false;
        WorkflowUser? Find(string id) => state.Users.FirstOrDefault(u => u.Id == id);

        foreach (var (id, superior) in new[] { ("verdi", "rossi"), ("rossi", "moretti"), ("bianchi", "colombo"), ("colombo", "gallo") })
        {
            var user = Find(id);
            if (user is not null && user.SuperiorId is null && Find(superior) is not null)
            {
                user.SuperiorId = superior;
                changed = true;
            }
        }

        // L'Autorità che c'era prima della linea di vertice diventa C1.
        var legacy = Find("neri");
        if (legacy is not null && legacy.Role == Role.AutoritaDiVertice && legacy.Level == 0)
        {
            legacy.Level = 1;
            changed = true;
        }

        foreach (var (level, sectionId, sectionName, officeId, officeName, authorityId, authorityName) in Vertice)
        {
            var name = WorkflowText.LevelName(level);
            if (Find(sectionId) is null)
            {
                state.Users.Add(User(sectionId, sectionName, Role.CapoSezioneDiVertice, $"Sezione di vertice {name}", $"Sez. {name}", officeId, level));
                changed = true;
            }

            if (Find(officeId) is null)
            {
                state.Users.Add(User(officeId, officeName, Role.CapoUfficioDiVertice, $"Ufficio di vertice {name}", $"Uff. {name}", authorityId, level));
                changed = true;
            }

            if (Find(authorityId) is null)
            {
                state.Users.Add(User(authorityId, authorityName, Role.AutoritaDiVertice, $"Autorità di vertice {name}", name, null, level));
                changed = true;
            }
        }

        if (state.SchemaVersion < WorkflowState.CurrentVersion)
        {
            state.SchemaVersion = WorkflowState.CurrentVersion;
            changed = true;
        }

        return changed;
    }

    public static void AddSamplePractices(PracticeLibrary library)
    {
        // Ogni pratica parte dal Capo Sezione, che sceglie il livello decisionale (0 = si ferma al Reparto, 4 = C4 … 1 = C1).
        library.CreatePractice("Fattura Fornitore Alpha", "verdi", year: DemoYear, decisionLevel: 4);

        // La copertina d'introduzione di questa pratica è un Word (si legge in sola lettura).
        var p2 = WithTempFile(
            "copertina.docx",
            SampleWordFactory.Create(
                "Copertina",
                "Contratto Collaborazione Beta",
                "Introduzione alla pratica: contratto di collaborazione con il fornitore Beta.",
                "Si sottopone alla firma l'accordo allegato, con le condizioni economiche."),
            path => library.CreatePractice("Contratto Collaborazione Beta", "verdi", year: DemoYear, decisionLevel: 0, introductionPath: path));
        library.AddAttachment(p2.Id, "Condizioni economiche", parentId: FirstAttachmentId(library, p2.Id));
        library.Engine.Forward(p2.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));

        // Un allegato Word già modificato una volta: mostra la versione attuale e l'originale conservato.
        var draft = WithTempFile(
            "bozza.docx",
            SampleWordFactory.Create(
                "Bozza di contratto",
                "Versione di partenza",
                "Le parti convengono quanto segue.",
                "Articolo 1. Oggetto della collaborazione: da definire."),
            path => library.AddAttachment(p2.Id, "Bozza di contratto (Word)", path));
        WithTempFile(
            "bozza-modificata.docx",
            SampleWordFactory.Create(
                "Bozza di contratto",
                "Versione modificata da L. Rossi",
                "Le parti convengono quanto segue.",
                "Articolo 1. Oggetto della collaborazione: servizi di consulenza amministrativa.",
                "Articolo 2. Durata: dodici mesi, rinnovabile."),
            path => library.ReplaceDocument(p2.Id, draft.Id, path, "rossi"));
        p2.Validations.Add(Stamp(ValidationKind.Concordato, "verdi", 2023, 10, 25, 10, 15));

        var p3 = library.CreatePractice("Verbale Consiglio Amministrazione", "verdi", year: DemoYear, decisionLevel: 2);
        library.Engine.Forward(p3.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        library.Engine.Forward(p3.Id, "rossi", new ForwardTarget(TransferMode.Forward, "moretti"));
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "verdi", 2023, 10, 25, 10, 15));
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "rossi", 2023, 10, 28, 14, 30));
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "bianchi", 2023, 10, 30, 9, 0));
        p3.Validations.Add(Stamp(ValidationKind.Approvato, "moretti", 2023, 11, 1, 16, 30));
        p3.Validations.Add(Stamp(ValidationKind.Visto, "neri", 2023, 11, 2, 11, 0));

        // Documenti di esempio per vedere le colonne di navigazione: allegati alla contropagina,
        // altri allegati e gli annessi del primo allegato (L1.1, L1.2).
        library.AddCoverAttachment(p3.Id, "Convocazione");
        library.AddCoverAttachment(p3.Id, "Ordine del giorno");
        var firstAttachment = FirstAttachmentId(library, p3.Id);
        library.AddAttachment(p3.Id, "Planimetria sala", parentId: firstAttachment);
        library.AddAttachment(p3.Id, "Tabella riepilogativa", parentId: firstAttachment);
        library.AddAttachment(p3.Id, "Delibera");
        library.AddAttachment(p3.Id, "Parere legale");

        library.CreatePractice("Nota Esigenze Addestrative", "verdi", year: DemoYear, decisionLevel: 3);

        var p5 = library.CreatePractice("Relazione Attività Trimestrale", "verdi", year: DemoYear, decisionLevel: 1);
        library.Engine.Forward(p5.Id, "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));
        library.Engine.Forward(p5.Id, "rossi", new ForwardTarget(TransferMode.Forward, "moretti"));

        library.State.Log.Clear();
        library.Engine.AddLog("Pratica 2.026_3 inoltrata da L. Rossi ad A. Moretti");
    }

    /// <summary>Genera una firma di esempio per chi può averne una e non l'ha ancora caricata. Restituisce true se ha cambiato qualcosa.</summary>
    public static bool AddSampleSignatures(PracticeLibrary library)
    {
        var changed = false;
        foreach (var user in library.State.Users.Where(WorkflowEngine.CanHaveSignature))
        {
            if (library.GetSignaturePath(user.Id) is not null)
            {
                continue;
            }

            Directory.CreateDirectory(library.SignaturesFolder);
            var relative = Path.Combine("firme", user.Id + ".png");
            File.WriteAllBytes(Path.Combine(library.RootFolder, relative), SampleSignatureFactory.Create(user.Name));
            user.SignatureFile = relative;
            changed = true;
        }

        return changed;
    }

    private static T WithTempFile<T>(string fileName, byte[] content, Func<string, T> use)
    {
        var folder = Path.Combine(Path.GetTempPath(), "gerarchia-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, content);
            return use(path);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // cartella temporanea: se resta lì non è un problema
            }
        }
    }

    private static string FirstAttachmentId(PracticeLibrary library, string practiceId) =>
        library.Binders.LoadManifest(library.GetBinderPath(practiceId)).Attachments[0].Id;

    private static WorkflowUser User(
        string id, string name, Role role, string unit, string shortUnit, string? superiorId = null, int level = 0) => new()
    {
        Id = id,
        Name = name,
        Role = role,
        Unit = unit,
        ShortUnit = shortUnit,
        SuperiorId = superiorId,
        Level = level
    };

    private static ValidationStamp Stamp(ValidationKind kind, string userId, int year, int month, int day, int hour, int minute) => new()
    {
        Kind = kind,
        UserId = userId,
        At = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
    };
}
