namespace ARIS1.Services
{
    // IProgress<T> for Blazor pages: every report updates the page's fields, but the (expensive) re-render callback
    // runs at most once per `minInterval`, so 30 quick subject completions don't flood the SignalR circuit.
    // Reports can arrive on any thread, hence the lock; the caller's render callback is responsible for InvokeAsync.
    public sealed class ThrottledProgress<T> : IProgress<T>
    {
        private readonly Action<T> _apply;
        private readonly Action _render;
        private readonly TimeSpan _minInterval;
        private readonly object _lock = new();
        private DateTime _lastRender = DateTime.MinValue;

        public ThrottledProgress(Action<T> apply, Action render, TimeSpan? minInterval = null)
        {
            _apply = apply;
            _render = render;
            _minInterval = minInterval ?? TimeSpan.FromMilliseconds(150);
        }

        public void Report(T value)
        {
            bool render;
            lock (_lock)
            {
                _apply(value);
                var now = DateTime.UtcNow;
                render = now - _lastRender >= _minInterval;
                if (render) _lastRender = now;
            }
            if (render) _render();
        }
    }
}
