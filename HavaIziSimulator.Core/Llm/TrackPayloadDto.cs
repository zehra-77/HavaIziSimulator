namespace HavaIziSimulator.Llm;

public sealed class TrackPayloadDto
{
    public int TrackId { get; set; }

    public int Hiz { get; set; }

    public int Yukseklik { get; set; }

    public string Yonelim { get; set; } = string.Empty;

    public string Teshis { get; set; } = string.Empty;

    public string Tasnif { get; set; } = string.Empty;

    public double Enlem { get; set; }

    public double Boylam { get; set; }
}