namespace HavaIziSimulator.Llm;

public sealed class TrackDroppedPayloadDto
{
    public int TrackId { get; set; }
    public string Neden { get; set; } = string.Empty;
}