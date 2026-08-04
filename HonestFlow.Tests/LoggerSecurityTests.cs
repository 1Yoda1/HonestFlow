using System.Reflection;
using System.Threading;
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

        private static string InvokeMaskSecrets(string value)
        {
            MethodInfo method = typeof(Logger).GetMethod(
                "MaskSecrets",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (string)method.Invoke(null, new object[] { value });
        }
    }
}
