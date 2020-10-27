using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Views.TimeLines;
using BEditorCore.Data;
using BEditorCore.Data.PropertyData;

namespace BEditor.Views.PropertyControls {
    /// <summary>
    /// EasePropertyControl.xaml の相互作用ロジック
    /// </summary>
    public partial class EaseControl : UserControl, ICustomTreeViewItem, ISizeChangeMarker {

        #region EaseControlメンバー

        private EaseProperty EasingSetting;

        private double OpenHeight;

        #endregion

        public event EventHandler SizeChange;
        public double LogicHeight {
            get {
                double h;
                if ((bool)togglebutton.IsChecked) {
                    h = OpenHeight;
                }
                else {
                    h = 32.5;
                }

                return h;
            }
        }


        /// <summary>
        /// Value_Anmのコンストラクタ
        /// </summary>
        /// <param name="anm">EaseSettingのインスタンス</param>
        public EaseControl(EaseProperty anm) {
            InitializeComponent();

            DataContext = new EasePropertyViewModel(anm);
            EasingSetting = anm;

            OpenHeight = (double)(OpenAnm.To = 32.5 * anm.Value.Count + 10);

            Loaded += (_, _) => {
                anm.Value.CollectionChanged += Value_CollectionChanged;

                OpenStoryboard.Children.Add(OpenAnm);
                CloseStoryboard.Children.Add(CloseAnm);

                Storyboard.SetTarget(OpenAnm, this);
                Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

                Storyboard.SetTarget(CloseAnm, this);
                Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));
            };
        }


        #region 値変更時のイベント

        private void Value_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove) {
                OpenHeight = (double)(OpenAnm.To = 32.5 * EasingSetting.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }

        #endregion

        #region View関係

        private Storyboard OpenStoryboard = new Storyboard();
        private Storyboard CloseStoryboard = new Storyboard();
        private DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };


        private void ListToggleClick(object sender, RoutedEventArgs e) {
            //開く
            if ((bool)togglebutton.IsChecked) {
                OpenStoryboard.Begin();
            }
            else {
                CloseStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        float oldvalue;


        private void TextBox_GotFocus(object sender, RoutedEventArgs e) {
            if (!IsLoaded) return;

            TextBox tb = (TextBox)sender;


            int index = (int)tb.ToolTip;

            oldvalue = EasingSetting.Value[index];
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e) {
            if (!IsLoaded) return;

            TextBox tb = (TextBox)sender;

            int index = (int)tb.ToolTip;

            if (float.TryParse(tb.Text, out float _out)) {
                EasingSetting.Value[index] = oldvalue;

                UndoRedoManager.Do(new EaseProperty.ChangeValue(EasingSetting, index, _out));
            }
        }

        #region MouseEvent
        private void TextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            TextBox textBox = (TextBox)sender;

            if (textBox.IsKeyboardFocused) {
                int index = (int)textBox.ToolTip;

                int v = 10;//定数増え幅

                if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;

                float val = float.Parse(textBox.Text);
                val += e.Delta / 120 * v;

                EasingSetting.Value[index] = EasingSetting.InRange(val);

                Component.Current.Project.PreviewUpdate(EasingSetting.ClipData);

                e.Handled = true;
            }
        }
        #endregion

        private void TextBox_KeyDown(object sender, KeyEventArgs e) {
            TextBox textBox = (TextBox)sender;
            int index = (int)textBox.ToolTip;


            if (float.TryParse(textBox.Text, out float _out)) {
                EasingSetting.Value[index] = EasingSetting.InRange(_out);

                Component.Current.Project.PreviewUpdate(EasingSetting.ClipData);
            }
        }
    }
}
