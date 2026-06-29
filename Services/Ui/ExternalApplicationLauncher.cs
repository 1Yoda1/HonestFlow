using System;
using System.Diagnostics;
using System.IO;

namespace HonestFlow.Services.Ui
{
    public sealed class ExternalApplicationLauncher
    {
        private const string KktDriverToolPath = @"C:\Program Files (x86)\ATOL\Drivers10\KKT\bin\fptr10_t.exe";
        private const string EsmGuiPath = @"C:\Program Files\ESP\ESM\bin\esm-gui.exe";

        public void OpenKktDriver() => Open(KktDriverToolPath);

        public void OpenEsm() => Open(EsmGuiPath);

        public void Open(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Не найден файл", path);

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true
            });
        }
    }
}
