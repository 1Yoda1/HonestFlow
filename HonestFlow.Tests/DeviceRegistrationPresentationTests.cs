using System;
using HonestFlow.Application.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DeviceRegistrationPresentationTests
    {
        [Fact]
        public void AwaitingAddress_ShowsSubmitAndSafeSwitchClientActions()
        {
            DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
                DeviceRegistrationStartupResult.AwaitingAddress("Введите адрес."), "Точка 1");

            Assert.True(presentation.ShowAddressEntry);
            Assert.True(presentation.ShowSubmit);
            Assert.False(presentation.ShowCheckStatus);
            Assert.True(presentation.ShowSwitchClient);
            Assert.Equal("Отправить заявку", presentation.SubmitText);
            Assert.Equal("Точка 1", presentation.ClientName);
        }

        [Fact]
        public void Pending_UsesHumanReadableStatusAndOnlyCheckAction()
        {
            DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
                DeviceRegistrationStartupResult.Pending("raw", new DeviceRegistrationStatus
                {
                    Status = "Pending", RequestedAtUtc = new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.Zero)
                }), "Точка 1");

            Assert.Equal("Ожидает подтверждения", presentation.StatusText);
            Assert.Equal("После подтверждения HonestFlow продолжит запуск автоматически.", presentation.GuidanceText);
            Assert.True(presentation.ShowCheckStatus);
            Assert.False(presentation.ShowSubmit);
            Assert.False(presentation.ShowSwitchClient);
            Assert.False(presentation.ShowAddressEntry);
            Assert.False(string.IsNullOrWhiteSpace(presentation.RequestedAtText));
        }

        [Fact]
        public void Rejected_ShowsReasonResubmitAndSafeSwitchClientAction()
        {
            DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
                DeviceRegistrationStartupResult.Rejected("raw", new DeviceRegistrationStatus
                {
                    Status = "Rejected", Comment = "Отклонено оператором HonestDesk"
                }), "Точка 1");

            Assert.Equal("Отклонена", presentation.StatusText);
            Assert.Equal("Заявка отклонена оператором.", presentation.RejectionReason);
            Assert.True(presentation.ShowAddressEntry);
            Assert.True(presentation.ShowSubmit);
            Assert.Equal("Отправить повторно", presentation.SubmitText);
            Assert.True(presentation.ShowSwitchClient);
            Assert.False(presentation.ShowCheckStatus);
        }

        [Fact]
        public void RejectedWithoutComment_UsesExplicitFallbackReason()
        {
            DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
                DeviceRegistrationStartupResult.Rejected("raw", new DeviceRegistrationStatus { Status = "Rejected" }),
                "Точка 1");

            Assert.Equal("Заявка отклонена оператором.", presentation.RejectionReason);
        }

        [Fact]
        public void EmptyContext_IsCollapsed()
        {
            DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
                DeviceRegistrationStartupResult.AwaitingAddress("Введите адрес."), null);

            Assert.False(presentation.HasContext);
        }
    }
}
