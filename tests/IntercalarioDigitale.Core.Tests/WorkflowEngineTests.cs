using IntercalarioDigitale.Core.Workflow;
using Xunit;

namespace IntercalarioDigitale.Core.Tests;

public sealed class WorkflowEngineTests
{
    private static (WorkflowState State, WorkflowEngine Engine) CreateEngine()
    {
        var state = DemoData.CreateBaseState();
        state.Practices.Add(new Practice { Id = "2.026_1", Name = "Prova", HolderId = "verdi" });
        var clock = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        return (state, new WorkflowEngine(state, () => clock));
    }

    [Fact]
    public void ForwardTargets_FollowTheHierarchy()
    {
        var (_, engine) = CreateEngine();

        var fromSezione = engine.GetForwardTargets("verdi");
        Assert.All(fromSezione, t => Assert.Equal(Role.CapoUfficio, engine.GetUser(t.RecipientId).Role));
        Assert.Equal(2, fromSezione.Count);

        var fromReparto = engine.GetForwardTargets("moretti");
        Assert.Contains(fromReparto, t => t.Mode == TransferMode.Forward && t.RecipientId == "neri");
        Assert.Contains(fromReparto, t => t.Mode == TransferMode.Tramite && t.RecipientId == "bianchi" && t.DepartmentId == "log");
        Assert.DoesNotContain(fromReparto, t => t.RecipientId == "verdi");

        Assert.Empty(engine.GetForwardTargets("neri"));
    }

    [Fact]
    public void CapoUfficio_NeedsDelegationToReachVertice()
    {
        var (_, engine) = CreateEngine();

        Assert.DoesNotContain(engine.GetForwardTargets("rossi"), t => t.RecipientId == "neri");

        engine.SetViceDelegation("rossi", true);

        Assert.Contains(engine.GetForwardTargets("rossi"), t => t.RecipientId == "neri");
    }

    [Fact]
    public void ViceDelegation_IsOnlyForCapiUfficio()
    {
        var (_, engine) = CreateEngine();

        Assert.Throws<InvalidOperationException>(() => engine.SetViceDelegation("verdi", true));
    }

    [Fact]
    public void Forward_MovesThePracticeAndWritesTheLog()
    {
        var (state, engine) = CreateEngine();

        var result = engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));

        var practice = engine.GetPractice("2.026_1");
        Assert.Equal("rossi", practice.HolderId);
        Assert.Equal("verdi", practice.FromId);
        Assert.Equal(PracticeStatus.InLavorazione, practice.Status);
        Assert.Equal("rossi", result.FinalHolderId);
        Assert.Contains("2.026_1 inoltrato a L. Rossi", state.Log[0].Text);
    }

    [Fact]
    public void Forward_ByWhoDoesNotHoldThePractice_Fails()
    {
        var (_, engine) = CreateEngine();

        Assert.Throws<InvalidOperationException>(
            () => engine.Forward("2.026_1", "bianchi", new ForwardTarget(TransferMode.Forward, "rossi")));
    }

    [Fact]
    public void Forward_ToNotAllowedRecipient_Fails()
    {
        var (_, engine) = CreateEngine();

        Assert.Throws<InvalidOperationException>(
            () => engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "neri")));
    }

    [Fact]
    public void Forward_ToRecipientOnLeave_GoesToTheDelegate()
    {
        var (_, engine) = CreateEngine();
        engine.SetLeave("rossi", true, "colombo");

        var result = engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));

        Assert.Equal("colombo", result.FinalHolderId);
        Assert.Equal(PracticeStatus.InLavorazione, result.Status);
        Assert.Contains("delegato P. Colombo", result.Message);
    }

    [Fact]
    public void Forward_ToRecipientOnLeaveWithoutDelegate_WaitsForTakeOver()
    {
        var (_, engine) = CreateEngine();
        engine.SetLeave("rossi", true, null);

        var result = engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));

        Assert.Equal("rossi", result.FinalHolderId);
        Assert.Equal(PracticeStatus.InAttesaDiPresaInCarico, result.Status);
    }

    [Fact]
    public void Tramite_GoesToTheSectionHeadOfTheOtherDepartment()
    {
        var (state, engine) = CreateEngine();
        state.Practices[0].HolderId = "moretti";

        var result = engine.Forward(
            "2.026_1", "moretti", new ForwardTarget(TransferMode.Tramite, "bianchi", "log"));

        Assert.Equal("bianchi", result.FinalHolderId);
        Assert.Contains("tramite telematico", result.Message);
    }

    [Fact]
    public void Return_GoesBackToTheSender()
    {
        var (_, engine) = CreateEngine();
        engine.Forward("2.026_1", "verdi", new ForwardTarget(TransferMode.Forward, "rossi"));

        var result = engine.Return("2.026_1", "rossi", "Mancano gli allegati");

        var practice = engine.GetPractice("2.026_1");
        Assert.Equal("verdi", practice.HolderId);
        Assert.Equal("rossi", practice.FromId);
        Assert.Equal(PracticeStatus.Restituito, result.Status);
        Assert.Contains("Mancano gli allegati", engine.State.Log[0].Text);
    }

    [Fact]
    public void Return_WithoutSender_Fails()
    {
        var (_, engine) = CreateEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Return("2.026_1", "verdi"));
    }

    [Fact]
    public void SetLeave_RequiresAPeerAsDelegate()
    {
        var (_, engine) = CreateEngine();

        Assert.Throws<InvalidOperationException>(() => engine.SetLeave("rossi", true, "verdi"));
        Assert.Throws<InvalidOperationException>(() => engine.SetLeave("rossi", true, "rossi"));

        engine.SetLeave("rossi", true, "colombo");
        Assert.Equal("colombo", engine.GetUser("rossi").DelegateId);

        engine.SetLeave("rossi", false, null);
        Assert.False(engine.GetUser("rossi").OnLeave);
        Assert.Null(engine.GetUser("rossi").DelegateId);
    }

    [Theory]
    [InlineData("verdi", ValidationKind.Concordato, true)]
    [InlineData("rossi", ValidationKind.Concordato, true)]
    [InlineData("moretti", ValidationKind.Concordato, false)]
    [InlineData("moretti", ValidationKind.Approvato, true)]
    [InlineData("neri", ValidationKind.Approvato, true)]
    [InlineData("verdi", ValidationKind.Approvato, false)]
    [InlineData("verdi", ValidationKind.Visto, true)]
    [InlineData("neri", ValidationKind.Visto, true)]
    public void CanStamp_DependsOnTheRole(string userId, ValidationKind kind, bool expected)
    {
        var (_, engine) = CreateEngine();

        Assert.Equal(expected, engine.CanStamp(engine.GetUser(userId), kind));
    }

    [Fact]
    public void AddValidation_RejectsDuplicatesAndForbiddenRoles()
    {
        var (_, engine) = CreateEngine();

        var stamp = engine.AddValidation("2.026_1", "verdi", ValidationKind.Concordato);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), stamp.At);

        Assert.Throws<InvalidOperationException>(
            () => engine.AddValidation("2.026_1", "verdi", ValidationKind.Concordato));
        Assert.Throws<InvalidOperationException>(
            () => engine.AddValidation("2.026_1", "verdi", ValidationKind.Approvato));
        Assert.Single(engine.GetPractice("2.026_1").Validations);
    }

    [Fact]
    public void Log_KeepsOnlyTheLatestEntries()
    {
        var (state, engine) = CreateEngine();

        for (var i = 0; i < 80; i++)
        {
            engine.AddLog($"voce {i}");
        }

        Assert.Equal(50, state.Log.Count);
        Assert.Equal("voce 79", state.Log[0].Text);
    }
}
