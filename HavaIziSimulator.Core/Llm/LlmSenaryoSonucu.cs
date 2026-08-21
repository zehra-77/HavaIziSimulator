using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Llm;

/// <summary>
/// RadarScenarioValidator'ın ürettiği, ICD tiplerine dönüştürülmüş ve
/// aralık doğrulaması yapılmış sonuç. MessageType'a göre yalnızca
/// ilgili alan dolu gelir; MainViewModel bunu switch ile SensorSimulatoru'ndaki
/// karşılığına yönlendirir.
/// </summary>
public sealed record LlmSenaryoSonucu(
    MessageType MessageType,
    TrackData? TrackData = null,
    TrackDroppedData? TrackDroppedData = null,
    TeshisUpdatedData? TeshisUpdatedData = null,
    TasnifUpdatedData? TasnifUpdatedData = null);