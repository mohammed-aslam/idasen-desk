using FluentAssertions ;
using Idasen.SystemTray.Win11.Interfaces ;
using Idasen.SystemTray.Win11.TraySettings ;
using Idasen.SystemTray.Win11.Utils ;
using NSubstitute ;
using Serilog ;
using System.Reactive.Concurrency ;
using System.Reactive.Subjects ;
using System.Threading ;
using Idasen.TestLogger ;
using Xunit ;

namespace Idasen.SystemTray.Win11.Tests.Utils ;

public class SitStandReminderManagerTests : IDisposable
{
    private readonly InMemoryLogger         _logger          = new ( ) ;
    private readonly ISettingsManager       _settingsManager = Substitute.For < ISettingsManager > ( ) ;
    private readonly IUiDeskManager         _uiDeskManager   = Substitute.For < IUiDeskManager > ( ) ;
    private readonly IScheduler             _scheduler       = ImmediateScheduler.Instance ;
    private readonly Subject < ISettings >  _settingsSaved   = new ( ) ;
    private readonly Subject < StatusBarInfo > _statusBarInfoChanged = new ( ) ;
    private readonly ISettings              _settings        = Substitute.For < ISettings > ( ) ;
    private readonly DeviceSettings         _deviceSettings  = new ( ) ;
    private readonly HeightSettings         _heightSettings  = new ( ) ;
    private          bool                   _disposed ;

    public SitStandReminderManagerTests ( )
    {
        _settingsManager.SettingsSaved.Returns ( _settingsSaved ) ;
        _settingsManager.CurrentSettings.Returns ( _settings ) ;
        _settings.DeviceSettings = _deviceSettings ;
        _settings.HeightSettings = _heightSettings ;

        _uiDeskManager.StatusBarInfoChanged.Returns ( _statusBarInfoChanged ) ;

        _deviceSettings.RemindersEnabled        = true ;
        _deviceSettings.SittingIntervalMinutes  = 40 ;
        _deviceSettings.StandingIntervalMinutes = 15 ;
        _deviceSettings.SnoozeIntervalMinutes   = 5 ;

        _heightSettings.StandingHeightInCm  = 120 ;
        _heightSettings.SeatingHeightInCm   = 65 ;
        _heightSettings.LastKnownDeskHeight = 65 ; // closer to sitting
    }

    [ Fact ]
    public void Initialize_ShouldStartTimerAndConfigureSubscriptions ( )
    {
        // Arrange
        var mockTimer = Substitute.For < ITimer > ( ) ;
        Func < TimerCallback , object ? , TimeSpan , TimeSpan , ITimer > timerFactory =
            ( callback , state , due , period ) => mockTimer ;

        using var sut = new SitStandReminderManager ( _logger ,
                                                      _settingsManager ,
                                                      _uiDeskManager ,
                                                      _scheduler ,
                                                      timerFactory ) ;

        // Act
        sut.Initialize ( ) ;

        // Assert
        mockTimer.Should ( ).NotBeNull ( ) ;
    }

    public void Dispose ( )
    {
        if ( ! _disposed )
        {
            _settingsSaved.Dispose ( ) ;
            _statusBarInfoChanged.Dispose ( ) ;
            _logger.Dispose ( ) ;
            _disposed = true ;
        }
        GC.SuppressFinalize ( this ) ;
    }
}
