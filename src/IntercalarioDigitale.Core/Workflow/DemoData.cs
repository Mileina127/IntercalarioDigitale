namespace IntercalarioDigitale.Core.Workflow;

/// <summary>Utenti, Reparti e pratiche di esempio per la prima apertura dell'applicazione.</summary>
public static class DemoData
{
    /// <summary>Le pratiche di esempio usano sempre l'anno delle slide (ID "2.026_n").</summary>
    public const int DemoYear = 2026;

    public static WorkflowState CreateBaseState() => new()
    {
        Users =
        [
            User("verdi", "G. Verdi", Role.CapoSezione, "Sezione Amministrazione", "Amm."),
            User("bianchi", "D. Bianchi", Role.CapoSezione, "Sezione Legale", "Legal"),
            User("rossi", "L. Rossi", Role.CapoUfficio, "Ufficio Controllo Qualità", "QC"),
            User("colombo", "P. Colombo", Role.CapoUfficio, "Ufficio Pianificazione", "Pian."),
            User("moretti", "A. Moretti", Role.CapoReparto, "Reparto Operazioni", "Resp. Area"),
            User("gallo", "R. Gallo", Role.CapoReparto, "Reparto Logistico", "Log."),
            User("neri", "F. Neri", Role.AutoritaDiVertice, "Autorità di vertice", "Vertice")
        ],
        Departments =
        [
            new Department { Id = "op", Name = "Reparto Operazioni", HeadId = "moretti", SectionHeadId = "verdi" },
            new Department { Id = "log", Name = "Reparto Logistico", HeadId = "gallo", SectionHeadId = "bianchi" }
        ]
    };

    public static void AddSamplePractices(PracticeLibrary library)
    {
        library.CreatePractice("Fattura Fornitore Alpha", "verdi", year: DemoYear);

        var p2 = library.CreatePractice("Contratto Collaborazione Beta", "rossi", year: DemoYear);
        p2.FromId = "verdi";
        p2.Validations.Add(Stamp(ValidationKind.Concordato, "verdi", 2023, 10, 25, 10, 15));

        var p3 = library.CreatePractice("Verbale Consiglio Amministrazione", "moretti", year: DemoYear);
        p3.FromId = "rossi";
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "verdi", 2023, 10, 25, 10, 15));
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "rossi", 2023, 10, 28, 14, 30));
        p3.Validations.Add(Stamp(ValidationKind.Concordato, "bianchi", 2023, 10, 30, 9, 0));
        p3.Validations.Add(Stamp(ValidationKind.Approvato, "moretti", 2023, 11, 1, 16, 30));
        p3.Validations.Add(Stamp(ValidationKind.Visto, "neri", 2023, 11, 2, 11, 0));

        library.CreatePractice("Nota Esigenze Addestrative", "verdi", year: DemoYear);

        var p5 = library.CreatePractice("Relazione Attività Trimestrale", "moretti", year: DemoYear);
        p5.FromId = "colombo";

        library.State.Log.Clear();
        library.Engine.AddLog("Pratica 2.026_3 inoltrata da L. Rossi ad A. Moretti");
    }

    private static WorkflowUser User(string id, string name, Role role, string unit, string shortUnit) => new()
    {
        Id = id,
        Name = name,
        Role = role,
        Unit = unit,
        ShortUnit = shortUnit
    };

    private static ValidationStamp Stamp(ValidationKind kind, string userId, int year, int month, int day, int hour, int minute) => new()
    {
        Kind = kind,
        UserId = userId,
        At = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
    };
}
