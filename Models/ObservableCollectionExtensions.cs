using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Topiary.Models
{
    public static class ObservableCollectionExtensions
    {
        public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
        {
            var enumerable = items.ToList();
            foreach (var item in enumerable)
            {
                collection.Add(item);
            }
        }
    }
} 