using System.Collections.Generic;

namespace ARKServerCreationTool.Services.Common
{
    /// <summary>Order-preserving list moves for reorderable UIs. Out-of-range moves are no-ops.</summary>
    public static class ListReorder
    {
        public static void MoveUp<T>(IList<T> list, int index)
        {
            if (index <= 0 || index >= list.Count) return;
            (list[index - 1], list[index]) = (list[index], list[index - 1]);
        }

        public static void MoveDown<T>(IList<T> list, int index)
        {
            if (index < 0 || index >= list.Count - 1) return;
            (list[index + 1], list[index]) = (list[index], list[index + 1]);
        }

        public static void Move<T>(IList<T> list, int from, int to)
        {
            if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return;
            T item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
        }
    }
}
