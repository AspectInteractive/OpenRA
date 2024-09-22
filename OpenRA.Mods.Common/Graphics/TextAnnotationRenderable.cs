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

using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;
using System.Security.Cryptography;

namespace OpenRA.Mods.Common.Graphics
{
	public class TextAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		World world;
		readonly SpriteFont font;
		readonly Color color;
		readonly Color bgDark;
		readonly Color bgLight;
		readonly string text;

		public TextAnnotationRenderable(World world, SpriteFont font, WPos pos, int zOffset, Color color, Color bgDark, Color bgLight, string text)
		{
			this.world = world;
			this.font = font;
			Pos = pos;
			ZOffset = zOffset;
			this.color = color;
			this.bgDark = bgDark;
			this.bgLight = bgLight;
			this.text = text;
		}

		public void AddOrUpdateScreenMap()
		{
			var size = font.Measure(text);
			world.ScreenMap.AddOrUpdate(this, Pos, new Size(size.X, size.Y));
		}

		public void RemoveFromScreenMap() => world.ScreenMap.Remove(this);

		public TextAnnotationRenderable(World world, SpriteFont font, WPos pos, int zOffset, Color color, string text)
			: this(world, font, pos, zOffset, color,
				ChromeMetrics.Get<Color>("TextContrastColorDark"),
				ChromeMetrics.Get<Color>("TextContrastColorLight"),
				text)
		{ }

		public WPos Pos { get; }
		public int ZOffset { get; }
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset) { return new TextAnnotationRenderable(world, font, Pos, ZOffset, color, text); }
		public IRenderable OffsetBy(in WVec vec) { return new TextAnnotationRenderable(world, font, Pos + vec, ZOffset, color, text); }
		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			var size = font.Measure(text).ToFloat2();
			var viewPos = wr.Viewport.WorldToViewPx(wr.ScreenPosition(Pos));
			var textViewTL = viewPos - 0.5f * size;

			font.DrawTextWithContrast(text, textViewTL, color, bgDark, bgLight, 1);
		}

		public void RenderDebugGeometry(WorldRenderer wr)
		{
			var size = font.Measure(text).ToFloat2();
			var screenPos = wr.Viewport.WorldToViewPx(wr.ScreenPosition(Pos));
			Game.Renderer.RgbaColorRenderer.DrawRect(screenPos - 0.5f * size, screenPos + 0.5f * size, 1, Color.Red);
		}

		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
		public void Dispose() => RemoveFromScreenMap();
	}
}
