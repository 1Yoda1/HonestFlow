using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Atol.Drivers10.Fptr;

internal static class Program
{
#if KKT_BOOTSTRAP_X86
    private const string BuiltArchitecture = "x86";
#else
    private const string BuiltArchitecture = "x64";
#endif

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        IFptr fptr = null;
        bool opened = false;
        try
        {
            (string architecture, string requiredVersion) = Parse(args);
            if (!string.Equals(architecture, BuiltArchitecture, StringComparison.OrdinalIgnoreCase))
                return Error("HELPER_ARCH_MISMATCH", 0, $"Helper {BuiltArchitecture} не соответствует {architecture}.", 11);

            string driverPath = DriverPath(BuiltArchitecture);
            if (!File.Exists(driverPath))
                return Error("DRIVER_DLL_NOT_FOUND", 0, "Не найден fptr10.dll выбранной архитектуры.", 12);

            fptr = new Fptr(driverPath);
            string actualVersion = fptr.version();
            if (!VersionsEqual(actualVersion, requiredVersion))
                return Error("DRIVER_VERSION_MISMATCH", 0,
                    $"Требуется драйвер {requiredVersion}, фактически загружен {actualVersion}.", 13);

            fptr.setSingleSetting(Constants.LIBFPTR_SETTING_MODEL, Constants.LIBFPTR_MODEL_ATOL_AUTO.ToString());
            fptr.setSingleSetting(Constants.LIBFPTR_SETTING_PORT, Constants.LIBFPTR_PORT_USB.ToString());
            fptr.applySingleSettings();
            int openResult = fptr.open();
            if (openResult != 0 || !fptr.isOpened())
            {
                int code = fptr.errorCode();
                return code == Constants.LIBFPTR_ERROR_PORT_BUSY
                    ? Error("PORT_BUSY", code, "ККТ занята другим процессом.", 21)
                    : Error("OPEN_FAILED", code, Safe(fptr.errorDescription()), 20);
            }
            opened = true;

            fptr.setParam(Constants.LIBFPTR_PARAM_DATA_TYPE, Constants.LIBFPTR_DT_STATUS);
            if (fptr.queryData() != 0)
                return Error("KKT_QUERY_FAILED", fptr.errorCode(), Safe(fptr.errorDescription()), 22);

            Write("CONNECTED",
                "arch=" + Escape(architecture),
                "driverVersion=" + Escape(actualVersion),
                "model=" + Escape(fptr.getParamString(Constants.LIBFPTR_PARAM_MODEL_NAME)),
                "serial=" + Escape(fptr.getParamString(Constants.LIBFPTR_PARAM_SERIAL_NUMBER)),
                "firmware=" + Escape(fptr.getParamString(Constants.LIBFPTR_PARAM_UNIT_VERSION)));
            Write("WAITING_FOR_CLOSE");

            while (true)
            {
                string command = Console.ReadLine();
                if (command == null || string.IsNullOrWhiteSpace(command) ||
                    string.Equals(command.Trim(), "CLOSE", StringComparison.OrdinalIgnoreCase)) break;
                if (string.Equals(command.Trim(), "PING", StringComparison.OrdinalIgnoreCase)) Write("PONG");
            }

            fptr.close();
            opened = false;
            Write("CLOSED");
            return 0;
        }
        catch (ArgumentException ex) { return Error("INVALID_ARGUMENTS", 0, ex.Message, 10); }
        catch (Exception ex) { return Error("UNEXPECTED_ERROR", 0, ex.GetType().Name + ": " + ex.Message, 100); }
        finally
        {
            if (fptr != null && opened)
            {
                try { fptr.close(); } catch { }
            }
        }
    }

    private static (string Architecture, string Version) Parse(string[] args)
    {
        string architecture = null;
        string version = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--arch", StringComparison.OrdinalIgnoreCase) && ++i < args.Length) architecture = args[i];
            else if (string.Equals(args[i], "--version", StringComparison.OrdinalIgnoreCase) && ++i < args.Length) version = args[i];
            else throw new ArgumentException("Ожидаются --arch x86|x64 и --version <version>.");
        }
        if (architecture is not ("x86" or "x64") || string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Ожидаются --arch x86|x64 и --version <version>.");
        return (architecture, version);
    }

    private static string DriverPath(string architecture)
    {
        string root = architecture == "x86"
            ? Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            : Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(root, "ATOL", "Drivers10", "KKT", "bin", "fptr10.dll");
    }

    private static bool VersionsEqual(string actual, string required)
    {
        Match a = Regex.Match(actual ?? string.Empty, @"\d+\.\d+\.\d+\.\d+");
        Match r = Regex.Match(required ?? string.Empty, @"\d+\.\d+\.\d+\.\d+");
        return a.Success && r.Success
            ? string.Equals(a.Value, r.Value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual?.Trim(), required?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Error(string code, int driverCode, string message, int exitCode)
    {
        Write("ERROR", "code=" + Escape(code), "driverCode=" + driverCode, "message=" + Escape(message));
        return exitCode;
    }

    private static void Write(string name, params string[] fields)
    {
        Console.WriteLine(fields.Length == 0 ? name : name + "|" + string.Join("|", fields));
        Console.Out.Flush();
    }

    private static string Escape(string value) => Safe(value)
        .Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string Safe(string value) => value ?? string.Empty;
}
