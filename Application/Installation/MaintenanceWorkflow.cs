using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Infrastructure;
using HonestFlow.Models;

namespace HonestFlow.Application.Installation
{
    public sealed class MaintenanceWorkflow
    {
        private readonly ComponentInstallationWorkflow _componentWorkflow;
        private readonly LmDatabaseRestoreService _databaseRestoreService;
        private readonly Func<bool> _isAdministrator;

        public MaintenanceWorkflow(
            ComponentInstallationWorkflow componentWorkflow,
            LmDatabaseRestoreService databaseRestoreService)
            : this(componentWorkflow, databaseRestoreService, Utils.IsAdministrator)
        {
        }

        public MaintenanceWorkflow(
            ComponentInstallationWorkflow componentWorkflow,
            LmDatabaseRestoreService databaseRestoreService,
            Func<bool> isAdministrator)
        {
            _componentWorkflow = componentWorkflow ?? throw new ArgumentNullException(nameof(componentWorkflow));
            _databaseRestoreService = databaseRestoreService ?? throw new ArgumentNullException(nameof(databaseRestoreService));
            _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        }

        public bool CanModifySystem() => _isAdministrator();

        public Task<bool> ReinstallAsync(
            IPData selectedClient,
            IReadOnlyCollection<InstallationComponent> components) =>
            _componentWorkflow.ReinstallAsync(selectedClient, components);

        public Task<bool> RestoreLmDatabaseAsync(IPData selectedClient) =>
            _databaseRestoreService.Restore(selectedClient);
    }
}
