using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{

	[TestFixture]
	public class LinkedListTest
	{
		public LinkedListNode<CPos>[][] AllCellNodes;

		public void InitializeAllCellNodes(int2 mapSize)
		{
			AllCellNodes = new LinkedListNode<CPos>[mapSize.X][];
			for (var x = 0; x < mapSize.X; x++)
				AllCellNodes[x] = new LinkedListNode<CPos>[mapSize.Y];
		}

		static bool IsValidParent(LinkedListNode<CPos> a, LinkedListNode<CPos> b)
			=> Math.Abs(b.Value.X - a.Value.X) + Math.Abs(b.Value.Y - a.Value.Y) == 1 &&
			   !(a.Parent == b && b.Parent == a);

		[TestCase(TestName = "Reverse a Linked List")]
		public void ReverseLinkedList()
		{
			InitializeAllCellNodes(new int2(128, 128));

			var currId = 3;
			var heads = new System.Collections.Generic.List<LinkedListNode<CPos>>();
			var head = new LinkedListNode<CPos>(new CPos(1, 1), IsValidParent, currId, false);
			var endLinkedListNode
				= new LinkedListNode<CPos>(
					new LinkedListNode<CPos>(
						new LinkedListNode<CPos>(
							new LinkedListNode<CPos>(
								new LinkedListNode<CPos>(
									new LinkedListNode<CPos>(
										new LinkedListNode<CPos>(head, head, new CPos(4, 5), IsValidParent),
										head, new CPos(4, 4), IsValidParent),
									head, new CPos(4, 3), IsValidParent),
								head, new CPos(4, 2), IsValidParent),
							head, new CPos(3, 2), IsValidParent),
						head, new CPos(2, 2), IsValidParent),
					head, new CPos(1, 2), IsValidParent);
			head.Parent = endLinkedListNode;

			//var reversedLinkedListNodes = BasicCellDomainManager.ReverseLinkedListNodes(head, ref AllCellNodes, ref heads);

			var node = head;
			Console.WriteLine("Original Order:");
			while (node.Parent != null)
			{
				Console.WriteLine($"node {node.Value} with parent {node.Parent.Value}.");
				node = node.Parent;
			}

			//Console.WriteLine("Reverse Order:");
			//foreach (var node2 in reversedLinkedListNodes)
			//	Console.WriteLine($"node {node2.Value} with parent {node2.Parent.Value}.");
		}
	}
}
