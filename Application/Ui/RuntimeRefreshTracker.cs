using System.Collections.Generic;

namespace HonestFlow.Application.Ui;

/// <summary>Rejects results from an old runtime or an older request for the same view.</summary>
public sealed class RuntimeRefreshTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _requests = new();
    private long _generation;
    private long _nextRequest;

    public long Generation
    {
        get { lock (_sync) return _generation; }
    }

    public void Advance()
    {
        lock (_sync)
        {
            _generation++;
            _requests.Clear();
        }
    }

    public bool IsCurrent(long generation)
    {
        lock (_sync) return generation == _generation;
    }

    public Request Begin(string view)
    {
        lock (_sync)
        {
            long sequence = ++_nextRequest;
            _requests[view] = sequence;
            return new Request(this, view, _generation, sequence);
        }
    }

    public sealed class Request
    {
        private readonly RuntimeRefreshTracker _owner;
        private readonly string _view;
        private readonly long _generation;
        private readonly long _sequence;

        internal Request(RuntimeRefreshTracker owner, string view, long generation, long sequence)
        {
            _owner = owner;
            _view = view;
            _generation = generation;
            _sequence = sequence;
        }

        public bool IsCurrent
        {
            get
            {
                lock (_owner._sync)
                    return _owner._generation == _generation &&
                        _owner._requests.TryGetValue(_view, out long sequence) && sequence == _sequence;
            }
        }
    }
}
