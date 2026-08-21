using System.Text.Json;

namespace HavaIziSimulator.LogEkleme;

/// <summary>
/// JSONL dosyasındaki tek bir satırı temsil eder.
/// </summary>
public sealed class IcdLogRecord
{
    public string Log { get; set; } = "";

    public string MessageType { get; set; } = "";

    public IcdLogHeader Header { get; set; } = new();

    // Payload mesaj türüne göre değiştiği için şimdilik JsonElement tutuyoruz.
    public JsonElement Payload { get; set; }
}

public sealed class IcdLogHeader
{
    public string MessageType { get; set; } = "";

    public uint SequenceNumber { get; set; }

    public ulong TimestampEpochMillis { get; set; }

    public int PayloadLength { get; set; }
}