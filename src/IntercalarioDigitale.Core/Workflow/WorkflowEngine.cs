namespace IntercalarioDigitale.Core.Workflow;

/// <summary>
/// Regole di flusso degli appunti (vedi "Struttura funzionale dell'applicativo").
/// La via gerarchica è fissa e si decide alla creazione della pratica:
/// Capo Sezione → Capo Ufficio → Capo Reparto, poi, per ogni livello decisionale da C4 (il meno elevato)
/// fino a quello scelto, Capo Sezione di vertice → Capo Ufficio di vertice → Autorità di vertice.
/// Restano la licenza con sostituto parigrado e il tramite telematico riservato ai Capi Reparto.
/// </summary>
public sealed class WorkflowEngine
{
    private const int MaxLogEntries = 50;

    private readonly WorkflowState _state;
    private readonly Func<DateTimeOffset> _clock;

    public WorkflowEngine(WorkflowState state, Func<DateTimeOffset>? clock = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public WorkflowState State => _state;

    public WorkflowUser GetUser(string userId) =>
        _state.Users.FirstOrDefault(u => u.Id == userId)
        ?? throw new KeyNotFoundException($"Utente non trovato: {userId}");

    public Practice GetPractice(string practiceId) =>
        _state.Practices.FirstOrDefault(p => p.Id == practiceId)
        ?? throw new KeyNotFoundException($"Pratica non trovata: {practiceId}");

    // ---------- via gerarchica ----------

    public const int LowestLevel = 4;
    public const int HighestLevel = 1;

    private static bool IsRepartoLine(Role role) => role is Role.CapoSezione or Role.CapoUfficio or Role.CapoReparto;

    private WorkflowUser? Superior(WorkflowUser user) =>
        user.SuperiorId is null ? null : _state.Users.FirstOrDefault(u => u.Id == user.SuperiorId);

    private WorkflowUser FindVertice(Role role, int level) =>
        _state.Users.FirstOrDefault(u => u.Role == role && u.Level == level)
        ?? throw new InvalidOperationException($"Nella linea di vertice manca {role.DisplayName()} C{level}.");

    /// <summary>
    /// Percorso di salita di un appunto che parte da <paramref name="startUserId"/> (di norma il Capo Sezione):
    /// la linea di Reparto fino al Capo Reparto, poi le terne di vertice da C4 fino a <paramref name="decisionLevel"/>.
    /// Con livello 0 l'appunto si ferma al Capo Reparto.
    /// </summary>
    public List<string> BuildRoute(string startUserId, int decisionLevel)
    {
        if (decisionLevel is < 0 or > LowestLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionLevel), "Il livello decisionale va da 1 (C1) a 4 (C4); 0 = si ferma al Reparto.");
        }

        var start = GetUser(startUserId);
        if (!IsRepartoLine(start.Role))
        {
            throw new InvalidOperationException("Un appunto parte dalla linea di Reparto (Capo Sezione, Capo Ufficio o Capo Reparto).");
        }

        var route = new List<string>();
        var current = start;
        while (current is not null && !route.Contains(current.Id))
        {
            route.Add(current.Id);
            if (current.Role == Role.CapoReparto)
            {
                break;
            }

            current = Superior(current);
        }

        for (var level = LowestLevel; decisionLevel > 0 && level >= decisionLevel; level--)
        {
            route.Add(FindVertice(Role.CapoSezioneDiVertice, level).Id);
            route.Add(FindVertice(Role.CapoUfficioDiVertice, level).Id);
            route.Add(FindVertice(Role.AutoritaDiVertice, level).Id);
        }

        return route;
    }

    /// <summary>I vecchi appunti (senza percorso) lo ricevono la prima volta che servono.</summary>
    private void EnsureRoute(Practice practice)
    {
        if (practice.Route.Count > 0)
        {
            return;
        }

        var holder = GetUser(practice.HolderId);
        practice.Route = IsRepartoLine(holder.Role)
            ? BuildRoute(holder.Id, practice.DecisionLevel)
            : [holder.Id];
        practice.RouteIndex = 0;
    }

    /// <summary>Le tappe del percorso, con quelle già fatte, quella attuale e quelle da fare.</summary>
    public IReadOnlyList<RouteStep> GetRoute(Practice practice)
    {
        EnsureRoute(practice);
        var steps = new List<RouteStep>();
        for (var i = 0; i < practice.Route.Count; i++)
        {
            var user = GetUser(practice.Route[i]);
            var state = i < practice.RouteIndex ? RouteStepState.Done
                : i == practice.RouteIndex ? RouteStepState.Current
                : RouteStepState.Upcoming;
            steps.Add(new RouteStep(user, state, WorkflowText.LevelName(user.Level)));
        }

        return steps;
    }

    // ---------- destinatari ----------

    public IReadOnlyList<ForwardTarget> GetForwardTargets(Practice practice, string actorId)
    {
        var actor = GetUser(actorId);
        EnsureRoute(practice);
        var targets = new List<ForwardTarget>();

        var next = practice.RouteIndex + 1;
        if (next < practice.Route.Count)
        {
            targets.Add(new ForwardTarget(TransferMode.Forward, practice.Route[next]));
        }

        if (actor.Role == Role.CapoReparto)
        {
            foreach (var department in _state.Departments.Where(d => d.HeadId != actor.Id))
            {
                targets.Add(new ForwardTarget(TransferMode.Tramite, department.SectionHeadId, department.Id));
            }
        }

        return targets;
    }

    public string Describe(ForwardTarget target)
    {
        var recipient = GetUser(target.RecipientId);
        if (target.Mode == TransferMode.Tramite && target.DepartmentId is not null)
        {
            var department = _state.Departments.FirstOrDefault(d => d.Id == target.DepartmentId);
            var departmentName = department?.Name ?? target.DepartmentId;
            return $"{departmentName}: tramite telematico a {recipient.Name} ({recipient.Role.DisplayName()})";
        }

        return $"{recipient.Name} · {recipient.RoleName}, {recipient.Unit}";
    }

    public string? DescribeLeave(string userId)
    {
        var user = GetUser(userId);
        if (!user.OnLeave)
        {
            return null;
        }

        var delegateUser = FindDelegate(user);
        return delegateUser is null
            ? "In licenza: l'appunto resterà in attesa di presa in carico"
            : $"In licenza: l'appunto andrà al delegato {delegateUser.Name}";
    }

    // ---------- spostamenti ----------

    public bool CanForward(Practice practice, string actorId) =>
        practice.HolderId == actorId && GetForwardTargets(practice, actorId).Count > 0;

    public bool CanReturn(Practice practice, string actorId) =>
        practice.HolderId == actorId && practice.FromId is not null;

    public TransferResult Forward(string practiceId, string actorId, ForwardTarget target, string? note = null)
    {
        var practice = GetPractice(practiceId);
        var actor = GetUser(actorId);
        EnsureHolder(practice, actor);

        if (target.Mode == TransferMode.Return || !GetForwardTargets(practice, actorId).Contains(target))
        {
            throw new InvalidOperationException("Destinatario non consentito: la via gerarchica è fissa.");
        }

        var result = Deliver(practice, actor, GetUser(target.RecipientId), target.Mode, note);
        AdvanceRoute(practice, target);
        return result;
    }

    public TransferResult Return(string practiceId, string actorId, string? note = null)
    {
        var practice = GetPractice(practiceId);
        var actor = GetUser(actorId);
        EnsureHolder(practice, actor);

        if (practice.FromId is null)
        {
            throw new InvalidOperationException("Nessun mittente precedente a cui restituire l'appunto.");
        }

        var recipient = GetUser(practice.FromId);
        var result = Deliver(practice, actor, recipient, TransferMode.Return, note);

        // Si torna alla tappa di chi aveva mandato l'appunto (o di chi lo sostituiva).
        EnsureRoute(practice);
        var back = practice.RouteIndex > 0 ? practice.Route.LastIndexOf(recipient.Id, practice.RouteIndex - 1) : -1;
        practice.RouteIndex = back >= 0 ? back : Math.Max(0, practice.RouteIndex - 1);
        return result;
    }

    private void AdvanceRoute(Practice practice, ForwardTarget target)
    {
        EnsureRoute(practice);
        var here = practice.RouteIndex;

        if (target.Mode == TransferMode.Tramite)
        {
            // L'appunto attraversa la linea di Reparto dell'altra articolazione e poi riprende la salita al vertice.
            var chain = new List<string>();
            var current = GetUser(target.RecipientId);
            while (current is not null && !chain.Contains(current.Id))
            {
                chain.Add(current.Id);
                if (current.Role == Role.CapoReparto)
                {
                    break;
                }

                current = Superior(current);
            }

            var alreadyThere = practice.Route.Count > here + 1 && practice.Route[here + 1] == chain[0];
            if (!alreadyThere)
            {
                var tail = practice.Route.Skip(here + 1).ToList();
                practice.Route = practice.Route.Take(here + 1).Concat(chain).Concat(tail).ToList();
            }
        }

        practice.RouteIndex = here + 1;
    }

    private TransferResult Deliver(Practice practice, WorkflowUser actor, WorkflowUser recipient, TransferMode mode, string? note)
    {
        var holderId = recipient.Id;
        var status = mode == TransferMode.Return ? PracticeStatus.Restituito : PracticeStatus.InLavorazione;
        var extra = string.Empty;

        if (recipient.OnLeave)
        {
            var delegateUser = FindDelegate(recipient);
            if (delegateUser is not null)
            {
                holderId = delegateUser.Id;
                status = PracticeStatus.InLavorazione;
                extra = $" ({recipient.Name} è in licenza: inoltrato all'Ufficiale delegato {delegateUser.Name})";
            }
            else
            {
                status = PracticeStatus.InAttesaDiPresaInCarico;
                extra = $" ({recipient.Name} è in licenza: in attesa di presa in carico)";
            }
        }

        practice.FromId = actor.Id;
        practice.HolderId = holderId;
        practice.Status = status;

        var verb = mode switch
        {
            TransferMode.Return => "restituito a",
            TransferMode.Tramite => "inviato in tramite telematico a",
            _ => "inoltrato a"
        };

        var message = $"{practice.Id} {verb} {recipient.Name}{extra}";
        var logText = string.IsNullOrWhiteSpace(note)
            ? $"{actor.Name}: {message}"
            : $"{actor.Name}: {message} · «{note.Trim()}»";
        AddLog(logText);

        return new TransferResult(practice.Id, recipient.Id, holderId, status, message);
    }

    private WorkflowUser? FindDelegate(WorkflowUser user) =>
        user.DelegateId is null ? null : _state.Users.FirstOrDefault(u => u.Id == user.DelegateId);

    private static void EnsureHolder(Practice practice, WorkflowUser actor)
    {
        if (practice.HolderId != actor.Id)
        {
            throw new InvalidOperationException("Solo chi ha in carico l'appunto può spostarlo.");
        }
    }

    // ---------- licenza e deleghe ----------

    public void SetLeave(string userId, bool onLeave, string? delegateId)
    {
        var user = GetUser(userId);

        if (!onLeave)
        {
            user.OnLeave = false;
            user.DelegateId = null;
            return;
        }

        if (!string.IsNullOrEmpty(delegateId))
        {
            var delegateUser = GetUser(delegateId);
            if (delegateUser.Role != user.Role || delegateUser.Level != user.Level || delegateUser.Id == user.Id)
            {
                throw new InvalidOperationException("Il sostituto deve essere un parigrado diverso dall'Ufficiale in licenza.");
            }
        }

        user.OnLeave = true;
        user.DelegateId = string.IsNullOrEmpty(delegateId) ? null : delegateId;
    }

    // ---------- validazioni ----------

    public bool CanStamp(WorkflowUser user, ValidationKind kind) => kind switch
    {
        ValidationKind.Concordato => user.Role is Role.CapoSezione or Role.CapoUfficio
            or Role.CapoSezioneDiVertice or Role.CapoUfficioDiVertice,
        ValidationKind.Approvato => user.Role is Role.CapoReparto or Role.AutoritaDiVertice,
        _ => true
    };

    /// <summary>
    /// Può avere una firma PNG preimpostata chi è Capo Ufficio, Capo Reparto o Autorità di vertice.
    /// Non l'hanno i Capi Sezione né il Capo Sezione e il Capo Ufficio di vertice.
    /// </summary>
    public static bool CanHaveSignature(WorkflowUser user) =>
        user.Role is Role.CapoUfficio or Role.CapoReparto or Role.AutoritaDiVertice;

    public ValidationStamp AddValidation(string practiceId, string actorId, ValidationKind kind, DateTimeOffset? at = null)
    {
        var practice = GetPractice(practiceId);
        var actor = GetUser(actorId);

        if (!CanStamp(actor, kind))
        {
            throw new InvalidOperationException($"{actor.Role.DisplayName()} non può apporre «{kind.Title()}».");
        }

        if (practice.Validations.Any(v => v.Kind == kind && v.UserId == actor.Id))
        {
            throw new InvalidOperationException($"{actor.Name} ha già apposto «{kind.Title()}» su questa pratica.");
        }

        var stamp = new ValidationStamp { Kind = kind, UserId = actor.Id, At = at ?? _clock() };
        practice.Validations.Add(stamp);
        AddLog($"{actor.Name} ha apposto «{kind.Title()}» su {practice.Id}");
        return stamp;
    }

    // ---------- registro movimenti ----------

    public void AddLog(string text)
    {
        _state.Log.Insert(0, new MovementLogEntry { At = _clock(), Text = text });
        if (_state.Log.Count > MaxLogEntries)
        {
            _state.Log.RemoveRange(MaxLogEntries, _state.Log.Count - MaxLogEntries);
        }
    }
}
