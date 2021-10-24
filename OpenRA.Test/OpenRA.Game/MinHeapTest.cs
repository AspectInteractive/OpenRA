using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	// This code is contributed by Princi Singh (for printing the heap)
	[TestFixture]
	public class MinHeapTest
	{
		[TestCase(TestName = "Testing heap from basic list of numbers from 1 to 10")]
		public static void BasicExample()
		{
			// var inputs = new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
			var inputs = GenRandomIntList(1, 11, 10, true);
			var sortedInputs = inputs;
			sortedInputs.Sort();
			System.Console.WriteLine($"Inputs: {string.Join(", ", inputs)}");
			var heap = new OpenRA.Primitives.MinHeap<int>();
			heap.AddList(inputs);
			var poppedItems = new List<int>();
			while (!heap.Empty)
			{
				var poppedItem = heap.Pop();
				Console.WriteLine($"Popped item: {poppedItem}\n");
				poppedItems.Add(poppedItem);
			}

			System.Console.WriteLine($"sortedInputs: {string.Join(", ", sortedInputs)}" +
									 $"\npoppedItems: {string.Join(", ", poppedItems)}");
			Assert.IsTrue(poppedItems.SequenceEqual(sortedInputs));
		}

		[TestCase(TestName = "A more complex heap test")]
		public static void ComplexExample()
		{
			// var inputs = new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
			var inputs = new List<int>() { 24, 6, 8, 19, 13, 5, 0, 1, 6, 8 };
			System.Console.WriteLine($"Inputs: {string.Join(", ", inputs)}");
			var heap = new OpenRA.Primitives.MinHeap<int>();
			heap.AddList(inputs);
			heap.Add(3);
			heap.Add(15);
			heap.Add(7);
			heap.Add(22);
			heap.Add(4);
			heap.Pop();
			heap.Add(8);
			heap.Pop();
			var heapList = new List<int>();
			while (!heap.Empty)
				heapList.Add(heap.Pop());
			var expectedList = new List<int>() { 3, 4, 5, 6, 6, 7, 8, 8, 8, 13, 15, 19, 22, 24 };
			System.Console.WriteLine($"expectedList: {string.Join(", ", expectedList)}" +
									 $"\nheapList: {string.Join(", ", heapList)}");
			Assert.IsTrue(expectedList.SequenceEqual(heapList));
		}

		// Generate a random list of integers within [minInt, maxInt)
		public static List<int> GenRandomIntList(int minInt, int maxIntExc, int maxSize, bool uniqueOnly)
		{
			if (uniqueOnly && Math.Abs(maxIntExc - minInt) < maxSize)
				throw new InvalidOperationException($"A unique list of size {maxSize} needs more integers than those within [{minInt}, {maxIntExc})");

			var rnd = new Random();
			var randomList = new List<int>();
			while (randomList.Count < maxSize)
			{
				var newInt = rnd.Next(minInt, maxIntExc);
				if (!uniqueOnly || !randomList.Contains(newInt))
					randomList.Add(newInt);
			}

			return randomList;
		}
	}
}
