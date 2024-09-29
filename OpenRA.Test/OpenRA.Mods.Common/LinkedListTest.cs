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
	}
}
