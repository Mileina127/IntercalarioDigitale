using System.Text.Json.Serialization;

namespace IntercalarioDigitale.Core.Workflow;

public enum Role
{
    CapoSezione = 1,
    CapoUfficio = 2,
    CapoReparto = 3,
    AutoritaDiVertice = 4
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
        _ => role.ToString()
    };

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

    /// <summary>Solo Capi Ufficio: delega a inoltrare all'Autorità di vertice.</summary>
    public bool CanForwardToVertice { get; set; }

    [JsonIgnore]
    public string Label => $"{Name} · {this.Role.DisplayName()}";

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

public sealed class ValidationStamp
{
    public ValidationKind Kind { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

public sealed class Practice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HolderId { get; set; } = string.Empty;
    public string? FromId { get; set; }
    public PracticeStatus Status { get; set; } = PracticeStatus.InLavorazione;
    public List<ValidationStamp> Validations { get; set; } = [];
}

public sealed class MovementLogEntry
{
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class WorkflowState
{
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
