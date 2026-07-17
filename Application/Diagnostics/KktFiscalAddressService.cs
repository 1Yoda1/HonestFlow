using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HonestFlow.Application.Diagnostics
{
    public static class KktFiscalAddressService
    {
        public static string TryFindAddress()
        {
            string logPath = GetDefaultAtolLogPath();
            return TryFindAddress(logPath);
        }

        public static string TryFindAddress(string logPath)
        {
            if (!File.Exists(logPath))
                return null;

            try
            {
                string found = null;
                using var source = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                while (!reader.EndOfStream)
                {
                    string address = TryExtractFiscalAddress(reader.ReadLine());
                    if (!string.IsNullOrWhiteSpace(address))
                        found = address;
                }

                return found;
            }
            catch
            {
                return null;
            }
        }

        public static string GetDefaultAtolLogPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "ATOL", "drivers10", "logs", "fptr10.log");
        }

        public static string TryExtractFiscalAddress(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            string address = TryExtractAddressField(line);
            if (!string.IsNullOrWhiteSpace(address))
                return address;

            return TryExtractAddressFromTextItems(line);
        }

        private static string TryExtractAddressField(string line)
        {
            if (line.IndexOf("address", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            Match escaped = Regex.Match(line, @"\\\""address\\\""\s*:\s*\\\""(?<address>[^\\\""]+)\\\""");
            if (escaped.Success)
                return NormalizeTextValue(escaped.Groups["address"].Value);

            Match plain = Regex.Match(line, @"""address""\s*:\s*""(?<address>[^""]+)""");
            if (plain.Success)
                return NormalizeTextValue(plain.Groups["address"].Value);

            return null;
        }

        private static string TryExtractAddressFromTextItems(string line)
        {
            if (line.IndexOf(@"""text""", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            var addressParts = ExtractTextItems(line)
                .Where(IsAddressLikeText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (addressParts.Count == 0)
                return null;

            return string.Join(", ", addressParts);
        }

        private static IEnumerable<string> ExtractTextItems(string line)
        {
            foreach (Match match in Regex.Matches(line, @"""text""\s*:\s*""(?<text>(?:\\.|[^""\\])*)"""))
            {
                string text = NormalizeTextValue(match.Groups["text"].Value);
                if (!string.IsNullOrWhiteSpace(text))
                    yield return text;
            }
        }

        private static bool IsAddressLikeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsReceiptNoise(text))
                return false;

            string lower = text.ToLowerInvariant();

            return lower.Contains("г.") ||
                   lower.Contains("город") ||
                   lower.Contains("ул.") ||
                   lower.Contains("улица") ||
                   lower.Contains("пр-т") ||
                   lower.Contains("просп") ||
                   lower.Contains("пер.") ||
                   lower.Contains("переул") ||
                   lower.Contains("шоссе") ||
                   lower.Contains("бульвар") ||
                   lower.Contains("д.") ||
                   lower.Contains("дом ") ||
                   lower.Contains("обл") ||
                   lower.Contains("край") ||
                   lower.Contains("район") ||
                   lower.Contains("р-н") ||
                   lower.Contains("пос.") ||
                   lower.Contains("с.") ||
                   lower.Contains("дер.");
        }

        private static bool IsReceiptNoise(string text)
        {
            string lower = text.ToLowerInvariant();

            if (lower.Contains("чек") ||
                lower.Contains("оплата") ||
                lower.Contains("карта") ||
                lower.Contains("сумма") ||
                lower.Contains("комиссия") ||
                lower.Contains("одобрено") ||
                lower.Contains("rrn") ||
                lower.Contains("подпись") ||
                lower.Contains("идентификатор") ||
                lower.Contains("bluetooth") ||
                lower.Contains("к/а:"))
            {
                return true;
            }

            return Regex.IsMatch(text, @"^[\d\s=~S]+$");
        }

        private static string NormalizeTextValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Regex.Unescape(value).Trim();
        }
    }
}
