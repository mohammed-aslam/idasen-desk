namespace Idasen.SystemTray.Win11.Interfaces ;

public interface ISitStandReminderManager : IDisposable
{
    void Initialize ( ) ;
    void HandleNotificationAction ( string action ) ;
}
