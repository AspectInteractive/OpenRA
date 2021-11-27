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
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class WPosTest
	{
		[TestCase(TestName = "Testing line intersection against a rectangle for two lines b0->b1 and c0->c1")]
		public void LineIntersectsWithRectangleLines()
		{
			var tl = new WPos(15000, 21000, 0);
			var tr = new WPos(28000, 21000, 0);
			var trtlvec = tr - tl;
			var bl = new WPos(15000, 35000, 0);
			var bltlvec = bl - tl;
			var br = new WPos(28000, 35000, 0);
			var brtrvec = br - tr;
			var brblvec = br - bl;

			var b0 = new WPos(11000, 29000, 0);
			var b1 = new WPos(32000, 28000, 0);
			var bvec = b1 - b0;

			var c0 = new WPos(11000, 29000, 0);
			var c1 = new WPos(29000, 12000, 0);
			var cvec = c1 - c0;

			Assert.IsFalse(WPos.DoTwoLinesIntersect(tl, tr, b0, b1));
			Assert.IsTrue(WPos.DoTwoLinesIntersect(tr, br, b0, b1));
			Assert.IsFalse(WPos.DoTwoLinesIntersect(br, bl, b0, b1));
			Assert.IsTrue(WPos.DoTwoLinesIntersect(bl, tl, b0, b1));

			Assert.IsTrue(WPos.DoTwoLinesIntersect(tl, tr, c0, c1));
			Assert.IsFalse(WPos.DoTwoLinesIntersect(tr, br, c0, c1));
			Assert.IsFalse(WPos.DoTwoLinesIntersect(br, bl, c0, c1));
			Assert.IsTrue(WPos.DoTwoLinesIntersect(bl, tl, c0, c1));

			System.Console.WriteLine($"DoTwoLinesIntersect for tl,tr, b0,b1: {WPos.DoTwoLinesIntersect(tl, tr, b0, b1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for tr,br, b0,b1: {WPos.DoTwoLinesIntersect(tr, br, b0, b1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for br,bl, b0,b1: {WPos.DoTwoLinesIntersect(br, bl, b0, b1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for bl,tl, b0,b1: {WPos.DoTwoLinesIntersect(bl, tl, b0, b1)}");

			System.Console.WriteLine($"DoTwoLinesIntersect for tl,tr, c0,c1: {WPos.DoTwoLinesIntersect(tl, tr, c0, c1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for tr,br, c0,c1: {WPos.DoTwoLinesIntersect(tr, br, c0, c1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for br,bl, c0,c1: {WPos.DoTwoLinesIntersect(br, bl, c0, c1)}");
			System.Console.WriteLine($"DoTwoLinesIntersect for bl,tl, c0,c1: {WPos.DoTwoLinesIntersect(bl, tl, c0, c1)}");
		}

		[TestCase(TestName = "Testing line intersection pos between lines p1->p2 and p3->p4")]
		public void GetIntersectionOfTwoLines()
		{
			var linePairs = new List<(List<WPos>, List<WPos>, bool)>()
			{
				  (new List<WPos>() { new WPos(428, 859, 0), new WPos(2428, 1359, 0) },
				   new List<WPos>() { new WPos(228, 1459, 0), new WPos(1500, 650, 0) }, true)
				, (new List<WPos>() { new WPos(-1300, 0, 0), new WPos(-300, 1800, 0) },
				   new List<WPos>() { new WPos(-1000, 400, 0), new WPos(428, 559, 0) }, false)
				, (new List<WPos>() { new WPos(450, 800, 0), new WPos(700, 800, 0) },
				   new List<WPos>() { new WPos(715, 800, 0), new WPos(750, 800, 0) }, false)
				, (new List<WPos>() { new WPos(450, 800, 0), new WPos(700, 800, 0) },
				   new List<WPos>() { new WPos(250, 800, 0), new WPos(500, 800, 0) }, true)
			};

			Func<List<WPos>, List<WPos>, WPos?> intersectingPoint = (l1, l2) =>
			{ return WPos.FindIntersection(l1.ElementAt(0), l1.ElementAt(1), l2.ElementAt(0), l2.ElementAt(1)); };

			var lineIntersectingPoint = new List<WPos?>();
			foreach (var (lines, index) in linePairs.Select((item, index) => (item, index)))
			{
				var currIntersectingPoint = intersectingPoint(lines.Item1, lines.Item2);
				//Assert.That((currIntersectingPoint != null) == lines.Item3); // if != null is true, a point exists
				System.Console.WriteLine($"lineSet {index + 1} intersects at: {currIntersectingPoint}");
			}
		}
	}
}
