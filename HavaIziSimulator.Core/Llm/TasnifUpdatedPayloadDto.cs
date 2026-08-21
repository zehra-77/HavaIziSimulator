namespace HavaIziSimulator.Llm;

public sealed class TasnifUpdatedPayloadDto
{
    public int TrackId { get; set; }
    public string YeniTasnif { get; set; } = string.Empty;
}
