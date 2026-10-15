using System.Text.Json.Serialization;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>
/// Gli ultimi due ruoli appartengono alla linea di vertice: per ogni livello decisionale (C4…C1)
/// ci sono un Capo Sezione, un Capo Ufficio e l'Autorità di vertice.
/// I valori numerici sono salvati nei file: non vanno cambiati.
/// </summary>
public enum Role
{
    CapoSezione = 1,
    CapoUfficio = 2,
    CapoReparto = 3,
    AutoritaDiVertice = 4,
    CapoSezioneDiVertice = 5,
    CapoUfficioDiVertice = 6
}

public enum PracticeStatus
{
    InLavorazione,
    InAttesaDiPresaInCarico,
    Restituito
}

public enum ValidationKind
{
    Concordato,
    Approvato,
    Visto
}

public enum TransferMode
{
    /// <summary>Inoltro lungo la via gerarchica.</summary>
    Forward,

    /// <summary>Invio in tramite telematico (solo Capi Reparto) verso il Capo Sezione di un altro Reparto.</summary>
    Tramite,

    /// <summary>Restituzione al mittente precedente.</summary>
    Return
}

public static class WorkflowText
{
    public static string DisplayName(this Role role) => role switch
    {
        Role.CapoSezione => "Capo Sezione",
        Role.CapoUfficio => "Capo Ufficio",
        Role.CapoReparto => "Capo Reparto",
        Role.AutoritaDiVertice => "Autorità di vertice",
        Role.CapoSezioneDiVertice => "Capo Sezione di vertice",
        Role.CapoUfficioDiVertice => "Capo Ufficio di vertice",
        _ => role.ToString()
    };

    /// <summary>Livello decisionale: 0 = si ferma al Reparto, poi C4 (il meno elevato) … C1 (il più elevato).</summary>
    public static string LevelName(int level) => level is >= 1 and <= 4 ? "C" + level : "Reparto";

    public static string DisplayName(this PracticeStatus status) => status switch
    {
        PracticeStatus.InLavorazione => "In lavorazione",
        PracticeStatus.InAttesaDiPresaInCarico => "In attesa di presa in carico",
        PracticeStatus.Restituito => "Restituito",
        _ => status.ToString()
    };

    public static string Title(this ValidationKind kind) => kind switch
    {
        ValidationKind.Concordato => "Concordato",
        ValidationKind.Approvato => "Approvato",
        ValidationKind.Visto => "Aggiuntivo",
        _ => kind.ToString()
    };

    public static string Badge(this ValidationKind kind) => kind switch
    {
        ValidationKind.Concordato => "CONCORDATO",
        ValidationKind.Approvato => "APPROVATO",
        ValidationKind.Visto => "VISTO",
        _ => kind.ToString().ToUpperInvariant()
    };
}

public sealed class WorkflowUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Role Role { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string ShortUnit { get; set; } = string.Empty;

    /// <summary>L'Ufficiale è "in licenza": gli appunti a lui destinati vanno al delegato o restano in attesa.</summary>
    public bool OnLeave { get; set; }

    /// <summary>Parigrado che sostituisce l'Ufficiale durante la licenza.</summary>
    public string? DelegateId { get; set; }

    /// <summary>Non più usata: la linea gerarchica è fissa e non si salta nessun livello. Resta per leggere i vecchi file.</summary>
    public bool CanForwardToVertice { get; set; }

    /// <summary>Firma (PNG) preimpostata dell'Ufficiale, relativa alla cartella dell'archivio (per esempio <c>firme/rossi.png</c>).</summary>
    public string? SignatureFile { get; set; }

    /// <summary>Superiore diretto lungo la linea di Reparto (Sezione → Ufficio → Reparto); vuoto per il Capo Reparto e per il vertice.</summary>
    public string? SuperiorId { get; set; }

    /// <summary>Solo linea di vertice: 4 = C4 (meno elevato) … 1 = C1 (più elevato). Per gli altri è 0.</summary>
    public int Level { get; set; }

    [JsonIgnore]
    public string RoleName => Level > 0 ? $"{Role.DisplayName()} C{Level}" : Role.DisplayName();

    [JsonIgnore]
    public string Label => $"{Name} · {RoleName}";

    [JsonIgnore]
    public string Signature => $"{Name} ({ShortUnit})";

    [JsonIgnore]
    public string Initials => string.Concat(
        Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0])));
}

/// <summary>Articolazione (Reparto) a cui si può inviare un appunto in tramite telematico.</summary>
public sealed class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HeadId { get; set; } = string.Empty;
    public string SectionHeadId { get; set; } = string.Empty;
}

/// <summary>
/// Dove sta la firma sul documento. Le misure sono frazioni della pagina (0…1), così valgono con qualunque zoom:
/// <c>X</c> e <c>Y</c> sono l'angolo in alto a sinistra, <c>Width</c> la larghezza rispetto alla pagina.
/// </summary>
public sealed class SignaturePlacement
{
    public string DocumentId { get; set; } = string.Empty;
    public int PageIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
}

public sealed class ValidationStamp
{
    public ValidationKind Kind { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }

    /// <summary>Firma (PNG) apposta insieme al timbro, copiata nell'intercalario: percorso relativo, per esempio <c>firme/xxx.png</c>.</summary>
    public string? SignatureFile { get; set; }

    /// <summary>Posizione della firma sul documento (vuota se il timbro è senza firma).</summary>
    public SignaturePlacement? Placement { get; set; }
}

public sealed class Practice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HolderId { get; set; } = string.Empty;
    public string? FromId { get; set; }
    public PracticeStatus Status { get; set; } = PracticeStatus.InLavorazione;
    public List<ValidationStamp> Validations { get; set; } = [];

    /// <summary>Livello decisionale scelto da chi carica la pratica: 0 = si ferma al Reparto, 4 = C4 … 1 = C1.</summary>
    public int DecisionLevel { get; set; }

    /// <summary>Percorso di salita fissato alla creazione: gli ID degli utenti nell'ordine in cui l'appunto li attraversa.</summary>
    public List<string> Route { get; set; } = [];

    /// <summary>Posizione nel percorso di chi ha (o dovrebbe avere) l'appunto.</summary>
    public int RouteIndex { get; set; }
}

public enum RouteStepState
{
    Done,
    Current,
    Upcoming
}

public sealed record RouteStep(WorkflowUser User, RouteStepState State, string Stage);

public sealed class MovementLogEntry
{
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class WorkflowState
{
    /// <summary>Versione del formato: 2 = linea gerarchica fissa e livelli di vertice.</summary>
    public const int CurrentVersion = 2;

    public int SchemaVersion { get; set; } = CurrentVersion;

    public List<WorkflowUser> Users { get; set; } = [];
    public List<Department> Departments { get; set; } = [];
    public List<Practice> Practices { get; set; } = [];
    public List<MovementLogEntry> Log { get; set; } = [];
}

public sealed record ForwardTarget(TransferMode Mode, string RecipientId, string? DepartmentId = null);

public sealed record TransferResult(
    string PracticeId,
    string RecipientId,
    string FinalHolderId,
    PracticeStatus Status,
    string Message);
