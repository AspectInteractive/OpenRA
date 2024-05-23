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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class LineAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos end;
		readonly float width;
		readonly Color startColor;
		readonly Color endColor;
		readonly int layer;
		readonly (int, int, Color) endPointCircleProps;
		List<CircleAnnotationRenderable> endPointCircles = new List<CircleAnnotationRenderable>();

		public LineAnnotationRenderable(WPos start, WPos end, float width, Color startColor, Color endColor,
										(int, int, Color) endPointCircleProps, int layer = 0)
		{
			Pos = start;
			this.end = end;
			this.width = width;
			this.startColor = startColor;
			this.endColor = endColor;
			this.layer = layer;
			this.endPointCircleProps = endPointCircleProps;
			Func<WPos, CircleAnnotationRenderable> makeCircleAnno = (WPos pos) =>
																				{
																					return new CircleAnnotationRenderable(pos,
																						new WDist(endPointCircleProps.Item1),
																						endPointCircleProps.Item2,
																						endPointCircleProps.Item3, true);
																				};
			if (endPointCircleProps.Item1 != -1)
			{
				endPointCircles.Add(makeCircleAnno(start));
				endPointCircles.Add(makeCircleAnno(end));
			}
		}

		public LineAnnotationRenderable(WPos start, WPos end, float width, Color color, (int, int, Color) endPointCircleProps, int layer)
			: this(start, end, width, color, color, endPointCircleProps, layer) { }
		public LineAnnotationRenderable(WPos start, WPos end, float width, Color color, (int, int, Color) endPointCircleProps)
			: this(start, end, width, color, color, endPointCircleProps, 0) { }

		public LineAnnotationRenderable(WPos start, WPos end, float width, Color color, int layer)
			: this(start, end, width, color, (-1, -1, Color.Black), layer) { }
		public LineAnnotationRenderable(WPos start, WPos end, float width, Color color)
			: this(start, end, width, color, (-1, -1, Color.Black), 0) { }

		public LineAnnotationRenderable(WPos start, WPos end, float width, Color startColor, Color endColor, int layer)
			: this(start, end, width, startColor, endColor, (-1, -1, Color.Black), layer) { }
		public LineAnnotationRenderable(WPos start, WPos end, float width, Color startColor, Color endColor)
			: this(start, end, width, startColor, endColor, (-1, -1, Color.Black), 0) { }

		public WPos Pos => start;
		public int ZOffset => 0;
		public int Layer => layer;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset)
		{ return new LineAnnotationRenderable(start, end, width, startColor, endColor, endPointCircleProps, layer); }
		public IRenderable OffsetBy(in WVec vec)
		{ return new LineAnnotationRenderable(start + vec, end + vec, width, startColor, endColor, endPointCircleProps, layer); }

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			Game.Renderer.RgbaColorRenderer.DrawLine(
				wr.Viewport.WorldToViewPx(wr.ScreenPosition(Pos)),
				wr.Viewport.WorldToViewPx(wr.Screen3DPosition(end)),
				width, startColor, endColor);
			foreach (var circle in endPointCircles)
				circle.Render(wr);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
