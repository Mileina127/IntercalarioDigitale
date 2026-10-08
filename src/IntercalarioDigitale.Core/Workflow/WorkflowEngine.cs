namespace IntercalarioDigitale.Core.Workflow;

/// <summary>
/// Regole di flusso degli appunti (vedi "Struttura funzionale dell'applicativo"):
/// via gerarchica Sezione → Ufficio → Reparto → Vertice, delega al Vertice per i Capi Ufficio,
/// licenza con sostituto parigrado, tramite telematico riservato ai Capi Reparto.
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

    // ---------- destinatari ----------

    public IReadOnlyList<ForwardTarget> GetForwardTargets(string actorId)
    {
        var actor = GetUser(actorId);
        var targets = new List<ForwardTarget>();

        var nextLevel = (int)actor.Role + 1;
        foreach (var user in _state.Users.Where(u => (int)u.Role == nextLevel && u.Id != actor.Id))
        {
            targets.Add(new ForwardTarget(TransferMode.Forward, user.Id));
        }

        if (actor.Role == Role.CapoUfficio && actor.CanForwardToVertice)
        {
            foreach (var user in _state.Users.Where(u => u.Role == Role.AutoritaDiVertice))
            {
                targets.Add(new ForwardTarget(TransferMode.Forward, user.Id));
            }
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

        return $"{recipient.Name} · {recipient.Role.DisplayName()}, {recipient.Unit}";
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
        practice.HolderId == actorId && GetForwardTargets(actorId).Count > 0;

    public bool CanReturn(Practice practice, string actorId) =>
        practice.HolderId == actorId && practice.FromId is not null;

    public TransferResult Forward(string practiceId, string actorId, ForwardTarget target, string? note = null)
    {
        var practice = GetPractice(practiceId);
        var actor = GetUser(actorId);
        EnsureHolder(practice, actor);

        if (target.Mode == TransferMode.Return || !GetForwardTargets(actorId).Contains(target))
        {
            throw new InvalidOperationException("Destinatario non consentito per questo ruolo.");
        }

        return Deliver(practice, actor, GetUser(target.RecipientId), target.Mode, note);
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

        return Deliver(practice, actor, GetUser(practice.FromId), TransferMode.Return, note);
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
            if (delegateUser.Role != user.Role || delegateUser.Id == user.Id)
            {
                throw new InvalidOperationException("Il sostituto deve essere un parigrado diverso dall'Ufficiale in licenza.");
            }
        }

        user.OnLeave = true;
        user.DelegateId = string.IsNullOrEmpty(delegateId) ? null : delegateId;
    }

    public void SetViceDelegation(string userId, bool enabled)
    {
        var user = GetUser(userId);
        if (user.Role != Role.CapoUfficio)
        {
            throw new InvalidOperationException("La delega di inoltro al Vertice riguarda solo i Capi Ufficio.");
        }

        user.CanForwardToVertice = enabled;
    }

    // ---------- validazioni ----------

    public bool CanStamp(WorkflowUser user, ValidationKind kind) => kind switch
    {
        ValidationKind.Concordato => user.Role is Role.CapoSezione or Role.CapoUfficio,
        ValidationKind.Approvato => user.Role is Role.CapoReparto or Role.AutoritaDiVertice,
        _ => true
    };

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
