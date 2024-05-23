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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

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
