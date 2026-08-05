using System;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class HelpRequestDataBuilderTests
    {
        [Fact]
        public void Build_MapsClientLicenseAndPointStatusOutsideTheForm()
        {
            var now = new DateTimeOffset(2026, 8, 6, 9, 8, 7, TimeSpan.Zero);
            var builder = new HelpRequestDataBuilder(() => now, () => true);
            var context = new HelpRequestDataContext
            {
                SelectedClient = new IPData
                {
                    ClientId = "client-1",
                    Name = "ИП Тест",
                    Inn = "123456789012"
                },
                RuDesktopId = "123456789",
                HonestFlowVersion = "3.0.0",
                FiscalAddress = "  Москва  ",
                ProblemType = "ККТ",
                Message = "Не работает",
                PointStatusCheckedAt = now.AddMinutes(-1),
                PointStatus = new PointStatusResult
                {
                    Kkt = new NodeStatus(
                        NodeLevel.Error,
                        "Нет связи",
                        "ККТ недоступна",
                        new[] { new ServiceSnapshot("KktService", "Stopped") })
                },
                LicenseSnapshot = new LicenseObservationSnapshot
                {
                    DeviceId = "device-1",
                    Decision = LicenseDecision.Allowed,
                    Revision = 42
                }
            };

            HelpRequestData result = builder.Build(context);

            Assert.Equal("ИП Тест", result.ClientName);
            Assert.Equal("12********12", result.InnMasked);
            Assert.Equal("client-1", result.ClientId);
            Assert.Equal("device-1", result.DeviceId);
            Assert.Equal("Allowed", result.LicenseDecision);
            Assert.Equal(42, result.LicenseRevision);
            Assert.Equal("Москва", result.FiscalAddress);
            Assert.Equal("3.0.0", result.HonestFlowVersion);
            Assert.True(result.IsAdministrator);
            Assert.Equal(now.ToString("o"), result.CreatedAt);
            Assert.StartsWith("20260806-090807-", result.RequestId);
            Assert.Equal("Нет связи", result.PointStatus.Kkt.ShortText);
            Assert.Equal("KktService", result.PointStatus.Kkt.Services[0].Name);
        }

        [Fact]
        public void Build_UsesRememberedClientWhenNoClientIsSelected()
        {
            var now = new DateTimeOffset(2026, 8, 6, 9, 8, 7, TimeSpan.Zero);
            var builder = new HelpRequestDataBuilder(() => now, () => false);
            var context = new HelpRequestDataContext
            {
                LastClient = new LastAuthorizedClientState
                {
                    ClientId = "remembered-client",
                    Name = "Сохранённая точка",
                    Inn = "1234"
                },
                PointStatusCheckedAt = now
            };

            HelpRequestData result = builder.Build(context);

            Assert.Equal("remembered-client", result.ClientId);
            Assert.Equal("Сохранённая точка", result.ClientName);
            Assert.Equal("****", result.InnMasked);
            Assert.Equal("-", result.RuDesktopId);
            Assert.False(result.IsAdministrator);
        }
    }
}
