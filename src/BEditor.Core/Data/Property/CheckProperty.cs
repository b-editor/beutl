using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// チェックボックスのプロパティを表します
    /// </summary>
    [DataContract]
    public class CheckProperty : PropertyElement<CheckPropertyMetadata>, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _CheckedArgs = new(nameof(IsChecked));
        private bool _IsChecked;
        private List<IObserver<bool>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<bool>? _Bindable;
        private string? _BindHint;

        #endregion


        private List<IObserver<bool>> Collection => _List ??= new();
        /// <summary>
        /// チェックされている場合 <see langword="true"/>、そうでない場合は <see langword="false"/> となります
        /// </summary>
        [DataMember]
        public bool IsChecked
        {
            get => _IsChecked;
            set => SetValue(value, ref _IsChecked, _CheckedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._IsChecked);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _Bindable?.GetString();
            private set => _BindHint = value;
        }
        /// <inheritdoc/>
        public bool Value => IsChecked;


        /// <summary>
        /// <see cref="CheckProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="CheckPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _IsChecked = metadata.DefaultIsChecked;
        }


        #region Methods

        #region IBindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            IsChecked = value;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), o =>
            {
                o.observer.OnCompleted();
                o.Item2.Collection.Remove(o.observer);
            });
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                IsChecked = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_BindHint is not null)
            {
                if (this.GetBindable(_BindHint, out var b))
                {
                    Bind(b);
                }
            }
            _BindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";

        #endregion


        /// <summary>
        /// チェックされているかを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeCheckedCommand : IRecordCommand
        {
            private readonly CheckProperty _Property;
            private readonly bool _Value;

            /// <summary>
            /// <see cref="ChangeCheckedCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="CheckProperty"/></param>
            /// <param name="value">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeCheckedCommand(CheckProperty property, bool value)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                this._Value = value;
            }

            public string Name => CommandName.ChangeIsChecked;

            /// <inheritdoc/>
            public void Do() => _Property.IsChecked = _Value;
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => _Property.IsChecked = !_Value;
        }
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.CheckProperty"/> のメタデータを表します
    /// </summary>
    public record CheckPropertyMetadata(string Name, bool DefaultIsChecked = false) : PropertyElementMetadata(Name);
}
