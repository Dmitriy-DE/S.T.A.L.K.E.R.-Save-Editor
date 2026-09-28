using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Backups;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, object?>), TypeInfoPropertyName = "Journal")]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
internal partial class BackupJournalJsonContext : JsonSerializerContext
{
}
