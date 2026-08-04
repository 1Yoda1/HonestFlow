using System.Reflection;
using System.Threading;
using System;
using System.IO;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LoggerSecurityTests
    {
        [Fact]
        public void MaskSecrets_HidesActualSecretsButKeepsInn()
        {
            string source =
                "token=lm-secret password:4827 SMTPPassword=mail-secret " +
                "ИНН: 123456789012 " +
                "{\"apiKey\":\"json-secret\",\"password\":\"json-password\"}";

            string result = InvokeMaskSecrets(source);

            Assert.DoesNotContain("lm-secret", result);
            Assert.DoesNotContain("4827", result);
            Assert.DoesNotContain("mail-secret", result);
            Assert.DoesNotContain("json-secret", result);
            Assert.DoesNotContain("json-password", result);
            Assert.Contains("123456789012", result);
        }

        [Fact]
        public void FormatLine_IncludesCurrentOperationId()
        {
            FieldInfo field = typeof(Logger).GetField(
                "CurrentOperationId",
                BindingFlags.NonPublic | BindingFlags.Static);
            var operation = (AsyncLocal<string>)field.GetValue(null);
            string previous = operation.Value;

            try
            {
                operation.Value = "abc12345";
                MethodInfo format = typeof(Logger).GetMethod(
                    "FormatLine",
                    BindingFlags.NonPublic | BindingFlags.Static);
                string line = (string)format.Invoke(
                    null,
                    new object[] { "INFO", "message", "Test" });

                Assert.Contains("[OP:abc12345]", line);
            }
            finally
            {
                operation.Value = previous;
            }
        }

        [Fact]
        public void SessionLogPath_UsesSessionPrefix()
        {
            MethodInfo method = typeof(Logger).GetMethod(
                "CreateSessionLogPath",
                BindingFlags.NonPublic | BindingFlags.Static);

            string path = (string)method.Invoke(
                null,
                new object[] { new DateTime(2026, 8, 5, 12, 34, 56) });

            Assert.Equal("session_2026-08-05_12-34-56.log", Path.GetFileName(path));
        }

        [Fact]
        public void SessionHeader_IsCompactAndDoesNotRepeatFolders()
        {
            MethodInfo method = typeof(Logger).GetMethod(
                "BuildSessionHeader",
                BindingFlags.NonPublic | BindingFlags.Static);

            string header = (string)method.Invoke(
                null,
                new object[] { new DateTime(2026, 8, 5, 12, 34, 56) });

            Assert.Contains("HONESTFLOW SESSION START", header);
            Assert.Contains("Архитектура:", header);
            Assert.Contains("Файл журнала:", header);
            Assert.DoesNotContain("ProgramDataFolder", header);
            Assert.DoesNotContain("LogsFolder", header);
            Assert.DoesNotContain("64-bit OS", header);
            Assert.DoesNotContain("64-bit process", header);
        }

        [Fact]
        public void OperationCompletion_IsNeutralAboutResult()
        {
            MethodInfo method = typeof(Logger).GetMethod(
                "FormatOperationCompletion",
                BindingFlags.NonPublic | BindingFlags.Static);

            string message = (string)method.Invoke(
                null,
                new object[] { "Тест", TimeSpan.FromSeconds(1.25) });

            Assert.Contains("выполнение закончено", message);
            Assert.DoesNotContain("успеш", message, StringComparison.OrdinalIgnoreCase);
        }

        private static string InvokeMaskSecrets(string value)
        {
            MethodInfo method = typeof(Logger).GetMethod(
                "MaskSecrets",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (string)method.Invoke(null, new object[] { value });
        }
    }
}
