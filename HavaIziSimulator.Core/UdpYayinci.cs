using System.Net;
using System.Net.Sockets;

namespace HavaIziSimulator;

/// <summary>
/// ICD Bölüm 2.2'de tanımlanan taşıma katmanı parametrelerine göre
/// (UDP/IPv4, Broadcast veya Unicast) mesaj gönderen sınıf.
/// Her mesaj tipi ayrı bir UDP datagramı olarak gönderilir; mesajlar
/// birleştirilmez (Bölüm 6 — "Çoklu İz" kuralı).
/// </summary>
public sealed class UdpYayinci : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _hedefEndpoint;
    private readonly bool _broadcastModu;

    /// <param name="hedefIp">
    /// Unicast için hedef IP adresi. Broadcast modunda kullanılmaz.
    /// </param>
    /// <param name="port">Hedef UDP portu (varsayılan: 5000).</param>
    /// <param name="broadcastModu">
    /// true ise 255.255.255.255 adresine broadcast yapılır (Bölüm 2.2),
    /// false ise hedefIp parametresine unicast gönderim yapılır.
    /// </param>
    public UdpYayinci(string hedefIp, int port, bool broadcastModu)
    {
        _broadcastModu = broadcastModu;
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        if (broadcastModu)
        {
            _udpClient.EnableBroadcast = true;
            _hedefEndpoint = new IPEndPoint(IPAddress.Broadcast, port);
        }
        else
        {
            _hedefEndpoint = new IPEndPoint(IPAddress.Parse(hedefIp), port);
        }
    }

    public IPEndPoint HedefEndpoint => _hedefEndpoint;
    public bool BroadcastModu => _broadcastModu;

    /// <summary>
    /// Kodlanmış (Header+Payload+CRC) tam mesaj bayt dizisini,
    /// tek bir UDP datagramı olarak gönderir.
    /// </summary>
    public void Gonder(byte[] mesaj)
    {
        _udpClient.Send(mesaj, mesaj.Length, _hedefEndpoint);
    }

    public void Dispose()
    {
        _udpClient.Dispose();
    }
}
