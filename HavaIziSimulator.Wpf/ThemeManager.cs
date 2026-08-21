using System.Windows;
using System.Windows.Media;

namespace HavaIziSimulator.Wpf;

/// <summary>
/// Uygulamanın ortak renk kaynaklarını açık veya gece paletiyle değiştirir.
/// Pencereler aynı Application kaynaklarını kullandığı için açık olan LogWindow
/// dahil bütün ekranlar yeniden başlatmadan anında güncellenir.
/// </summary>
public static class ThemeManager
{
    public static bool GeceModuAktif { get; private set; }

    public static void GeceModunuUygula()
    {
        GeceModuAktif = true;
        RenkleriUygula(GecePaleti);
    }

    public static void GunduzModunuUygula()
    {
        GeceModuAktif = false;
        RenkleriUygula(GunduzPaleti);
    }

    private static void RenkleriUygula(
        IReadOnlyDictionary<string, string> renkler)
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach ((string kaynakAdi, string hexRenk) in renkler)
        {
            Color renk =
                (Color)ColorConverter.ConvertFromString(hexRenk);

            // Color alt-kaynağını değil, kontrolün doğrudan kullandığı
            // SolidColorBrush kaynağını değiştiriyoruz.
            Application.Current.Resources[kaynakAdi] =
                new SolidColorBrush(renk);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> GunduzPaleti =
        new Dictionary<string, string>
        {
            ["ArkaPlanKoyu"] = "#F4F7FB",
            ["PanelArkaPlan"] = "#FFFFFF",
            ["PanelKenarlik"] = "#D9E2EC",
            ["PanelIkincil"] = "#F8FAFC",
            ["VurguYesil"] = "#16A36A",
            ["VurguKirmizi"] = "#DC4C4C",
            ["VurguSari"] = "#D99A16",
            ["VurguMavi"] = "#2563EB",
            ["VurguMaviAcik"] = "#EAF1FF",
            ["MetinAna"] = "#172033",
            ["MetinSoluk"] = "#64748B",
            ["SatirSecili"] = "#DDE9FF"
        };

    private static readonly IReadOnlyDictionary<string, string> GecePaleti =
        new Dictionary<string, string>
        {
            ["ArkaPlanKoyu"] = "#0B1220",
            ["PanelArkaPlan"] = "#111827",
            ["PanelKenarlik"] = "#334155",
            ["PanelIkincil"] = "#1E293B",
            ["VurguYesil"] = "#34D399",
            ["VurguKirmizi"] = "#F87171",
            ["VurguSari"] = "#FBBF24",
            ["VurguMavi"] = "#60A5FA",
            ["VurguMaviAcik"] = "#1E3A5F",
            ["MetinAna"] = "#F1F5F9",
            ["MetinSoluk"] = "#A8B4C7",
            ["SatirSecili"] = "#1D4E75"
        };
}