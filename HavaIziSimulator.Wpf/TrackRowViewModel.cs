using System.ComponentModel;
using System.Runtime.CompilerServices;
using HavaIziSimulator;
using IcdLib.Enums;
using IcdLib.Models;

namespace HavaIziSimulator.Wpf;

/// <summary>
/// Aktif izler DataGrid'inde bir satırı temsil eder. Her `Tick` sonrası
/// ilgili `TrackData`'dan tazelenerek UI'a canlı veri yansıtılır.
/// </summary>
public sealed class TrackRowViewModel : INotifyPropertyChanged
{
    private ushort _trackId;
    private ushort _speedKnots;
    private ushort _altitudeMeters;
    private Yonelim _yonelim;
    private Teshis _teshis;
    private Tasnif _tasnif;
    private double _latitude;
    private double _longitude;
    private DateTime _sonGuncellemeYerel;

    public ushort TrackId { get => _trackId; set => Set(ref _trackId, value); }
    public ushort SpeedKnots { get => _speedKnots; set => Set(ref _speedKnots, value); }
    public ushort AltitudeMeters { get => _altitudeMeters; set => Set(ref _altitudeMeters, value); }
    public Yonelim Yonelim { get => _yonelim; set => Set(ref _yonelim, value); }
    public Teshis Teshis { get => _teshis; set => Set(ref _teshis, value); }
    public Tasnif Tasnif { get => _tasnif; set => Set(ref _tasnif, value); }
    public double Latitude { get => _latitude; set => Set(ref _latitude, value); }
    public double Longitude { get => _longitude; set => Set(ref _longitude, value); }
    public DateTime SonGuncellemeYerel { get => _sonGuncellemeYerel; set => Set(ref _sonGuncellemeYerel, value); }

    public void Guncelle(TrackData d)
    {
        TrackId = d.TrackId;
        SpeedKnots = d.Hiz;
        AltitudeMeters = d.Yukseklik;
        Yonelim = d.Yonelim;
        Teshis = d.Teshis;
        Tasnif = d.Tasnif;
        Latitude = d.Enlem;
        Longitude = d.Boylam;
        SonGuncellemeYerel = DateTimeOffset.FromUnixTimeMilliseconds((long)d.IzZamaniEpochMillis).LocalDateTime;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T alan, T deger, [CallerMemberName] string? adi = null)
    {
        if (EqualityComparer<T>.Default.Equals(alan, deger)) return;
        alan = deger;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(adi));
    }
}
