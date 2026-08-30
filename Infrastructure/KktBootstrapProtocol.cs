using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation;

namespace HonestFlow.Infrastructure
{
    public sealed class KktBootstrapProtocolEvent
    {
        private KktBootstrapProtocolEvent(string name, IReadOnlyDictionary<string, string> fields)
        {
            Name = name;
            Fields = fields;
        }

        public string Name { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }
        public string Value(string key) => Fields.TryGetValue(key, out string value) ? value : null;

        public static KktBootstrapProtocolEvent Parse(string line)
        {
            string[] parts = Split(line ?? string.Empty).ToArray();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in parts.Skip(1))
            {
                int separator = part.IndexOf('=');
                if (separator > 0)
                    fields[part.Substring(0, separator)] = Unescape(part.Substring(separator + 1));
            }
            return new KktBootstrapProtocolEvent(parts.FirstOrDefault() ?? string.Empty, fields);
        }

        public KktBootstrapStartResult ToStartFailure()
        {
            string code = Value("code") ?? "HELPER_FAILED";
            KktBootstrapStartStatus status = code switch
            {
                "PORT_BUSY" => KktBootstrapStartStatus.PortBusy,
                "DRIVER_VERSION_MISMATCH" => KktBootstrapStartStatus.DriverVersionMismatch,
                _ => KktBootstrapStartStatus.Failed
            };
            return KktBootstrapStartResult.Error(status, code, Value("message"));
        }

        public KktBootstrapConnectionInfo ToConnectionInfo() => new()
        {
            Architecture = Value("arch"),
            DriverVersion = Value("driverVersion"),
            Model = Value("model"),
            MaskedSerial = MaskIdentifier(Value("serial")),
            Firmware = Value("firmware")
        };

        private static IEnumerable<string> Split(string line)
        {
            var current = new System.Text.StringBuilder();
            bool escaped = false;
            foreach (char value in line)
            {
                if (escaped)
                {
                    current.Append('\\');
                    current.Append(value);
                    escaped = false;
                }
                else if (value == '\\')
                {
                    escaped = true;
                }
                else if (value == '|')
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else current.Append(value);
            }
            if (escaped) current.Append('\\');
            yield return current.ToString();
        }

        private static string Unescape(string value) => (value ?? string.Empty)
            .Replace("\\|", "|")
            .Replace("\\\\", "\\");

        private static string MaskIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.Length <= 4 ? "****" : new string('*', trimmed.Length - 4) + trimmed.Substring(trimmed.Length - 4);
        }
    }
}
