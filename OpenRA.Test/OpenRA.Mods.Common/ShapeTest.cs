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
using OpenRA.Mods.Common.HitShapes;

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

		[TestCase(TestName = "CircleShape Line Intersection works")]
		public void CircleShapeIntersection()
		{
			shape = new CircleShape(new WDist(1234));
			shape.Initialize();
			var shapeCenter = new WPos(426, 0, 0);

			var lines = new List<(List<WPos>, bool)>()
			{
				  (new List<WPos>() { new WPos(428, 859, 0), new WPos(2428, 1359, 0) }, true)
				, (new List<WPos>() { new WPos(228, 1459, 0), new WPos(1500, 650, 0) }, true)
				, (new List<WPos>() { new WPos(-1300, 0, 0), new WPos(-300, 1800, 0) }, false)
				, (new List<WPos>() { new WPos(-300, 1800, 0), new WPos(428, 859, 0) }, true)
				, (new List<WPos>() { new WPos(-2000, 3000, 0), new WPos(-700, 800, 0) }, false)
				, (new List<WPos>() { new WPos(-3000, -500, 0), new WPos(3000, -500, 0) }, true)
				, (new List<WPos>() { new WPos(0, -3000, 0), new WPos(0, 2000, 0) }, true)
				, (new List<WPos>() { new WPos(-1200, -1200, 0), new WPos(1200, 1800, 0) }, true)
				, (new List<WPos>() { new WPos(500, -800, 0), new WPos(200, -100, 0) }, false)
			};

			Func<List<WPos>, WPos?> shapeIntersectsLine = l => shape.FirstIntersectingPosFromLine(shapeCenter, l.ElementAt(0), l.ElementAt(1));
			Func<List<WPos>, List<WPos>> intersectingLinePoints = l => shape.IntersectingPosesFromLine(shapeCenter, l.ElementAt(0), l.ElementAt(1));

			var lineIntersectingPoint = new List<WPos?>();
			var lineIntersectingPoints = new List<List<WPos>>();
			foreach (var (lineBoolPair, index) in lines.Select((item, index) => (item, index)))
			{
				var currIntersectingPoint = shapeIntersectsLine(lineBoolPair.Item1);
				var currIntersectingPoints = intersectingLinePoints(lineBoolPair.Item1);
				lineIntersectingPoint.Add(currIntersectingPoint);
				lineIntersectingPoints.Add(currIntersectingPoints);

				Assert.That((currIntersectingPoint != null) == lineBoolPair.Item2); // if != null is true, a point exists
				System.Console.WriteLine($"line {index + 1} intersects at: {currIntersectingPoint} with pts {string.Join("; ", currIntersectingPoints)}");
			}
		}

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
			shape = new PolygonShape(new int2[] { new int2(-123, -456), new int2(100, -456), new int2(100, 100), new int2(-123, 100) });
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
			shape = new PolygonShape(new int2[] { new int2(-123, 100), new int2(100, 100), new int2(100, -456), new int2(-123, -456) });
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
			shape = new PolygonShape(new int2[] { new int2(-123, -456), new int2(100, -456), new int2(100, 100) });
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
			shape = new PolygonShape(new int2[] { new int2(100, 100), new int2(100, -456), new int2(-123, -456) });
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
