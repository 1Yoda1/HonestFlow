using System;
using System.Diagnostics;
using System.IO;

namespace HonestFlow.Application.Ui
{
    public sealed class ExternalApplicationLauncher
    {
        private const string KktDriverToolX64Path = @"C:\Program Files\ATOL\Drivers10\KKT\bin\fptr10_t.exe";
        private const string KktDriverToolX86Path = @"C:\Program Files (x86)\ATOL\Drivers10\KKT\bin\fptr10_t.exe";
        private const string EsmGuiPath = @"C:\Program Files\ESP\ESM\bin\esm-gui.exe";

        public void OpenKktDriver()
        {
            string path = File.Exists(KktDriverToolX64Path) ? KktDriverToolX64Path : KktDriverToolX86Path;
            Open(path);
        }

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
