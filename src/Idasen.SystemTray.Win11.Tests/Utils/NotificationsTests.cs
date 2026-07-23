using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Idasen.SystemTray.Win11.Interfaces;
using Idasen.SystemTray.Win11.Utils;
using Idasen.TestLogger;
using NSubstitute;

namespace Idasen.SystemTray.Win11.Tests.Utils;

public sealed class NotificationsTests
{
    [Fact]
    #pragma warning disable xUnit1051
    public async Task Initialize_DoesNotShowStartupNotification_WhenSettingsLoadSucceeds()
    {
        // Arrange
        var logger = new InMemoryLogger();
        var settingsManager = Substitute.For<ISettingsManager>();
        var versionProvider = Substitute.For<IVersionProvider>();
        var toastService = Substitute.For<IToastService>();

        versionProvider.GetVersion().Returns("1.0.0");
#pragma warning disable CA2012
        settingsManager.LoadAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask());
#pragma warning restore CA2012

        var notifications = new Notifications(logger, settingsManager, versionProvider, toastService);

        // Act
        notifications.Initialize(null!, CancellationToken.None);

        await Task.Delay(100);

        // Assert
        toastService.DidNotReceive().Show(Arg.Any<NotificationParameters>());
    }
    #pragma warning restore xUnit1051
}
