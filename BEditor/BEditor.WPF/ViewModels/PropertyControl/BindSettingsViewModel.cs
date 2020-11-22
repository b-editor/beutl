using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Data;

namespace BEditor.ViewModels.PropertyControl
{
    public class BindSettingsViewModel<T> : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs usetwowatArgs = new(nameof(UseTwoWay));
        private bool useTwoWay;

        public IBindable<T> Bindable { get; }
        public bool UseTwoWay
        {
            get => useTwoWay;
            set => SetValue(value, ref useTwoWay, usetwowatArgs);
        }


        public BindSettingsViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
        }
    }
}
