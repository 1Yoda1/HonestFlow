using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation.Planning;

namespace HonestFlow.Application.Installation
{
    public sealed class InstallationOptions
    {
        public static InstallationOptions Default { get; } = new();
        public bool SkipLmStack { get; init; }
        // Null preserves the regular full-install behavior. Auto-fix supplies the
        // affected components so an unrelated local API is not queried or changed.
        public IReadOnlyCollection<InstallationComponent> RequestedComponents { get; init; }

        public bool Includes(InstallationComponent component) =>
            RequestedComponents == null || RequestedComponents.Contains(component);

        public bool RequiresKktBootstrap =>
            RequestedComponents == null ||
            Includes(InstallationComponent.AtolDriver) ||
            Includes(InstallationComponent.Esm);
    }
}
