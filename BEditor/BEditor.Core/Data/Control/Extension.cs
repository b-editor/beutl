using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;

namespace BEditor.Core.Data.Control
{
    public static class Extension
    {
        public static IObservable<PropertyChangedEventArgs> ObserveProperty(this INotifyPropertyChanged self, string propertyName) =>
            Observable.FromEvent<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                h => (s, e) => h(e),
                h => self.PropertyChanged += h,
                h => self.PropertyChanged -= h)
                .Where(e => e.PropertyName == propertyName);

        public static IObservable<T1> ObserveProperty<T1, T2>(this T1 self, Expression<Func<T1, T2>> propertyName) where T1 : INotifyPropertyChanged
        {
            var name = ((MemberExpression)propertyName.Body).Member.Name;
            
            return Observable.FromEvent<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                h => (s, e) => h(e),
                h => self.PropertyChanged += h,
                h => self.PropertyChanged -= h)
                .Where(e => e.PropertyName == name)
                .Select(_ => self);
        }
    }
}
