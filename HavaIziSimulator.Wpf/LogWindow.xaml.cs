using System.Collections.Specialized;
using System.Windows;

namespace HavaIziSimulator.Wpf;

public partial class LogWindow : Window
{
    private MainViewModel? _viewModel;

    public LogWindow()
    {
        InitializeComponent();

        Loaded += LogWindow_Loaded;
        Closed += LogWindow_Closed;
    }

    private void LogWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as MainViewModel;

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Loglar.CollectionChanged += Loglar_CollectionChanged;

        SonLogaGit();
    }

    private void LogWindow_Closed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Loglar.CollectionChanged -= Loglar_CollectionChanged;
        }
    }

    private void Loglar_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        SonLogaGit();
    }

    private void SonLogaGit()
    {
        if (BuyukLogListesi.Items.Count == 0)
        {
            return;
        }

        BuyukLogListesi.Dispatcher.BeginInvoke(() =>
        {
            object sonKayit =
                BuyukLogListesi.Items[
                    BuyukLogListesi.Items.Count - 1];

            BuyukLogListesi.ScrollIntoView(sonKayit);
        });
    }

    private void Kapat_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}