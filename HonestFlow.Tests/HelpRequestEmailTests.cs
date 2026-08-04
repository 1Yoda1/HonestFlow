using System;
using System.Reflection;
using HonestFlow.Application.RemoteAccess;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class HelpRequestEmailTests
    {
        [Fact]
        public void BuildBody_IncludesPointStatusesServicesAndLicenseContext()
        {
            var request = new HelpRequestData
            {
                RequestId = "REQ-1",
                ClientName = "Client",
                RuDesktopId = "123456789",
                LicenseDecision = "Allowed",
                LicenseTechnicalCode = "Allowed",
                LicenseSource = "Remote",
                LicenseRevision = 42,
                PointStatus = new HelpRequestPointStatus
                {
                    CheckedAt = "2026-08-05T12:00:00+06:00",
                    Lm = new HelpRequestNodeStatus
                    {
                        Level = "Ok",
                        ShortText = "2.5.1",
                        Details = "API ready",
                        Services = new[]
                        {
                            new HelpRequestServiceStatus { Name = "regime", State = "Running" }
                        }
                    },
                    Esm = new HelpRequestNodeStatus
                    {
                        Level = "Warning",
                        ShortText = "Требует внимания",
                        Services = new[]
                        {
                            new HelpRequestServiceStatus { Name = "esm-cm-shop-42", State = "Stopped" }
                        }
                    }
                },
                CreatedAt = DateTimeOffset.Now.ToString("o")
            };

            MethodInfo method = typeof(HelpRequestEmailSender).GetMethod(
                "BuildBody",
                BindingFlags.NonPublic | BindingFlags.Static);
            string body = (string)method.Invoke(null, new object[] { request });

            Assert.Contains("License: Allowed", body);
            Assert.Contains("LicenseRevision: 42", body);
            Assert.Contains("LM: [Ok] 2.5.1", body);
            Assert.Contains("Service regime: Running", body);
            Assert.Contains("Service esm-cm-shop-42: Stopped", body);
            Assert.Contains("\"PointStatus\"", body);
        }

        [Fact]
        public void BuildBody_IncludesStatusCollectionFailureWithoutDroppingRequest()
        {
            var request = new HelpRequestData
            {
                RequestId = "REQ-2",
                PointStatus = new HelpRequestPointStatus
                {
                    CheckedAt = "2026-08-05T12:00:00+06:00",
                    Error = "Service snapshot unavailable"
                }
            };

            MethodInfo method = typeof(HelpRequestEmailSender).GetMethod(
                "BuildBody",
                BindingFlags.NonPublic | BindingFlags.Static);
            string body = (string)method.Invoke(null, new object[] { request });

            Assert.Contains("Error: Service snapshot unavailable", body);
            Assert.Contains("RuDesktop: -", body);
        }
    }
}
