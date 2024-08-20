#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Traits;


#pragma warning disable SA1001 // Commas should be spaced correctly

namespace OpenRA.Test
{
	[TestFixture]
	public class ShapeTest
	{
		IHitShape shape;

		[TestCase(TestName = "CircleShape reports accurate distance")]
		public void Circle()
		{
			shape = new CircleShape(new WDist(1234));
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(100, 100, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(1000, 0, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(2000, 2000, 0)).Length,
				Is.EqualTo(1594));

			Assert.That(new CircleShape(new WDist(73))
				.DistanceFromEdge(new WVec(150, -100, 0)).Length,
				Is.EqualTo(107));

			Assert.That(new CircleShape(new WDist(55))
				.DistanceFromEdge(new WVec(30, -45, 0)).Length,
				Is.EqualTo(0));
		}

		public struct CircleIntersectTestCase
		{
			public WDist CircleRadius;
			public WPos CircleCenter;
			public WPos P1;
			public WPos P2;
			public bool HasIntersection;

			public CircleIntersectTestCase(WDist circleRadius, WPos circleCenter, WPos p1, WPos p2, bool hasIntersection)
			{
				CircleRadius = circleRadius;
				CircleCenter = circleCenter;
				P1 = p1;
				P2 = p2;
				HasIntersection = hasIntersection;
			}
		}

		public struct CircleSliceTestCase
		{
			public WDist CircleRadius;
			public WPos CircleCenter;
			public WPos P;
			public bool IsInsideCircle;

			public CircleSliceTestCase(WDist circleRadius, WPos circleCenter, WPos p, bool isInsideCircle)
			{
				CircleRadius = circleRadius;
				CircleCenter = circleCenter;
				P = p;
				IsInsideCircle = isInsideCircle;
			}
		}

		[TestCase(TestName = "CircleShape Point in Slice check works")]
		public void CircleSliceTests()
		{
			var angleToUseForSlices = 10;
			var circleTestCases = new List<CircleSliceTestCase>()
			{
				  new(new WDist(1234), new WPos(0, 1600, 0), new WPos(-530, 2000, 0), true) // p1
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(-1400, 2000, 0), false) // p2
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(-200, 1200, 0), true) // p3
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(-1100, 300, 0), false) // p4
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(0, 1600, 0), true) // p5
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(800, 1700, 0), true) // p6
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(700, 2400, 0), true) // p7
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(500, 2900, 0), false) // p8
				, new(new WDist(1234), new WPos(0, 1600, 0), new WPos(200, 2300, 0), true) // p9
			};

			int SliceOfPointInsideCircle(CircleSliceTestCase ctc) => CircleShape.CalcCircleSliceIndex(ctc.CircleCenter,
				ctc.CircleRadius.Length, ctc.P, angleToUseForSlices);

			var circleSlicesOfPoints = new List<int>();
			foreach (var (ctc, index) in circleTestCases.Select((item, index) => (item, index)))
			{
				var pointCircleSlice = SliceOfPointInsideCircle(ctc);
				circleSlicesOfPoints.Add(pointCircleSlice);
				// Assert.That(pointCircleSlice >= 0 == ctc.IsInsideCircle); // if slice returned is -1, the point is not inside the circle
				Console.WriteLine($"point {index + 1} is inside circle slice: {pointCircleSlice} ");
			}
		}

		[TestCase(TestName = "CircleShape Line Intersection works")]
		public void CircleShapeIntersection()
		{
			var circleTestCases = new List<CircleIntersectTestCase>()
			{
				  new(new WDist(1234), new WPos(426, 0, 0), new WPos(428, 859, 0), new WPos(2428, 1359, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(228, 1459, 0), new WPos(1500, 650, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(-1300, 0, 0), new WPos(-300, 1800, 0), false)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(-300, 1800, 0), new WPos(428, 859, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(-2000, 3000, 0), new WPos(-700, 800, 0), false)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(-3000, -500, 0), new WPos(3000, -500, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(0, -3000, 0), new WPos(10, 2000, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(-1200, -1200, 0), new WPos(1200, 1800, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(500, -800, 0), new WPos(200, -100, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(0, -1000, 0), new WPos(50, 2000, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(30, -3000, 0), new WPos(0, 2000, 0), true)
				, new(new WDist(1234), new WPos(426, 0, 0), new WPos(0, -3000, 0), new WPos(0, 2000, 0), true)
				, new(new WDist(300), new WPos(14067, 35637, 0), new WPos(10677, 36340, 0), new WPos(13879, 36006, 0), false)
				, new(new WDist(300), new WPos(24514, 33723, 0), new WPos(24517, 33142, 0), new WPos(24739, 33699, 0), true)
			};

			bool LineCollision(CircleIntersectTestCase ctc)
			{
				shape = new CircleShape(ctc.CircleRadius);
				shape.Initialize();
				return shape.LineIntersectsOrIsInside(ctc.CircleCenter, ctc.P1, ctc.P2);
			}

			var lineCollisions = new List<bool>();
			foreach (var (ctc, index) in circleTestCases.Select((item, index) => (item, index)))
			{
				var collision = LineCollision(ctc);
				lineCollisions.Add(collision);
				Assert.That(collision == ctc.HasIntersection); // if != null is true, a point exists
				Console.WriteLine($"line {index + 1} has collision: {collision} ");
			}
		}

		[TestCase(TestName = "Do Two Lines Intersect.")]
		public void DoTwoLinesIntersect()
		{
			var lines = new List<(WPos C1, WPos C2, WPos L1, WPos L2, bool Intersect)>
			{
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(400, 800, 0), new WPos(100, 600, 0), false),
				(new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(400, 800, 0), new WPos(100, 600, 0), false),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(400, 800, 0), new WPos(873, 140, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 300, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(1000, 300, 0), new WPos(1000, 300, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(900, 300, 0), new WPos(650, 300, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(512, 100, 0), new WPos(512, 600, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(512, 600, 0), new WPos(512, 100, 0), true),
				(new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 100, 0), new WPos(512, 600, 0), true),
				(new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 600, 0), new WPos(512, 100, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(512, 100, 0), new WPos(512, 300, 0), true),
				(new WPos(512, 300, 0), new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 100, 0), true),
				(new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 100, 0), new WPos(512, 300, 0), true),
				(new WPos(1000, 300, 0), new WPos(512, 300, 0), new WPos(512, 300, 0), new WPos(512, 100, 0), true),
				(new WPos(180, 300, 0), new WPos(180, 700, 0), new WPos(180, 450, 0), new WPos(180, 550, 0), true),
				(new WPos(180, 300, 0), new WPos(180, 700, 0), new WPos(180, 550, 0), new WPos(180, 450, 0), true),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 550, 0), new WPos(180, 450, 0), true),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 450, 0), new WPos(180, 550, 0), true),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 700, 0), new WPos(180, 700, 0), true),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 701, 0), new WPos(180, 701, 0), false),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 300, 0), new WPos(180, 300, 0), true),
				(new WPos(180, 700, 0), new WPos(180, 300, 0), new WPos(180, 299, 0), new WPos(180, 299, 0), false),
			};

			foreach (var (c1, c2, l1, l2, intersect) in lines)
			{
				Assert.That(WPos.DoTwoLinesIntersect(c1, c2, l1, l2) == intersect);
				Console.WriteLine($"{c1}{c2}{l1}{l2} has intersection: {intersect} ");
			}
		}

		// TO DO: Potentially find a way to Mock the World class so I can call AnyCellEdgeIntersectsWithLine	
		//public void AnyCellEdgeIntersectsWithLine()
		//{
		//	var world = OpenRA.Test.IMock

		//	var lines = new List<(CPos Cell, WPos L1, WPos L2, bool Intersect)>
		//	{
		//		(new CPos(40, 30), new WPos(40960, 30720, 0), new WPos(40960, 30720, 0), true), // top left corner
		//		(new CPos(40, 30), new WPos(40960, 31744, 0), new WPos(40960, 31744, 0), true), // top right corner
		//		(new CPos(40, 30), new WPos(40960, 31744, 0), new WPos(41984, 31744, 0), true), // top
		//		(new CPos(40, 30), new WPos(0, 31744, 0), new WPos(43000, 31744, 0), true), // top 2
		//		(new CPos(40, 30), new WPos(40960, 31744, 0), new WPos(43000, 31744, 0), true), // left
		//		(new CPos(40, 30), new WPos(0, 31744, 0), new WPos(43000, 31744, 0), true), // left 2
		//		(new CPos(40, 30), new WPos(41984, 30720, 0), new WPos(41984, 30720, 0), true), // bottom left corner
		//		(new CPos(40, 30), new WPos(41984, 31744, 0), new WPos(41984, 31744, 0), true), // bottom right corner
		//		(new CPos(40, 30), new WPos(40960, 31744, 0), new WPos(41984, 31744, 0), true), // bottom
		//		(new CPos(40, 30), new WPos(40950, 31744, 0), new WPos(42000, 31744, 0), true), // bottom 2
		//		(new CPos(40, 30), new WPos(40960, 30721, 0), new WPos(40960, 50, 0), true),
		//		(new CPos(40, 30), new WPos(0, 31744, 0), new WPos(50, 31744, 0), false),
		//		(new CPos(40, 30), new WPos(41200, 1000, 0), new WPos(41200, 20000, 0), false),
		//	};

		//	foreach (var (c, l1, l2, intersect) in lines)
		//	{
		//		Assert.That(world.Map.AnyCellEdgeIntersectsWithLine(c, l1, l2) == intersect);
		//		Console.WriteLine($"{c}{l1}{l2} has intersection: {intersect} ");
		//	}
		//}

		[TestCase(TestName = "CapsuleShape report accurate distance")]
		public void Capsule()
		{
			shape = new CapsuleShape(new int2(-50, 0), new int2(500, 235), new WDist(50));
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(300, 100, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-50, 0, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(518, 451, 0)).Length,
				Is.EqualTo(166));

			Assert.That(shape.DistanceFromEdge(new WVec(-50, -50, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-41, 97, 0)).Length,
				Is.EqualTo(35));

			Assert.That(shape.DistanceFromEdge(new WVec(339, 41, 0)).Length,
				Is.EqualTo(64));
		}

		[TestCase(TestName = "RectangleShape report accurate distance")]
		public void Rectangle()
		{
			shape = new RectangleShape(new int2(-123, -456), new int2(100, 100));
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, 50, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 200, 0)).Length,
				Is.EqualTo(100));

			Assert.That(shape.DistanceFromEdge(new WVec(123, 0, 0)).Length,
				Is.EqualTo(24));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, -400, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1000, -400, 0)).Length,
				Is.EqualTo(877));
		}

		[TestCase(TestName = "PolygonShape report accurate distance")]
		public void Polygon()
		{
			// Rectangle like above,
			// Note: The calculations don't match for all, but do have a tolerance of 1.
			shape = new PolygonShape(new int2[] { new(-123, -456), new(100, -456), new(100, 100), new(-123, 100) });
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, 50, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 200, 0)).Length,
				Is.EqualTo(100));

			Assert.That(shape.DistanceFromEdge(new WVec(123, 0, 0)).Length,
				Is.EqualTo(23));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, -400, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1000, -400, 0)).Length,
				Is.EqualTo(877));

			// Rectangle like above but reverse order
			// Note: The calculations don't match for all, but do have a tolerance of 1.
			shape = new PolygonShape(new int2[] { new(-123, 100), new(100, 100), new(100, -456), new(-123, -456) });
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, 50, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 200, 0)).Length,
				Is.EqualTo(100));

			Assert.That(shape.DistanceFromEdge(new WVec(123, 0, 0)).Length,
				Is.EqualTo(23));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, -400, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1000, -400, 0)).Length,
				Is.EqualTo(877));

			// Right triangle taken from above by removing a point
			shape = new PolygonShape(new int2[] { new(-123, -456), new(100, -456), new(100, 100) });
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(50));

			Assert.That(shape.DistanceFromEdge(new WVec(99, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(100, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, 50, 0)).Length,
				Is.EqualTo(167));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 200, 0)).Length,
				Is.EqualTo(141));

			Assert.That(shape.DistanceFromEdge(new WVec(123, 0, 0)).Length,
				Is.EqualTo(23));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, -400, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1000, -400, 0)).Length,
				Is.EqualTo(878));

			// Right triangle taken from above but reverse order
			shape = new PolygonShape(new int2[] { new(100, 100), new(100, -456), new(-123, -456) });
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(49)); // Differs from above by integer rounding.

			Assert.That(shape.DistanceFromEdge(new WVec(99, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(100, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, 50, 0)).Length,
				Is.EqualTo(167));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 200, 0)).Length,
				Is.EqualTo(141));

			Assert.That(shape.DistanceFromEdge(new WVec(123, 0, 0)).Length,
				Is.EqualTo(23));

			Assert.That(shape.DistanceFromEdge(new WVec(-100, -400, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1000, -400, 0)).Length,
				Is.EqualTo(878));

			// Plus shaped dodecagon
			shape = new PolygonShape(new[]
			{
				new int2(-511, -1535), new int2(511, -1535), new int2(511, -511), new int2(1535, -511),
				new int2(1535, 511), new int2(511, 511), new int2(511, 1535), new int2(-511, 1535),
				new int2(-511, 511), new int2(-1535, 511), new int2(-1535, -511), new int2(-511, -511)
			});
			shape.Initialize();

			Assert.That(shape.DistanceFromEdge(new WVec(10, 10, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-511, -1535, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-512, -1536, 0)).Length,
				Is.EqualTo(1));

			Assert.That(shape.DistanceFromEdge(new WVec(0, -1535, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(0, 1535, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1535, 0, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(1535, 0, 0)).Length,
				Is.EqualTo(0));

			Assert.That(shape.DistanceFromEdge(new WVec(-1535, -1535, 0)).Length,
				Is.EqualTo(1024));

			Assert.That(shape.DistanceFromEdge(new WVec(1535, -1535, 0)).Length,
				Is.EqualTo(1024));

			Assert.That(shape.DistanceFromEdge(new WVec(-1535, 1535, 0)).Length,
				Is.EqualTo(1024));

			Assert.That(shape.DistanceFromEdge(new WVec(1535, 1535, 0)).Length,
				Is.EqualTo(1024));

			Assert.That(shape.DistanceFromEdge(new WVec(-500, -1635, 0)).Length,
				Is.EqualTo(100));
		}
	}
}
