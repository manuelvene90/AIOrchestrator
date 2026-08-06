using System.Text.Json;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>Shared JSON writer options for every file this library persists.</summary>
public static class JsonWriting
{
    public static readonly JsonSerializerOptions INDENTED = new() { WriteIndented = true };
}
