using System.Reactive.Concurrency ;
using System.Reactive.Linq ;
using System.Threading ;
using System.Threading.Tasks ;
using Idasen.SystemTray.Win11.Interfaces ;
using Serilog ;
using Microsoft.Toolkit.Uwp.Notifications ;

namespace Idasen.SystemTray.Win11.Utils ;

public enum ReminderState
{
    Sitting ,
    Standing
}

public class SitStandReminderManager : ISitStandReminderManager
{
    private const string ReminderTag   = "Reminder" ;
    private const string ReminderGroup = "SitStandReminders" ;

    private readonly ILogger          _logger ;
    private readonly ISettingsManager _settingsManager ;
    private readonly IUiDeskManager   _uiDeskManager ;
    private readonly IScheduler       _scheduler ;
    private readonly ITimer           _timer ;

    private IDisposable ? _settingsSubscription ;
    private IDisposable ? _heightSubscription ;

    private ReminderState _currentState = ReminderState.Sitting ;
    private bool          _isSnoozed ;
    private uint          _secondsRemaining ;
    private uint          _currentHeight ;

    private readonly object _lock = new ( ) ;
    private          bool   _disposed ;

    public SitStandReminderManager (
        ILogger                                                          logger ,
        ISettingsManager                                                 settingsManager ,
        IUiDeskManager                                                   uiDeskManager ,
        IScheduler                                                       scheduler ,
        Func < TimerCallback , object ? , TimeSpan , TimeSpan , ITimer > timerFactory )
    {
        _logger          = logger ;
        _settingsManager = settingsManager ;
        _uiDeskManager   = uiDeskManager ;
        _scheduler       = scheduler ;

        // Create timer ticking every second
        _timer = timerFactory ( OnTimerTick ,
                                null ,
                                TimeSpan.FromSeconds ( 1 ) ,
                                TimeSpan.FromSeconds ( 1 ) ) ;
    }

    public void Initialize ( )
    {
        _logger.Information ( "Initializing SitStandReminderManager..." ) ;

        // Subscribe to settings changes to refresh timer intervals
        _settingsSubscription = _settingsManager.SettingsSaved
                                                .ObserveOn ( _scheduler )
                                                .Subscribe ( _ => RefreshSettings ( ) ) ;

        // Subscribe to desk height changes
        _heightSubscription = _uiDeskManager.StatusBarInfoChanged
                                            .ObserveOn ( _scheduler )
                                            .Subscribe ( info => OnHeightChanged ( info.Height ) ) ;

        // Initial setup
        RefreshSettings ( true ) ;
    }

    private void RefreshSettings ( bool isInitial = false )
    {
        lock ( _lock )
        {
            var settings = _settingsManager.CurrentSettings.DeviceSettings ;
            if ( ! settings.RemindersEnabled )
            {
                // Reminders disabled, clear any active reminders and pause counting
                DismissReminderNotification ( ) ;
                _secondsRemaining = 0 ;
                return ;
            }

            // If it's the initial run, determine the state based on the current height
            if ( isInitial )
            {
                DetermineInitialState ( ) ;
            }

            // Reset/adjust remaining seconds if needed
            ResetTimerForCurrentState ( ) ;
        }
    }

    private void DetermineInitialState ( )
    {
        var heightSettings = _settingsManager.CurrentSettings.HeightSettings ;
        var currentHeight  = _currentHeight > 0 ? _currentHeight : heightSettings.LastKnownDeskHeight ;

        var standingDiff = Math.Abs ( ( int )currentHeight - ( int )heightSettings.StandingHeightInCm ) ;
        var seatingDiff  = Math.Abs ( ( int )currentHeight - ( int )heightSettings.SeatingHeightInCm ) ;

        _currentState = standingDiff < seatingDiff ? ReminderState.Standing : ReminderState.Sitting ;
        _isSnoozed    = false ;
        _logger.Information ( "Determined initial reminder state: {State} (Current Height: {Height} cm)" ,
                              _currentState ,
                              currentHeight ) ;
    }

    private void ResetTimerForCurrentState ( )
    {
        var settings = _settingsManager.CurrentSettings.DeviceSettings ;
        if ( ! settings.RemindersEnabled )
        {
            _secondsRemaining = 0 ;
            return ;
        }

        if ( _isSnoozed )
        {
            _secondsRemaining = settings.SnoozeIntervalMinutes * 60 ;
        }
        else
        {
            _secondsRemaining = _currentState switch
                                {
                                    ReminderState.Sitting  => settings.SittingIntervalMinutes * 60 ,
                                    ReminderState.Standing => settings.StandingIntervalMinutes * 60 ,
                                    _                      => 0
                                } ;
        }

        _logger.Debug ( "Timer reset for state {State} (Snoozed: {Snoozed}). Remaining: {Seconds}s" ,
                        _currentState ,
                        _isSnoozed ,
                        _secondsRemaining ) ;
    }

    private void OnHeightChanged ( uint height )
    {
        if ( height == 0 ) return ;

        lock ( _lock )
        {
            _currentHeight = height ;

            var settings = _settingsManager.CurrentSettings ;
            if ( ! settings.DeviceSettings.RemindersEnabled ) return ;

            var heightSettings = settings.HeightSettings ;

            // Check if height is close to target heights (+/- 5cm tolerance)
            var isAtStanding = Math.Abs ( ( int )height - ( int )heightSettings.StandingHeightInCm ) <= 5 ;
            var isAtSitting  = Math.Abs ( ( int )height - ( int )heightSettings.SeatingHeightInCm ) <= 5 ;

            if ( isAtStanding && _currentState == ReminderState.Sitting )
            {
                _logger.Information ( "Desk detected at Standing height. Transitioning reminder to Standing state." ) ;
                _currentState = ReminderState.Standing ;
                _isSnoozed    = false ;
                ResetTimerForCurrentState ( ) ;
                DismissReminderNotification ( ) ;
            }
            else if ( isAtSitting && _currentState == ReminderState.Standing )
            {
                _logger.Information ( "Desk detected at Sitting height. Transitioning reminder to Sitting state." ) ;
                _currentState = ReminderState.Sitting ;
                _isSnoozed    = false ;
                ResetTimerForCurrentState ( ) ;
                DismissReminderNotification ( ) ;
            }
        }
    }

    private void OnTimerTick ( object ? state )
    {
        lock ( _lock )
        {
            var settings = _settingsManager.CurrentSettings.DeviceSettings ;
            if ( ! settings.RemindersEnabled ) return ;

            if ( _secondsRemaining > 0 )
            {
                _secondsRemaining -- ;
                if ( _secondsRemaining == 0 )
                {
                    _logger.Information ( "Reminder timer expired. Showing notification." ) ;
                    ShowReminderNotification ( ) ;
                }
            }
        }
    }

    private void ShowReminderNotification ( )
    {
        var settings = _settingsManager.CurrentSettings.DeviceSettings ;
        if ( ! settings.RemindersEnabled || ! settings.NotificationsEnabled ) return ;

        string title ;
        string text ;
        string actionText ;
        string actionArg ;

        if ( _currentState == ReminderState.Sitting )
        {
            title      = "Time to Stand up!" ;
            text       = $"You've been sitting for {settings.SittingIntervalMinutes} minutes. Stand up and stretch!" ;
            actionText = "Stand" ;
            actionArg  = "action=stand" ;
        }
        else
        {
            title      = "Time to Sit down!" ;
            text       = $"You've been standing for {settings.StandingIntervalMinutes} minutes. Time to take a seat." ;
            actionText = "Sit" ;
            actionArg  = "action=sit" ;
        }

        try
        {
            new ToastContentBuilder ( )
               .AddText ( title )
               .AddText ( text )
               .AddButton ( new ToastButton ( actionText ,
                                              actionArg ) )
               .AddButton ( new ToastButton ( "Snooze" ,
                                              "action=snooze" ) )
               .SetToastDuration ( ToastDuration.Long )
               .Show ( toast =>
                       {
                           toast.Tag   = ReminderTag ;
                           toast.Group = ReminderGroup ;
                       } ) ;
        }
        catch ( Exception ex )
        {
            _logger.Error ( ex ,
                            "Failed to show reminder toast notification" ) ;
        }
    }

    private void DismissReminderNotification ( )
    {
        try
        {
            ToastNotificationManagerCompat.History.Remove ( ReminderTag ,
                                                            ReminderGroup ) ;
        }
        catch ( Exception ex )
        {
            _logger.Error ( ex ,
                            "Failed to dismiss reminder toast notification" ) ;
        }
    }

    public void HandleNotificationAction ( string action )
    {
        lock ( _lock )
        {
            _logger.Information ( "Handling notification action: {Action}" ,
                                  action ) ;
            DismissReminderNotification ( ) ;

            var settings = _settingsManager.CurrentSettings.DeviceSettings ;
            if ( ! settings.RemindersEnabled ) return ;

            if ( action == "stand" )
            {
                _currentState = ReminderState.Standing ;
                _isSnoozed    = false ;
                ResetTimerForCurrentState ( ) ;
                Task.Run ( async ( ) =>
                           {
                               try
                               {
                                   await _uiDeskManager.StandAsync ( ) ;
                               }
                               catch ( Exception ex )
                               {
                                   _logger.Error ( ex ,
                                                   "Error moving desk to standing position from notification" ) ;
                               }
                           } ) ;
            }
            else if ( action == "sit" )
            {
                _currentState = ReminderState.Sitting ;
                _isSnoozed    = false ;
                ResetTimerForCurrentState ( ) ;
                Task.Run ( async ( ) =>
                           {
                               try
                               {
                                   await _uiDeskManager.SitAsync ( ) ;
                               }
                               catch ( Exception ex )
                               {
                                   _logger.Error ( ex ,
                                                   "Error moving desk to sitting position from notification" ) ;
                               }
                           } ) ;
            }
            else if ( action == "snooze" )
            {
                _isSnoozed = true ;
                ResetTimerForCurrentState ( ) ;
                _logger.Information ( "Reminder snoozed for {Minutes} minutes" ,
                                      settings.SnoozeIntervalMinutes ) ;
            }
        }
    }

    public void Dispose ( )
    {
        Dispose ( true ) ;
        GC.SuppressFinalize ( this ) ;
    }

    protected virtual void Dispose ( bool disposing )
    {
        if ( _disposed ) return ;

        if ( disposing )
        {
            _timer?.Dispose ( ) ;
            _settingsSubscription?.Dispose ( ) ;
            _heightSubscription?.Dispose ( ) ;
        }

        _disposed = true ;
    }
}
