#region Copyright & License Information
/*
 * Copyright 2007-2021 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

#pragma warning disable SA1108 // Block statements should not contain embedded comments

// Inspired by Egor Grishechko : https://egorikas.com/max-and-min-heap-implementation-with-csharp/
namespace OpenRA.Primitives
{
	public class MinHeap<T> : IPriorityQueue<T>
	{
		readonly List<T> items;
		readonly IComparer<T> comparer;

		public MinHeap()
			: this(Comparer<T>.Default) { }
		public MinHeap(IComparer<T> comparer)
		{
			items = new List<T>();
			this.comparer = comparer;
		}

		public bool Empty => items.Count == 0;

		public T Peek()
		{
			if (Empty)
				throw new InvalidOperationException("PriorityQueue empty.");
			return items[0];
		}

		public T Pop()
		{
			if (Empty)
				throw new InvalidOperationException("PriorityQueue empty.");
			var topItem = items[0];
			items[0] = items[items.Count - 1];
			items.RemoveAt(items.Count - 1);
			if (items.Count > 1)
				PercolateDown(0);
			return topItem;
		}

		public void Add(T item)
		{
			items.Add(item);
			PercolateUp(items.Count - 1);
		}

		public void AddList(List<T> items)
		{
			foreach (var item in items)
				Add(item);
		}

		public void Swap(int index1, int index2)
		{
			#if DEBUG
			/*System.Console.WriteLine($"Swapping index {index1}: {items[index1]} " +
									 $" with index {index2}: {items[index2]} ");*/
			#endif
			var temp = items[index1];
			items[index1] = items[index2];
			items[index2] = temp;
		}

		public void PercolateUp(int index)
		{
			Func<int, int> getParent = i => (i - 1) >> 1;
			var item = items[index];
			var parent = getParent(index);
			while (index > 0 && comparer.Compare(item, items[parent]) < 0)
			{
				Swap(index, parent);
				index = parent;
				parent = getParent(index);
			}
		}

		public void PercolateDown(int index)
		{
			var item = items[index];
			Func<int, int> getChild = i => (i << 1) + 1;
			while (getChild(index) < items.Count)
			{
				var child1 = getChild(index);
				var child2 = child1 + 1;
				var smallerIndex = child1; // default case

				// Check whether child2 exists, and replace smallerIndex if it is smaller than child1
				if (child2 < items.Count && comparer.Compare(items[child2], items[child1]) < 0)
					smallerIndex = child2;

				if (comparer.Compare(items[smallerIndex], items[index]) >= 0)
					break;
				else // Item is smaller than smallest child
				{
					Swap(smallerIndex, index);
					index = smallerIndex;
				}
			}
		}
	}
}
