// BindingHelper.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// バインディング関係の内部メソッドを提供します.
    /// </summary>
    internal static class BindingHelper
    {
        /// <summary>
        /// <see cref="IElementObject.Load"/> 時に自動で <see cref="IBindable{T}.Bind(IBindable{T}?)"/> を呼び出す必要があれば呼び出します.
        /// </summary>
        /// <typeparam name="T">バインドするオブジェクト.</typeparam>
        /// <param name="bindable">バインドソースのインスタンス.</param>
        /// <param name="id">バインドターゲットのID.</param>
        public static void AutoLoad<T>(this IBindable<T> bindable, ref Guid? id)
        {
            if (id is not null && bindable.GetBindable(id, out var b))
            {
                bindable.Bind(b);
            }

            id = null;
        }

        /// <summary>
        /// <see cref="IObservable{T}.Subscribe(IObserver{T})"/> の動作.
        /// </summary>
        /// <typeparam name="T">通知情報を提供するオブジェクト.</typeparam>
        /// <param name="list"><paramref name="observer"/> を追加するリスト.</param>
        /// <param name="observer">購読する <see cref="IObserver{T}"/> のインスタンス.</param>
        /// <param name="value">最初の通知の値.</param>
        /// <returns>プロバイダが通知の送信を完了する前に、オブザーバが通知の受信を停止できるようにするインターフェースへの参照.</returns>
        public static IDisposable Subscribe<T>(IList<IObserver<T>> list, IObserver<T> observer, T value)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            list.Add(observer);

            try
            {
                observer.OnNext(value);
            }
            catch (Exception e)
            {
                observer.OnError(e);
            }

            return Disposable.Create((observer, list), o =>
            {
                o.observer.OnCompleted();
                o.list.Remove(o.observer);
            });
        }

        /// <summary>
        /// <see cref="IBindable{T}.Bind(IBindable{T}?)"/> の動作.
        /// </summary>
        /// <typeparam name="T">バインドするオブジェクト.</typeparam>
        /// <param name="bindable1">バインドソース.</param>
        /// <param name="bindable2">バインドターゲット.</param>
        /// <param name="outbindable"><paramref name="bindable2"/> が設定されます.</param>
        /// <param name="disposable">オブザーバが通知の受信を停止できるようにするインターフェースへの参照.</param>
        /// <returns><paramref name="bindable2"/> 又は <paramref name="bindable1"/> の値.</returns>
        public static T Bind<T>(this IBindable<T> bindable1, IBindable<T>? bindable2, out IBindable<T>? outbindable, ref IDisposable? disposable)
        {
            disposable?.Dispose();
            outbindable = bindable2;

            if (bindable2 is not null)
            {
                var value = bindable2.Value;

                // bindableが変更時にthisが変更
                disposable = bindable2.Subscribe(bindable1);

                return value;
            }

            return bindable1.Value;
        }
    }
}