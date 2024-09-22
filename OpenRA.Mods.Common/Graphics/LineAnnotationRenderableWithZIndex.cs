#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class LineAnnotationRenderableWithZIndex : IRenderable, IFinalizedRenderable
	{
		readonly World world;
		readonly WPos start;
		readonly WPos end;
		readonly float width;
		readonly Color startColor;
		readonly Color endColor;
		readonly (int, int, Color) endPointCircleProps;
		List<CircleAnnotationRenderable> endPointCircles = new List<CircleAnnotationRenderable>();

		public LineAnnotationRenderableWithZIndex(World world, WPos start, WPos end, float width, Color startColor, Color endColor,
										(int, int, Color) endPointCircleProps)
		{
			this.world = world;
			this.start = start;
			this.end = end;
			this.width = width;
			this.startColor = startColor;
			this.endColor = endColor;
			this.endPointCircleProps = endPointCircleProps;
			world.ScreenMap.Add(this, start, end, (int)width);
			CircleAnnotationRenderable MakeCircleAnno(WPos pos)
			{
				return new CircleAnnotationRenderable(world, pos,
					new WDist(endPointCircleProps.Item1),
					endPointCircleProps.Item2,
					endPointCircleProps.Item3, true);
			}

			if (endPointCircleProps.Item1 != -1)
			{
				endPointCircles.Add(MakeCircleAnno(start));
				endPointCircles.Add(MakeCircleAnno(end));
			}
		}

		public LineAnnotationRenderableWithZIndex(World world, WPos start, WPos end, float width, Color color, (int, int, Color) endPointCircleProps)
			: this(world, start, end, width, color, color, endPointCircleProps) { }

		public LineAnnotationRenderableWithZIndex(World world, WPos start, WPos end, float width, Color color)
			: this(world, start, end, width, color, (-1, -1, Color.Black)) { }

		public LineAnnotationRenderableWithZIndex(World world, WPos start, WPos end, float width, Color startColor, Color endColor)
			: this(world, start, end, width, startColor, endColor, (-1, -1, Color.Black)) { }

		public WPos Pos => start;
		public int ZOffset => 0;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{ return new LineAnnotationRenderableWithZIndex(world, start, end, width, startColor, endColor, endPointCircleProps); }
		public IRenderable OffsetBy(in WVec vec)
		{ return new LineAnnotationRenderableWithZIndex(world, start + vec, end + vec, width, startColor, endColor, endPointCircleProps); }

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			Game.Renderer.RgbaColorRenderer.DrawLine(
				wr.Viewport.WorldToViewPx(wr.ScreenPosition(start)),
				wr.Viewport.WorldToViewPx(wr.Screen3DPosition(end)),
				width, startColor, endColor);
			foreach (var circle in endPointCircles)
				circle.Render(wr);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
