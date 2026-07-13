using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Uviewer.Models
{
    /// <summary>
    /// Observable collection that appends a prepared range with one UI notification.
    /// Existing realized items stay attached, so a virtualized reader does not blink
    /// or lose its scroll anchor when more lines become available.
    /// </summary>
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public RangeObservableCollection(IEnumerable<T> items)
            : base(items)
        {
        }

        public void AddRange(IEnumerable<T> items)
        {
            var addedItems = items as List<T> ?? items.ToList();
            if (addedItems.Count == 0) return;

            int startIndex = Count;
            foreach (T item in addedItems)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                addedItems,
                startIndex));
        }
    }
}
