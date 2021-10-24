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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.HitShapes
{
	public class RectangleShape : IHitShape
	{
		public WDist OuterRadius { get; private set; }

		[FieldLoader.Require]
		public readonly int2 TopLeft;

		[FieldLoader.Require]
		public readonly int2 BottomRight;

		[Desc("Defines the top offset relative to the actor's center.")]
		public readonly int VerticalTopOffset = 0;

		[Desc("Defines the bottom offset relative to the actor's center.")]
		public readonly int VerticalBottomOffset = 0;

		[Desc("Rotates shape by an angle relative to actor facing. Mostly required for buildings on isometric terrain.",
			"Mobile actors do NOT need this!")]
		public readonly WAngle LocalYaw = WAngle.Zero;

		int2 quadrantSize;
		int2 center;

		WVec[] combatOverlayVertsTop;
		WVec[] combatOverlayVertsBottom;
		WVec[] combatOverlayVertsSide1;
		WVec[] combatOverlayVertsSide2;

		public RectangleShape() { }

		public RectangleShape(int2 tl, int2 br)
		{
			TopLeft = tl;
			BottomRight = br;
		}

		public void Initialize()
		{
			if (TopLeft.X >= BottomRight.X || TopLeft.Y >= BottomRight.Y)
				throw new YamlException("TopLeft and BottomRight points are invalid.");

			if (VerticalTopOffset < VerticalBottomOffset)
				throw new YamlException("VerticalTopOffset must be equal to or higher than VerticalBottomOffset.");

			quadrantSize = (BottomRight - TopLeft) / 2;
			center = TopLeft + quadrantSize;

			var topRight = new int2(BottomRight.X, TopLeft.Y);
			var bottomLeft = new int2(TopLeft.X, BottomRight.Y);
			var corners = new[] { TopLeft, BottomRight, topRight, bottomLeft };
			OuterRadius = new WDist(corners.Select(x => x.Length).Max());

			combatOverlayVertsTop = new WVec[]
			{
				new WVec(TopLeft.X, TopLeft.Y, VerticalTopOffset),
				new WVec(BottomRight.X, TopLeft.Y, VerticalTopOffset),
				new WVec(BottomRight.X, BottomRight.Y, VerticalTopOffset),
				new WVec(TopLeft.X, BottomRight.Y, VerticalTopOffset),
			};

			combatOverlayVertsBottom = new WVec[]
			{
				new WVec(TopLeft.X, TopLeft.Y, VerticalBottomOffset),
				new WVec(BottomRight.X, TopLeft.Y, VerticalBottomOffset),
				new WVec(BottomRight.X, BottomRight.Y, VerticalBottomOffset),
				new WVec(TopLeft.X, BottomRight.Y, VerticalBottomOffset),
			};

			combatOverlayVertsSide1 = new WVec[]
			{
				new WVec(TopLeft.X, TopLeft.Y, VerticalBottomOffset),
				new WVec(TopLeft.X, TopLeft.Y, VerticalTopOffset),
				new WVec(TopLeft.X, BottomRight.Y, VerticalTopOffset),
				new WVec(TopLeft.X, BottomRight.Y, VerticalBottomOffset),
			};

			combatOverlayVertsSide2 = new WVec[]
			{
				new WVec(BottomRight.X, TopLeft.Y, VerticalBottomOffset),
				new WVec(BottomRight.X, TopLeft.Y, VerticalTopOffset),
				new WVec(BottomRight.X, BottomRight.Y, VerticalTopOffset),
				new WVec(BottomRight.X, BottomRight.Y, VerticalBottomOffset),
			};
		}

		public WDist DistanceFromEdge(in WVec v)
		{
			var r = new WVec(
				Math.Max(Math.Abs(v.X - center.X) - quadrantSize.X, 0),
				Math.Max(Math.Abs(v.Y - center.Y) - quadrantSize.Y, 0), 0);

			return new WDist(r.HorizontalLength);
		}

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

		// Must only be used with non-rotated rectangles
		bool IntersectsWithHitShape(int2 selfCenter, int2 rectCenter, RectangleShape rectHitShape)
		{
			if (LocalYaw != WAngle.Zero)
				throw new ArgumentException($"Rectangle's local yaw is a non-zero value of {LocalYaw}, which is invalid for IntersectsWithRectangleHitShape()");

			var rect1 = Rectangle.FromTLBR(selfCenter + TopLeft, selfCenter + BottomRight);
			var rect2 = Rectangle.FromTLBR(rectCenter + rectHitShape.TopLeft, rectCenter + rectHitShape.BottomRight);
			return rect1.IntersectsWithRectangle(rect2);
		}

		// Must only be used with non-rotated rectangles
		bool IntersectsWithHitShape(int2 selfCenter, int2 circleCenter, CircleShape circleHitShape)
		{
			if (LocalYaw != WAngle.Zero)
				throw new ArgumentException($"Rectangle's local yaw is a non-zero value of {LocalYaw}, which is invalid for IntersectsWithCircleHitShape()");

			var rect = Rectangle.FromTLBR(selfCenter + TopLeft, selfCenter + BottomRight);
			var circleRadius = circleHitShape.Radius.Length;

			/*System.Console.WriteLine($"rectTL: {selfCenter + TopLeft}, rectBR: {selfCenter + BottomRight}, circleCenter: {circleCenter}, circleRadius: {circleRadius}");*/

			return rect.IntersectsWithCircle(circleCenter, circleRadius);
		}

		bool IntersectsWithHitShape(int2 selfCenter, int2 polygonCenter, PolygonShape polygonHitShape) { return false; } // to be implemented
		bool IntersectsWithHitShape(int2 selfCenter, int2 capsuleCenter, CapsuleShape capsuleHitShape) { return false; } // to be implemented

		WPos[] IHitShape.GetCorners(int2 selfCenter)
		{
			var corners = new WPos[4];
			var topRight = new int2(BottomRight.X, TopLeft.Y);
			var bottomLeft = new int2(TopLeft.X, BottomRight.Y);
			corners[0] = new WPos(selfCenter.X + TopLeft.X, selfCenter.Y + TopLeft.Y, 0);
			corners[1] = new WPos(selfCenter.X + topRight.X, selfCenter.Y + topRight.Y, 0);
			corners[2] = new WPos(selfCenter.X + bottomLeft.X, selfCenter.Y + bottomLeft.Y, 0);
			corners[3] = new WPos(selfCenter.X + BottomRight.X, selfCenter.Y + BottomRight.Y, 0);
			return corners;
		}

		public WDist DistanceFromEdge(WPos pos, WPos origin, WRot orientation)
		{
			orientation += WRot.FromYaw(LocalYaw);

			if (pos.Z > origin.Z + VerticalTopOffset)
			{
				return DistanceFromEdge((pos - (origin + new WVec(0, 0, VerticalTopOffset))).Rotate(-orientation));
			}

			if (pos.Z < origin.Z + VerticalBottomOffset)
				return DistanceFromEdge((pos - (origin + new WVec(0, 0, VerticalBottomOffset))).Rotate(-orientation));

			return DistanceFromEdge((pos - new WPos(origin.X, origin.Y, pos.Z)).Rotate(-orientation));
		}

		IEnumerable<IRenderable> IHitShape.RenderDebugOverlay(HitShape hs, WorldRenderer wr, WPos origin, WRot orientation)
		{
			orientation += WRot.FromYaw(LocalYaw);

			var vertsTop = combatOverlayVertsTop.Select(v => origin + v.Rotate(orientation)).ToArray();
			var vertsBottom = combatOverlayVertsBottom.Select(v => origin + v.Rotate(orientation)).ToArray();
			var side1 = combatOverlayVertsSide1.Select(v => origin + v.Rotate(orientation)).ToArray();
			var side2 = combatOverlayVertsSide2.Select(v => origin + v.Rotate(orientation)).ToArray();

			var shapeColor = hs.IsTraitDisabled ? Color.LightGray : Color.Yellow;

			yield return new PolygonAnnotationRenderable(vertsTop, origin, 1, shapeColor);
			yield return new PolygonAnnotationRenderable(vertsBottom, origin, 1, shapeColor);
			yield return new PolygonAnnotationRenderable(side1, origin, 1, shapeColor);
			yield return new PolygonAnnotationRenderable(side2, origin, 1, shapeColor);
			yield return new CircleAnnotationRenderable(origin, OuterRadius, 1, hs.IsTraitDisabled ? Color.Gray : Color.LimeGreen);
		}
	}
}
