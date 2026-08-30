using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.UI;

internal static class WpfKktBootstrapComposition
{
    public static KktBootstrapWorkflow Create(
        Window owner,
        StartupResult startup,
        IPData client,
        TsPiotRegistrationWorkflow registration,
        PointStatusRefreshService pointStatusRefresh,
        IProgressService progress,
        Func<ILicenseOperationGuard> createLicenseGuard,
        Action<PointStatusRefreshResult>? applyRefresh = null)
    {
        var esmClient = new EsmRestStatusClient();
        var pointRepair = new PointRepairWorkflow(
            new WindowsServiceControlService(createLicenseGuard()),
            new LicensedLmInitializationService(createLicenseGuard()));

        async Task<PointStatusRefreshResult> RefreshAsync(CancellationToken token)
        {
            PointStatusRefreshResult refresh = await pointStatusRefresh.RefreshAsync(
                client,
                startup.RemoteVersions,
                includeLicensedComponents: true,
                token);
            if (applyRefresh != null)
                await owner.Dispatcher.InvokeAsync(() => applyRefresh(refresh));
            return refresh;
        }

        return new KktBootstrapWorkflow(
            new KktBootstrapProcessClient(),
            esmClient,
            registration,
            async token =>
            {
                PointStatusRefreshResult refresh = await RefreshAsync(token);
                await pointRepair.RestartServicesAsync(
                    refresh.PointStatus.EsmServiceStatus ?? refresh.PointStatus.Esm,
                    LicenseOperation.ManageServices,
                    token);
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            },
            async token => (await RefreshAsync(token)).Diagnostics,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await owner.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(owner,
                        "ТС ПИоТ успешно зарегистрирован.\n\n" +
                        "Дождаться создания контролируемого канала связи с ГИС МТ?\n\n" +
                        "Это может занять до 5 минут. Во время ожидания HonestFlow будет поддерживать соединение с ККТ.\n\n" +
                        "Да — дождаться готовности.\nНет — завершить сейчас.",
                        "Подготовка рабочей точки",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes);
            },
            progress);
    }
}
