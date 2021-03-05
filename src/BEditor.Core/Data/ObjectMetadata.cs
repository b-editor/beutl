using System;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Data
{
    //Todo: Document
#pragma warning disable CS1591
    public record ObjectMetadata(string Name, Func<ObjectElement> CreateFunc, Type Type)
    {
        public ObjectMetadata(string Name, Expression<Func<ObjectElement>> Create) : this(Name, Create.Compile(), ((NewExpression)Create.Body).Type)
        {

        }


        public static ObservableCollection<ObjectMetadata> LoadedObjects { get; } = new();


        public static ObjectMetadata Create<T>(string Name) where T : ObjectElement, new()
        {
            return new(Name, () => new T(), typeof(T));
        }
    }
#pragma warning restore CS1591
}
