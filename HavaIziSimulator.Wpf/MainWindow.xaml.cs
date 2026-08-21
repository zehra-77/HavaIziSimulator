using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HavaIziSimulator.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private LogWindow? _logWindow;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.Loglar.CollectionChanged += Loglar_CollectionChanged;

        Closing += MainWindow_Closing;
    }

    private void Loglar_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ListBox? listBox =
                    FindName("GonderimLogListesi") as ListBox
                    ?? FindVisualChild<ListBox>(this);

                if (listBox is { Items.Count: > 0 })
                {
                    listBox.ScrollIntoView(
                        listBox.Items[listBox.Items.Count - 1]);
                }
            }),
            DispatcherPriority.Background);
    }

    private void OtomatikMod_Checked(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.OtomatikModDegisti(true);
    }

    private void OtomatikMod_Unchecked(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.OtomatikModDegisti(false);
    }

    private void TemaDegistir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ThemeManager.GeceModuAktif)
        {
            ThemeManager.GunduzModunuUygula();
            TemaDegistirButonu.Content = "☾ Gece Modu";
        }
        else
        {
            ThemeManager.GeceModunuUygula();
            TemaDegistirButonu.Content = "☀ Gündüz Modu";
        }
    }

    private void GonderimLogunuBuyut_Click(
        object sender,
        RoutedEventArgs e)
    {
        GonderimLogPenceresiniAc();
    }

    private void GonderimLogPenceresiniAc()
    {
        if (_logWindow is not null)
        {
            if (_logWindow.WindowState == WindowState.Minimized)
            {
                _logWindow.WindowState = WindowState.Normal;
            }

            _logWindow.Activate();
            _logWindow.Focus();

            return;
        }

        _logWindow = new LogWindow
        {
            Owner = this,
            DataContext = _viewModel
        };

        _logWindow.Closed += LogWindow_Closed;
        _logWindow.Show();
    }

    private void LogWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_logWindow is null)
        {
            return;
        }

        _logWindow.Closed -= LogWindow_Closed;
        _logWindow = null;
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_logWindow is not null)
        {
            _logWindow.Closed -= LogWindow_Closed;
            _logWindow.Close();
            _logWindow = null;
        }

        _viewModel.Loglar.CollectionChanged -=
            Loglar_CollectionChanged;

        _viewModel.Dispose();
    }

    private static T? FindVisualChild<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        int childCount =
            VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
            {
                return typedChild;
            }

            T? result = FindVisualChild<T>(child);

            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

}