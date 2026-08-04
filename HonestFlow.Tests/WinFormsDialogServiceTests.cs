using System;
using System.Windows.Forms;
using HonestFlow.Infrastructure.Dialogs;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class WinFormsDialogServiceTests
    {
        [Fact]
        public void OneButtonMessages_UseNotificationSinkWhenAvailable()
        {
            var sink = new NotificationOwner();
            var dialogs = new WinFormsDialogService(sink);

            dialogs.ShowWarning("message", "title");

            Assert.Equal("message", sink.Message);
            Assert.Equal("title", sink.Title);
            Assert.Equal(UserNotificationSeverity.Warning, sink.Severity);
        }

        private sealed class NotificationOwner : IWin32Window, IUserNotificationSink
        {
            public IntPtr Handle => IntPtr.Zero;
            public string Message { get; private set; }
            public string Title { get; private set; }
            public UserNotificationSeverity Severity { get; private set; }

            public void ShowNotification(
                string message,
                string title,
                UserNotificationSeverity severity)
            {
                Message = message;
                Title = title;
                Severity = severity;
            }
        }
    }
}
