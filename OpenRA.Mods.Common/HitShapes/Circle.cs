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

		WPos[] IHitShape.GetCorners(int2 selfCenter)
		{
			var corners = new WPos[9];
			var wvec = new WVec(Radius.Length, 0, 0);
			corners[0] = new WPos(selfCenter.X, selfCenter.Y, 0); // center
			corners[1] = new WPos(selfCenter.X - Radius.Length, selfCenter.Y, 0); // left-center
			corners[2] = new WPos(selfCenter.X + Radius.Length, selfCenter.Y, 0); // right-center
			corners[3] = new WPos(selfCenter.X, selfCenter.Y - Radius.Length, 0); // top-center
			corners[4] = new WPos(selfCenter.X, selfCenter.Y + Radius.Length, 0); // bottom-center
			corners[5] = new WPos(selfCenter.X, selfCenter.Y, 0) + wvec.Rotate(WRot.FromYaw(WAngle.FromDegrees(45))); // bottom-right
			corners[6] = new WPos(selfCenter.X, selfCenter.Y, 0) + wvec.Rotate(WRot.FromYaw(WAngle.FromDegrees(135))); // bot-left
			corners[7] = new WPos(selfCenter.X, selfCenter.Y, 0) + wvec.Rotate(WRot.FromYaw(WAngle.FromDegrees(225))); // top-left
			corners[8] = new WPos(selfCenter.X, selfCenter.Y, 0) + wvec.Rotate(WRot.FromYaw(WAngle.FromDegrees(315))); // top-right
			/*System.Console.WriteLine($"corners[5] is now {corners[5]}");
			System.Console.WriteLine($"corners[6] is now {corners[6]}");
			System.Console.WriteLine($"corners[7] is now {corners[7]}");
			System.Console.WriteLine($"corners[8] is now {corners[8]}");*/
			return corners;
		}
		public static float Sq(float i) { return i * i; }
		public static float Sqrt(float i) { return (float)Math.Sqrt(i); }
		public static int Sq(int i) { return Exts.ISqr(i); }
		public static int Sqrt(int i) { return Exts.ISqrt(i); }

		public bool PosIsInsideCircle(WPos circleCenter, WPos checkPos) { return PosIsInsideCircle(circleCenter, Radius.Length, checkPos); }
		private static bool PosIsInsideCircle(WPos circleCenter, int radius, WPos checkPos)
		{
			var xDelta = circleCenter.X - checkPos.X;
			var yDelta = circleCenter.Y - checkPos.Y;
			return Sqrt(Sq(xDelta) + Sq(yDelta)) < radius;
		}

		public static bool PointIsWithinLineSegment(WPos checkPoint, WPos lineP1, WPos lineP2)
		{
			// if we knew lineP1.X < lineP2.X, then we could use lineP1.X <= checkPoint.X && checkPoint.X <= lineP2.X
			var deltaVec = lineP2 - lineP1;
			var slope = Math.Abs(deltaVec.Y / deltaVec.X);
			Func<WPos, int> getC; // get a coordinate
			if (slope <= 1)
				getC = p => p.X;
			else
				getC = p => p.Y;
			return getC(checkPoint) < getC(lineP1) ^ getC(checkPoint) <= getC(lineP2); // if this does not work use checkPoint.X or checkPoint.Y etc.
		}

		/*public bool LineIntersectsCircle(WPos circleCenter, WPos p1, WPos p2) { return LineIntersectsCircle(circleCenter, Radius.Length, p1, p2); }
		public static bool LineIntersectsCircle(WPos circleCenter, int radius, WPos p1, WPos p2)
		{
			var circleLeft = circleCenter.X - radius;
			var circleRight = circleCenter.X + radius;
			var circleTop = circleCenter.Y - radius;
			var circleBottom = circleCenter.Y + radius;
			return ((p1.X < circleLeft ^ p2.X < circleLeft) ||
				    (p1.X > circleRight ^ p2.X > circleRight)) &&
				   ((p1.Y < circleTop ^ p2.Y < circleTop) ||
				    (p1.Y > circleBottom ^ p2.Y > circleBottom));
		}*/

		WPos? IHitShape.FirstIntersectingPosFromLine(WPos circleCenter, WPos p1, WPos p2)
		{
			var intersectingPosesFromLine = IntersectingPosesFromLine(circleCenter, Radius.Length, p1, p2);
			if (intersectingPosesFromLine.Count > 0)
				return intersectingPosesFromLine.ElementAt(0);
			return null;
		}

		List<WPos> IHitShape.IntersectingPosesFromLine(WPos shapeCenterPos, WPos p1, WPos p2)
		{ return IntersectingPosesFromLine(shapeCenterPos, Radius.Length, p1, p2); }

		#pragma warning disable SA1312 // Variable names should begin with lower-case letter
		static List<WPos> IntersectingPosesFromLine(WPos circleCenter, int radius, WPos p1, WPos p2)
		{
			var poses = new List<WPos>();
			var p1InCircle = PosIsInsideCircle(circleCenter, radius, p1);
			var p2InCircle = PosIsInsideCircle(circleCenter, radius, p2);
			if (!(p1InCircle && p2InCircle))
			{
				if (p1.X == p2.X) // since we cannot have a slope for a vertical line, we add 1, or 0.1% the width of a cell to the ray cast
					p2 = new WPos(p2.X + 1, p2.Y, p2.Z);
				if (p1.Y == p2.Y)
					p2 = new WPos(p2.X, p2.Y + 1, p2.Z);
				var a1 = (float)(p2.Y - p1.Y) / (p2.X - p1.X); // could be an issue for floating point
				var b1 = (p2.X * p1.Y - p1.X * p2.Y) / (p2.X - p1.X);
				var a2 = (-1) / a1;
				var b2 = circleCenter.Y + circleCenter.X / a1;
				var Px = (float)(b2 - b1) / (a1 - a2); // could be an issue for floating point
				var Py = (float)(a1 * b2 - b1 * a2) / (a1 - a2);
				var LenCP = (new WPos((int)Px, (int)Py, 0) - circleCenter).Length;
				if (LenCP <= radius) // true if there is an intersection between the circle and the infinite line
				{
					var LenIPsq = Math.Abs(Sq(radius) - Sq(LenCP));
					// var A = Sq(a1) + 1;
					// var B = 2 * (a1 * (b1 - (int)Py) - 1);
					// var C = Sq((int)Px) + Sq(b1 - (int)Py) - LenIPsq;
					var A = Sq(a1) + 1;
					var B = (-2) * Px + (2 * a1 * b1) - (2 * a1 * Py);
					var C = Sq(Px) - 2 * b1 * Py + Sq(b1) + Sq(Py) - LenIPsq;
					var discr = Sq(B) - 4 * A * C; // discriminant
					if (discr > 0) // No roots found if this is less than 0
					{
						var sqrtDiscr = Sqrt(discr);
						var root1 = (-B + sqrtDiscr) / (2 * A);
						var root2 = (-B - sqrtDiscr) / (2 * A);
						float Ix, Ix2, Iy, Iy2;
						if (Math.Abs(p1.X - root1) < Math.Abs(p1.X - root2)) // root 1 is closer
						{
							Ix = root1;
							Ix2 = root2;
						}
						else // root 2 is closer
						{
							Ix = root2;
							Ix2 = root1;
						}
						Iy = a1 * Ix + b1;
						// TO DO: Add code to validate 'S -> E' is long enough to intersect
						var I = new WPos((int)Ix, (int)Iy, 0);
						//!((p2 - p1).Length < (I - p1).Length)
						if (PointIsWithinLineSegment(I, p1, p2)) // Check that line segment is long enough to intersect
						{
							poses.Add(I);
							// If root2 is not the same as root1, a second root exists, so we include it as item 2.
							// A second root also requires that no end points are inside the circle
							if (Ix2 != Ix && !p1InCircle && !p2InCircle)
							{
								Iy2 = a1 * Ix2 + b1;
								poses.Add(new WPos((int)Ix2, (int)Iy2, 0));
							}
						}
					}
				}
			}
			return poses;
		}
		#pragma warning restore SA1312 // Variable names should begin with lower-case letter

		/*
		public static WPos? FirstIntersectingPosFromLine(WPos circlePos, int circleRad, WPos lineStart, WPos lineEnd)
		{
			if (TrivialLineIsIntersecting(circlePos, circleRad, lineStart, lineEnd))
			{
				// B = d
				// M = m
				// Calculate terms of the linear and quadratic equations
				var m = Div(lineEnd.Y - lineStart.Y, lineEnd.X - lineStart.X);
				var d = lineStart.Y - m * lineStart.X;
				var a = 1 + m * m;
				var b = 2 * (m * d - m * circlePos.Y - circlePos.X);
				var c = circlePos.X * circlePos.X + d * d + circlePos.Y * circlePos.Y - circleRad * circleRad - 2 * d * circlePos.Y;
				// solve quadratic equation
				var sqRtTerm = (int)Math.Sqrt(b * b - 4 * a * c);
				var x = Div((-b) + sqRtTerm, 2 * a);
				// make sure we have the correct root for our line segment
				if ((x < Math.Min(lineStart.X, lineEnd.X)) || (x > Math.Max(lineStart.X, lineEnd.X)))
					x = Div((-b) - sqRtTerm, 2 * a);
				// solve for the y-component
				var y = m * x + d;
				return new WPos(x, y, 0);
			}

			// Line segment does not intersect at one point.  It is either fully outside, fully inside, intersects at two points, is
			// tangential to, or one or more points is exactly on the circle radius.
			return null;
		}*/

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
