using System.Text.Json;

namespace HavaIziSimulator.Llm;

/// <summary>
/// MCP radar aracının döndürdüğü ham senaryo zarfı.
/// Payload'ın şekli <see cref="MessageType"/>'a göre değiştiği için
/// (aynı IcdLogRecord'daki desende olduğu gibi) burada JsonElement tutulur;
/// asıl tipe çevirme ve doğrulama işi RadarScenarioValidator'da yapılır.
/// </summary>
public sealed class RadarScenarioDto
{
    public string MessageType { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}
