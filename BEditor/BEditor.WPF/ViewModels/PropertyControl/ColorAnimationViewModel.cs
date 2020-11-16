using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorAnimationViewModel
    {
        public ColorAnimationProperty Property { get; }
        public DelegateCommand<EasingData> EasingChangeCommand { get; }

        public ColorAnimationViewModel(ColorAnimationProperty property)
        {
            Property = property;
            EasingChangeCommand = new DelegateCommand<EasingData>(x =>
            {
                UndoRedoManager.Do(new ColorAnimationProperty.ChangeEaseCommand(property, x.Name));
            });
        }
    }
}
