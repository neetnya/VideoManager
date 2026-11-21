using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VideoManager.Common;

/// <summary>支持批量替换而不触发逐条 CollectionChanged 通知的 ObservableCollection。</summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public int RemoveAll(Predicate<T> match)
    {
        CheckReentrancy();
        var removed = 0;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (match(Items[i]))
            {
                Items.RemoveAt(i);
                removed++;
            }
        }

        if (removed > 0)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        return removed;
    }
}
