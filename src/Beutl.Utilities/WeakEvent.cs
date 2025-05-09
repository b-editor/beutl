﻿using System.Runtime.CompilerServices;

namespace Beutl.Utilities;

public class WeakEvent<TSender, TEventArgs> : WeakEvent where TEventArgs : EventArgs where TSender : class
{
    private readonly Func<TSender, EventHandler<TEventArgs>, Action> _subscribe;

    readonly ConditionalWeakTable<object, Subscription> _subscriptions = [];

    internal WeakEvent(
        Action<TSender, EventHandler<TEventArgs>> subscribe,
        Action<TSender, EventHandler<TEventArgs>> unsubscribe)
    {
        _subscribe = (t, s) =>
        {
            subscribe(t, s);
            return () => unsubscribe(t, s);
        };
    }

    internal WeakEvent(Func<TSender, EventHandler<TEventArgs>, Action> subscribe)
    {
        _subscribe = subscribe;
    }

    public void Subscribe(TSender target, IWeakEventSubscriber<TEventArgs> subscriber)
    {
        if (!_subscriptions.TryGetValue(target, out WeakEvent<TSender, TEventArgs>.Subscription? subscription))
        {
            _subscriptions.Add(target, subscription = new Subscription(this, target));
        }

        subscription.Add(new WeakReference<IWeakEventSubscriber<TEventArgs>>(subscriber));
    }

    public void Unsubscribe(TSender target, IWeakEventSubscriber<TEventArgs> subscriber)
    {
        if (_subscriptions.TryGetValue(target, out WeakEvent<TSender, TEventArgs>.Subscription? subscription))
        {
            subscription.Remove(subscriber);
        }
    }

    private class Subscription
    {
        private readonly WeakEvent<TSender, TEventArgs> _ev;
        private readonly TSender _target;

        private WeakReference<IWeakEventSubscriber<TEventArgs>>?[] _data =
            new WeakReference<IWeakEventSubscriber<TEventArgs>>[16];
        private int _count;
        private readonly Action _unsubscribe;
        private bool _compactScheduled;

        public Subscription(WeakEvent<TSender, TEventArgs> ev, TSender target)
        {
            _ev = ev;
            _target = target;
            _unsubscribe = ev._subscribe(target, OnEvent);
        }

        private void Destroy()
        {
            _unsubscribe();
            _ev._subscriptions.Remove(_target);
        }

        public void Add(WeakReference<IWeakEventSubscriber<TEventArgs>> s)
        {
            if (_count == _data.Length)
            {
                //Extend capacity
                var extendedData = new WeakReference<IWeakEventSubscriber<TEventArgs>>?[_data.Length * 2];
                Array.Copy(_data, extendedData, _data.Length);
                _data = extendedData;
            }

            _data[_count] = s;
            _count++;
        }

        public void Remove(IWeakEventSubscriber<TEventArgs> s)
        {
            bool removed = false;

            for (int c = 0; c < _count; ++c)
            {
                if (_data[c] is WeakReference<IWeakEventSubscriber<TEventArgs>> reference &&
                    reference.TryGetTarget(out IWeakEventSubscriber<TEventArgs>? instance) &&
                    instance == s)
                {
                    _data[c] = null;
                    removed = true;
                }
            }

            if (removed)
            {
                ScheduleCompact();
            }
        }

        private void ScheduleCompact()
        {
            if (_compactScheduled)
                return;
            _compactScheduled = true;
            Compact();
        }

        private void Compact()
        {
            _compactScheduled = false;
            int empty = -1;
            for (int c = 0; c < _count; c++)
            {
                WeakReference<IWeakEventSubscriber<TEventArgs>>? r = _data[c];
                //Mark current index as first empty
                if (r == null && empty == -1)
                    empty = c;
                //If current element isn't null and we have an empty one
                if (r != null && empty != -1)
                {
                    _data[c] = null;
                    _data[empty] = r;
                    empty++;
                }
            }

            if (empty != -1)
                _count = empty;
            if (_count == 0)
                Destroy();
        }

        private void OnEvent(object? sender, TEventArgs eventArgs)
        {
            bool needCompact = false;
            for (int c = 0; c < _count; c++)
            {
                if (_data[c]?.TryGetTarget(out IWeakEventSubscriber<TEventArgs>? sub) == true)
                    sub.OnEvent(_target, _ev, eventArgs);
                else
                    needCompact = true;
            }

            if (needCompact)
                ScheduleCompact();
        }
    }

}

public class WeakEvent
{
    public static WeakEvent<TSender, TEventArgs> Register<TSender, TEventArgs>(
        Action<TSender, EventHandler<TEventArgs>> subscribe,
        Action<TSender, EventHandler<TEventArgs>> unsubscribe) where TSender : class where TEventArgs : EventArgs
    {
        return new WeakEvent<TSender, TEventArgs>(subscribe, unsubscribe);
    }

    public static WeakEvent<TSender, TEventArgs> Register<TSender, TEventArgs>(
        Func<TSender, EventHandler<TEventArgs>, Action> subscribe) where TSender : class where TEventArgs : EventArgs
    {
        return new WeakEvent<TSender, TEventArgs>(subscribe);
    }

    public static WeakEvent<TSender, EventArgs> Register<TSender>(
        Action<TSender, EventHandler> subscribe,
        Action<TSender, EventHandler> unsubscribe) where TSender : class
    {
        return Register<TSender, EventArgs>((s, h) =>
        {
            void handler(object? _, EventArgs e) => h(s, e);
            subscribe(s, handler);
            return () => unsubscribe(s, handler);
        });
    }
}
