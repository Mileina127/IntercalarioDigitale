using System.Text.RegularExpressions;
using IntercalarioDigitale.Core.Workflow;

namespace IntercalarioDigitale.Core.Assistant;

public static class ItalianDate
{
    private static readonly string[] Months = ["Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic"];

    /// <summary>Formato "25 Ott 2023, 10:15".</summary>
    public static string Format(DateTimeOffset value) =>
        $"{value.Day:00} {Months[value.Month - 1]} {value.Year}, {value.Hour:00}:{value.Minute:00}";
}

public sealed record AssistantContext(
    Practice Practice,
    IReadOnlyList<WorkflowUser> Users,
    string ScopeLabel,
    string AttachmentTitle,
    int AttachmentPageCount,
    int CoverAnnotatedPages,
    int AttachmentAnnotatedPages);

/// <summary>
/// Assistente IA simulato: risponde a poche domande leggendo i dati della pratica.
/// Nessun modello è collegato; serve a provare l'interfaccia del pannello.
/// </summary>
public static class SimulatedAssistant
{
    public static string Greeting(string attachmentTitle) =>
        $"Salve, ho esaminato l'allegato «{attachmentTitle}». Quali punti vuoi analizzare? Posso controllare concordanze, approvazioni e altro.";

    public static IReadOnlyList<string> Suggestions { get; } =
    [
        "Controlla le concordanze",
        "Verifica le approvazioni",
        "Riassumi l'allegato",
        "Quali pagine ho annotato?"
    ];

    public static string Reply(string question, AssistantContext context)
    {
        var text = question.ToLowerInvariant();
        var stamps = context.Practice.Validations;

        string Who(string userId)
        {
            var user = context.Users.FirstOrDefault(u => u.Id == userId);
            return user is null ? userId : user.Signature;
        }

        if (Regex.IsMatch(text, "concord"))
        {
            var concordati = stamps.Where(v => v.Kind == ValidationKind.Concordato).ToList();
            if (concordati.Count == 0)
            {
                return $"Ambito «{context.ScopeLabel}»: non risulta alcun concordato. Servono almeno i Capi Sezione e Capi Ufficio coinvolti.";
            }

            var approvato = stamps.Any(v => v.Kind == ValidationKind.Approvato)
                ? "È presente anche l'approvazione finale."
                : "Manca ancora l'approvazione del Capo Reparto.";
            return $"Ambito «{context.ScopeLabel}»: risultano {concordati.Count} concordati ({string.Join(", ", concordati.Select(v => Who(v.UserId)))}). {approvato}";
        }

        if (Regex.IsMatch(text, "approv"))
        {
            var approvati = stamps.Where(v => v.Kind == ValidationKind.Approvato).ToList();
            return approvati.Count == 0
                ? "Nessuna approvazione registrata: la pratica è ancora in fase di concordanza."
                : "Approvato da " + string.Join("; ", approvati.Select(v => $"{Who(v.UserId)} il {ItalianDate.Format(v.At)}")) + ".";
        }

        if (Regex.IsMatch(text, "riassum|sintes|di cosa"))
        {
            return $"L'allegato «{context.AttachmentTitle}» ha {context.AttachmentPageCount} pagine. " +
                   "Questo assistente è simulato e non legge il contenuto del PDF: l'analisi vera arriverà con il collegamento a un modello.";
        }

        if (Regex.IsMatch(text, "annot|segn|appunt"))
        {
            var total = context.CoverAnnotatedPages + context.AttachmentAnnotatedPages;
            return total == 0
                ? "Non hai ancora annotato nessuna pagina. Attiva «Annotazioni» e disegna sulla pagina."
                : $"Hai annotato {context.CoverAnnotatedPages} pagine della contropagina e {context.AttachmentAnnotatedPages} dell'allegato.";
        }

        if (Regex.IsMatch(text, "banca|dati"))
        {
            return "La lettura della banca dati non è collegata. Qui verrebbero confrontati i dati dell'allegato con quelli archiviati.";
        }

        return $"Ambito «{context.ScopeLabel}». Posso rispondere su concordanze, approvazioni, riepilogo dell'allegato e annotazioni.";
    }
}
