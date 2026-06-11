using HonestFlow.Models;
using HonestFlow.Services.Installation.Planning;

namespace HonestFlow.Services.Installation
{
    /// <summary>
    /// Единое место, где формируются имена дистрибутивов из versions.json.
    /// </summary>
    public static class InstallerFileNameBuilder
    {
        public static void FillFileNames(InstallationPlan plan, IPData selectedIP, VersionsData versions)
        {
            foreach (var item in plan.Items)
            {
                switch (item.Component)
                {
                    case InstallationComponent.LmModule:
                        item.FileName = $"regime-{versions.LmModule}.msi";
                        break;
                    case InstallationComponent.AtolDriver:
                        item.FileName = selectedIP.Architecture == "x64"
                            ? $"KKT10-{versions.AtolDriver}-windows64-setup-signed.exe"
                            : $"KKT10-{versions.AtolDriver}-windows32-setup-signed.exe";
                        break;
                    case InstallationComponent.Esm:
                        item.FileName = $"esm_{versions.ESM}-windows-signed-setup.exe";
                        break;
                    case InstallationComponent.Controller:
                        item.FileName = $"esm-lm-controller_{versions.Controller}-windows-setup.exe";
                        break;
                }
            }
        }
    }
}
