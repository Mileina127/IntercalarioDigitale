using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>Salvataggio e lettura dello stato del flusso (utenti, pratiche, registro) in un file JSON.</summary>
public static class WorkflowStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static WorkflowState Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkflowState>(json, JsonOptions)
               ?? throw new InvalidOperationException("File di stato non valido.");
    }

    public static void Save(string path, WorkflowState state)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
