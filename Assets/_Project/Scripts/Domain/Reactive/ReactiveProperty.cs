using System;
using System.Collections.Generic;

namespace Game.Domain.Reactive
{
    public interface IReadOnlyReactiveProperty<out T>
    {
        T Value { get; }

        /// <summary>Subscribes to changes. When <paramref name="notifyCurrent"/> is set, the handler is invoked immediately.</summary>
        IDisposable Subscribe(Action<T> handler, bool notifyCurrent = true);
    }

    /// <summary>Minimal observable value (third-party reactive libraries are not allowed in this project).</summary>
    public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>
    {
        private readonly List<Action<T>> _handlers = new List<Action<T>>();
        private readonly IEqualityComparer<T> _comparer;
        private T _value;

        public ReactiveProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (_comparer.Equals(_value, value))
                {
                    return;
                }

                _value = value;
                // Copy so handlers may unsubscribe while being notified.
                Action<T>[] snapshot = _handlers.ToArray();
                foreach (Action<T> handler in snapshot)
                {
                    handler(value);
                }
            }
        }

        public IDisposable Subscribe(Action<T> handler, bool notifyCurrent = true)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers.Add(handler);
            if (notifyCurrent)
            {
                handler(_value);
            }

            return new Subscription(this, handler);
        }

        private sealed class Subscription : IDisposable
        {
            private ReactiveProperty<T> _owner;
            private readonly Action<T> _handler;

            public Subscription(ReactiveProperty<T> owner, Action<T> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                _owner?._handlers.Remove(_handler);
                _owner = null;
            }
        }
    }

    /// <summary>Collects subscriptions and disposes them together.</summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();

        public void Add(IDisposable item) => _items.Add(item);

        public void Dispose()
        {
            foreach (IDisposable item in _items)
            {
                item.Dispose();
            }

            _items.Clear();
        }
    }

    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, CompositeDisposable composite) where T : IDisposable
        {
            composite.Add(disposable);
            return disposable;
        }
    }
}
