using IcdLib.Models;

namespace HavaIziSimulator;

internal sealed class AktifIz
{
    public required TrackData Data { get; set; }

    /// <summary>
    /// İz seçilen JSONL senaryosu tarafından oluşturulduysa true olur.
    /// Genel aktif liste tüm kaynakları, Log Replay listesi yalnız bu izleri gösterir.
    /// </summary>
    public bool LogReplayIzi { get; init; }

    public DateTime SonrakiGuncellemeZamani { get; set; }

    /// <summary>
    /// Kullanıcı hız, yükseklik, yönelim veya konumu elle değiştirdiyse
    /// bu değerlerin bir sonraki periyodik TRACK_UPDATED mesajında
    /// rastgele değiştirilmeden gönderilmesini sağlar.
    /// </summary>
    public bool ManuelGuncellemeBekliyor { get; set; }
}
