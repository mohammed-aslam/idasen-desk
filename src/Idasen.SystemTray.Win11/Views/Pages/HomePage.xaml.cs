using System.Diagnostics.CodeAnalysis ;
using System.Reactive.Disposables ;
using System.Windows ;
using System.Windows.Input ;
using Idasen.SystemTray.Win11.Interfaces ;
using Idasen.SystemTray.Win11.ViewModels.Pages ;
using Wpf.Ui.Abstractions.Controls ;

namespace Idasen.SystemTray.Win11.Views.Pages ;

[ ExcludeFromCodeCoverage ]
public partial class HomePage : INavigableView < DashboardViewModel >, IDisposable
{
    private readonly IUiDeskManager _uiDeskManager ;
    private readonly ISettingsManager _settingsManager ;
    private readonly CompositeDisposable _disposables = new ( ) ;

    public HomePage ( DashboardViewModel viewModel ,
                      IDonateService     donateService ,
                      IUiDeskManager     uiDeskManager ,
                      ISettingsManager   settingsManager )
    {
        ViewModel       = viewModel ;
        DataContext     = this ;
        _uiDeskManager  = uiDeskManager ;
        _settingsManager = settingsManager ;

        InitializeComponent ( ) ;

        Loaded += OnLoaded ;
        Unloaded += OnUnloaded ;
    }

    public DashboardViewModel ViewModel { get ; }

    private void OnLoaded ( object sender , System.Windows.RoutedEventArgs e )
    {
        UpdateUi ( ) ;

        var heightSettings = _settingsManager.CurrentSettings.HeightSettings ;

        var sub = _uiDeskManager.StatusBarInfoChanged
            .Subscribe ( info =>
            {
                Application.Current.Dispatcher.Invoke ( ( ) =>
                {
                    CurrentHeightText.Text = info.Height > 0 ? $"{info.Height}" : "--" ;

                    if ( info.Height > 0 )
                    {
                        double min = heightSettings.DeskMinHeightInCm ;
                        double max = heightSettings.DeskMaxHeightInCm ;
                        double current = info.Height ;

                        double progressPercent = ( ( current - min ) / ( max - min ) ) * 100.0 ;
                        progressPercent = System.Math.Max ( 0.0 , System.Math.Min ( 100.0 , progressPercent ) ) ;

                        HeightProgressRing.Progress = progressPercent ;
                    }
                    else
                    {
                        HeightProgressRing.Progress = 0.0 ;
                    }

                    DeviceDetailsText.Text = info.Message ?? "-" ;
                    UpdateUi ( ) ;
                } );
            } );

        _disposables.Add ( sub ) ;
    }

    private void OnUnloaded ( object sender , System.Windows.RoutedEventArgs e )
    {
        _disposables.Dispose ( ) ;
    }

    public void Dispose ( )
    {
        _disposables.Dispose ( ) ;
        System.GC.SuppressFinalize ( this ) ;
    }

    private void UpdateUi ( )
    {
        var heightSettings = _settingsManager.CurrentSettings.HeightSettings ;

        SitButtonText.Text = heightSettings.SeatingName ;
        SitHeightText.Text = $"{heightSettings.SeatingHeightInCm} cm" ;

        StandButtonText.Text = heightSettings.StandingName ;
        StandHeightText.Text = $"{heightSettings.StandingHeightInCm} cm" ;

        Custom1ButtonText.Text = heightSettings.Custom1Name ;
        Custom1HeightText.Text = $"{heightSettings.Custom1HeightInCm} cm" ;

        Custom2ButtonText.Text = heightSettings.Custom2Name ;
        Custom2HeightText.Text = $"{heightSettings.Custom2HeightInCm} cm" ;

        var isConnected = _uiDeskManager.IsConnected ;
        ConnectButton.Content = isConnected ? "Disconnect" : "Connect" ;

        // Update Bluetooth status indicator icon and text
        if ( isConnected )
        {
            ConnectionStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.BluetoothConnected24 ;
            ConnectionStatusIcon.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource ( "SystemAccentColorBrush" ) ;
            ConnectionStatusText.Text = "Connected" ;
        }
        else
        {
            ConnectionStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.BluetoothDisabled24 ;
            ConnectionStatusIcon.Foreground = System.Windows.Media.Brushes.Gray ;
            ConnectionStatusText.Text = "Disconnected" ;
        }

        // Enable or disable preset buttons & stop button based on connectivity
        SitButton.IsEnabled = isConnected ;
        StandButton.IsEnabled = isConnected ;
        Custom1Button.IsEnabled = isConnected ;
        Custom2Button.IsEnabled = isConnected ;
        StopButton.IsEnabled = isConnected ;
    }

    private async void SitButton_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        await _uiDeskManager.SitAsync ( ) ;
    }

    private async void StandButton_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        await _uiDeskManager.StandAsync ( ) ;
    }

    private async void Custom1Button_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        await _uiDeskManager.Custom1Async ( ) ;
    }

    private async void Custom2Button_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        await _uiDeskManager.Custom2Async ( ) ;
    }

    private async void StopButton_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        await _uiDeskManager.StopAsync ( ) ;
    }

    private async void ConnectButton_OnClick ( object sender , System.Windows.RoutedEventArgs e )
    {
        if ( _uiDeskManager.IsConnected )
        {
            await _uiDeskManager.DisconnectAsync ( ) ;
        }
        else
        {
            await _uiDeskManager.AutoConnectAsync ( ) ;
        }

        UpdateUi ( ) ;
    }
}

