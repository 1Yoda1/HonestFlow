using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Bootstrap;

/// <summary>
/// Orders the public trusted update check before local Free composition.
/// The two narrow delegates are intentional test seams, not a service locator.
/// </summary>
public sealed class FreeApplicationStartup
{
    private readonly Func<CancellationToken, Task<bool>> _runPublicSelfUpdate;
    private readonly Func<LocalApplicationContext> _createLocalContext;

    public FreeApplicationStartup(
        Func<CancellationToken, Task<bool>> runPublicSelfUpdate,
        Func<LocalApplicationContext> createLocalContext)
    {
        _runPublicSelfUpdate = runPublicSelfUpdate ?? throw new ArgumentNullException(nameof(runPublicSelfUpdate));
        _createLocalContext = createLocalContext ?? throw new ArgumentNullException(nameof(createLocalContext));
    }

    public async Task<FreeApplicationStartupResult> StartAsync(CancellationToken cancellationToken)
    {
        bool updateStarted = await _runPublicSelfUpdate(cancellationToken);
        return updateStarted
            ? FreeApplicationStartupResult.ForUpdate()
            : FreeApplicationStartupResult.Ready(_createLocalContext());
    }
}

public sealed class FreeApplicationStartupResult
{
    private FreeApplicationStartupResult(bool updateStarted, LocalApplicationContext context)
    {
        UpdateStarted = updateStarted;
        Context = context;
    }

    public bool UpdateStarted { get; }
    public LocalApplicationContext Context { get; }

    public static FreeApplicationStartupResult ForUpdate() => new(true, null);
    public static FreeApplicationStartupResult Ready(LocalApplicationContext context) =>
        new(false, context ?? throw new ArgumentNullException(nameof(context)));
}
