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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

#pragma warning disable SA1005 // Single line comments should begin with single space
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1108 // Block statements should not contain embedded comments
#pragma warning disable SA1513 // Closing brace should be followed by blank line

namespace OpenRA.Mods.Common.HitShapes
{
	public class CircleShape : IHitShape
	{
		public WDist OuterRadius => Radius;

		[FieldLoader.Require]
		public readonly WDist Radius = new(426);

		[Desc("Defines the top offset relative to the actor's center.")]
		public readonly int VerticalTopOffset = 0;

		[Desc("Defines the bottom offset relative to the actor's center.")]
		public readonly int VerticalBottomOffset = 0;

		public CircleShape() { }

		public CircleShape(WDist radius) { Radius = radius; }

		public void Initialize()
		{
			if (VerticalTopOffset < VerticalBottomOffset)
				throw new YamlException("VerticalTopOffset must be equal to or higher than VerticalBottomOffset.");
		}

		public WDist DistanceFromEdge(in WVec v)
		{
			return new WDist(Math.Max(0, v.Length - Radius.Length));
		}

		public WDist DistanceFromEdge(WPos pos, WPos origin, WRot orientation)
		{
			if (pos.Z > origin.Z + VerticalTopOffset)
				return DistanceFromEdge(pos - (origin + new WVec(0, 0, VerticalTopOffset)));

			if (pos.Z < origin.Z + VerticalBottomOffset)
				return DistanceFromEdge(pos - (origin + new WVec(0, 0, VerticalBottomOffset)));

			return DistanceFromEdge(pos - new WPos(origin.X, origin.Y, pos.Z));
		}

		static List<WPos> GetPointsSurroundingCircleUnit(int2 selfCenter, WDist unitRadius) =>
			GetPointsSurroundingCircleUnit(selfCenter, unitRadius, WAngle.Zero);

		static List<WPos> GetPointsSurroundingCircleUnit(int2 selfCenter, WDist unitRadius, WAngle initialRotation, int angleIncAmount = 128)
		{
			var radiusVec = new WVec(unitRadius, WRot.FromYaw(initialRotation));
			var points = new List<WPos>(); // Points surrounding circle
			for (var a = 0; a < 1024; a += angleIncAmount) // 128 = 45 degrees,
				points.Add(new WPos(selfCenter.X, selfCenter.Y, 0) + radiusVec.Rotate(new WRot(WAngle.Zero, WAngle.Zero, new WAngle(a))));
			return points;
		}

		WPos[] IHitShape.GetCorners(int2 selfCenter) => GetPointsSurroundingCircleUnit(selfCenter, Radius).ToArray();

		public static Fix64 Sq(Fix64 i) { return i * i; }
		public static Fix64 Sqrt(Fix64 i) { return Fix64.Sqrt(i); }
		public static double Sq(double i) { return i * i; }
		public static double Sqrt(double i) { return (double)Math.Sqrt(i); }
		public static int Sq(int i) { return Exts.ISqr(i); }
		public static int Sqrt(int i) { return Exts.ISqrt(i); }

		public bool PosIsInsideCircle(WPos circleCenter, WPos checkPos) { return PosIsInsideCircle(circleCenter, Radius.Length, checkPos); }
		public static bool PosIsInsideCircle(WPos circleCenter, int radius, WPos checkPos)
		{
			var xDelta = circleCenter.X - checkPos.X;
			var yDelta = circleCenter.Y - checkPos.Y;
			var delta = (long)Sq(xDelta) + Sq(yDelta);
			return Sqrt(delta) < radius;
		}

		public static int GetSliceCount(int angleToCutCircleSlices)
		{ return angleToCutCircleSlices != 0 ? (int)((Fix64)360 / (Fix64)angleToCutCircleSlices) : -1; }

		public Fix64 GetSliceAngle(int sliceIndex, int angleToCutCircleSlices)
		{
			var nSlices = GetSliceCount(angleToCutCircleSlices);
			var oneSliceAngle = (Fix64)2.0 * Fix64.Pi / (Fix64)nSlices;
			return oneSliceAngle * (Fix64)sliceIndex;
		}

		public int CalcCircleSliceIndex(WPos circleCenter, WPos checkPos, int angleToCutCircleSlices)
		{ return CalcCircleSliceIndex(circleCenter, Radius.Length, checkPos, angleToCutCircleSlices); }
		public static int CalcCircleSliceIndex(WPos circleCenter, int radius, WPos checkPos, int angleToCutCircleSlices)
		{
			var nSlices = GetSliceCount(angleToCutCircleSlices);
			if (nSlices > 0 && PosIsInsideCircle(circleCenter, radius, checkPos))
			{
				var angle = Fix64.Atan2((Fix64)(circleCenter.Y - checkPos.Y), (Fix64)(circleCenter.X - checkPos.X));
				if (angle < Fix64.Zero)
					angle = Fix64.Pi - angle;
				var sliceAngle = (Fix64)2.0 * Fix64.Pi / (Fix64)nSlices;
				return (int)(angle / sliceAngle);
			}
			else
				return -1;
		}

		public static bool PointIsWithinLineSegment(WPos checkPoint, WPos lineP1, WPos lineP2)
		{
			// if we knew lineP1.X < lineP2.X, then we could use lineP1.X <= checkPoint.X && checkPoint.X <= lineP2.X
			var deltaVec = lineP2 - lineP1;
			var slope = Fix64.Abs(new Fix64(deltaVec.Y) / new Fix64(deltaVec.X));
			Func<WPos, int> getC; // get a coordinate
			if (slope <= new Fix64(1))
				getC = p => p.X;
			else
				getC = p => p.Y;
			return getC(checkPoint) < getC(lineP1) ^ getC(checkPoint) <= getC(lineP2); // if this does not work use checkPoint.X or checkPoint.Y etc.
		}

		bool IHitShape.LineIntersectsOrIsInside(WPos circleCenter, WPos p1, WPos p2)
		{
			if (PosIsInsideCircle(circleCenter, Radius.Length, p1) || PosIsInsideCircle(circleCenter, Radius.Length, p2))
				return true;

			return LineIsCollidingLogic(circleCenter, p1, p2);
		}

		bool LineIsCollidingLogic(WPos circleCenter, WPos p1, WPos p2)
		{
			var minimumDistance = (Fix64)2 * (Fix64)TriangleArea(circleCenter, p1, p2) / (Fix64)(p1 - p2).Length;

			if (minimumDistance <= (Fix64)Radius.Length)
				return true;
			else
				return false;
		}

		static int TriangleArea(WPos a, WPos b, WPos c)
		{
			var ab = b - a;
			var ac = c - a;
			var crossProduct = ab.X * ac.Y - ab.Y * ac.X;
			return (int)((Fix64)Math.Abs(crossProduct) / (Fix64)2);
		}

		bool IHitShape.LineIsColliding(WPos circleCenter, WPos p1, WPos p2) => LineIsCollidingLogic(circleCenter, p1, p2);

		bool IHitShape.IntersectsWithHitShape(int2 selfCenter, int2 secondCenter, HitShape hitShape)
		{
			if (hitShape.Info.Type is RectangleShape rect)
				return IntersectsWithHitShape(selfCenter, secondCenter, rect);
			else if (hitShape.Info.Type is CircleShape circ)
				return IntersectsWithHitShape(selfCenter, secondCenter, circ);
			else if (hitShape.Info.Type is PolygonShape poly)
				return IntersectsWithHitShape(selfCenter, secondCenter, poly);
			else if (hitShape.Info.Type is CapsuleShape caps)
				return IntersectsWithHitShape(selfCenter, secondCenter, caps);
			else
				return false;
		}

		bool IntersectsWithHitShape(int2 selfCenter, int2 rectCenter, RectangleShape rectHitShape) { return false; } // to be implemented
		bool IntersectsWithHitShape(int2 selfCenter, int2 circCenter, CircleShape circHitShape)
		{
			var circ1Radius = Radius.Length;
			var circ2Radius = circHitShape.Radius.Length;

			return Math.Abs((selfCenter.X - circCenter.X) * (selfCenter.X - circCenter.X) +
							(selfCenter.Y - circCenter.Y) * (selfCenter.Y - circCenter.Y)) <
					(circ1Radius + circ2Radius) * (circ1Radius + circ2Radius);
		}

		bool IntersectsWithHitShape(int2 selfCenter, int2 circleCenter, PolygonShape polygonHitShape) { return false; } // to be implemented
		bool IntersectsWithHitShape(int2 selfCenter, int2 circleCenter, CapsuleShape capsuleHitShape) { return false; } // to be implemented

		IEnumerable<IRenderable> IHitShape.RenderDebugOverlay(HitShape hs, WorldRenderer wr, WPos origin, WRot orientation)
		{
			var shapeColor = hs.IsTraitDisabled ? Color.LightGray : Color.Yellow;

			var corners = hs.Info.Type.GetCorners(origin.XYToInt2());
			foreach (var corner in corners)
			{
				var cornerWPos = new WPos(corner.X, corner.Y, origin.Z);
				yield return new LineAnnotationRenderable(cornerWPos - new WVec(64, 0, 0), cornerWPos + new WVec(64, 0, 0), 3, shapeColor);
			}

			yield return new CircleAnnotationRenderable(origin + new WVec(0, 0, VerticalTopOffset), Radius, 1, shapeColor);
			yield return new CircleAnnotationRenderable(origin + new WVec(0, 0, VerticalBottomOffset), Radius, 1, shapeColor);
		}
	}
}
