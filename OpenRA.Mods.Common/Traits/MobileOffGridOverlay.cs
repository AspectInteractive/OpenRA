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
using System.Xml.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Displays an overlay for Mobile Off Grid")]
	public class MobileOffGridOverlayInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new MobileOffGridOverlay(init.Self); }
	}

	public class MobileOffGridOverlay : IRenderAnnotations, INotifyCreated, IChatCommand
	{
		public readonly List<Command> Comms;
		const int DefaultThickness = 3;
		readonly DebugVisualizations debugVis;
		readonly List<string> enabledOverlays = new();
		bool NoFiltering => enabledOverlays.Count == 0;

		// Note: Key is used for persist condition, so that objects matching the Key (e.g. a specific kind of collision)
		// can be removed independently from other objects (different collision renders for example)
		readonly List<(List<WPos> Line, Color C, int Persist, int Thickness, string Key)> lines = new();
		readonly List<((WPos, WDist) Circle, Color C, int Persist, int Thickness, string Key)> circles = new();
		readonly List<(WPos Point, Color C, int Persist, int Thickness, string Key)> points = new();
		readonly List<(WPos Pos, string Text, Color C, int Persist, string FontName, string Key)> texts = new();
		readonly Color defaultColor = Color.White;

		// PersistCount determines how many times the object can be rendered before old versions are removed.
		// 'Never' means all old versions matching the same Key are removed.
		// 'Always' means no old versions are removed. This should not be used for renders that occur on every tick due to performance issues.
		// A number N from 1..inf means that N number of versions will stay on the screen before older versions are removed.
		public enum PersistConst
		{
			Always = -99,
			Never = 0
		}

		public struct OverlayKeyStrings
		{
			public const string LocalAvoidance = "local";
			public const string Vectors = "vectors";
		}

		public MobileOffGridOverlay(Actor self)
		{
			Comms = new List<Command>()
			{
				new(OverlayKeyStrings.LocalAvoidance, "toggles local avoidance overlays.", true),
				new(OverlayKeyStrings.Vectors, "toggles seek/flee vector overlays.", true),
			};

			var console = self.World.WorldActor.TraitOrDefault<ChatCommands>();
			var help = self.World.WorldActor.TraitOrDefault<HelpCommand>();

			foreach (var comm in Comms)
			{
				console.RegisterCommand(comm.Name, this);
				if (comm.InHelp)
					help.RegisterHelp(comm.Name, comm.Desc);
			}

			debugVis = self.World.WorldActor.TraitOrDefault<DebugVisualizations>();
		}

		public void InvokeCommand(string command, string arg)
		{
			// If the overlay does not exist and cannot be removed, then we add it
			if (Comms.Any(comm => comm.Name == command) && !enabledOverlays.Remove(command))
				enabledOverlays.Add(command);
		}

		void INotifyCreated.Created(Actor self)
		{
			var mobileOffGrid = self.TraitsImplementing<MobileOffGrid>().FirstOrDefault();
			if (mobileOffGrid != null)
				mobileOffGrid.Overlay = this;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (debugVis == null || !debugVis.MobileOffGridGeometry || self.World.FogObscures(self))
				return Enumerable.Empty<IRenderable>();

			return RenderAnnotations(self, wr);
		}

		IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr)
		{
			const int PointRadius = 100;
			const int EndPointRadius = 100;
			const int EndPointThickness = 3;
			const string DefaultFontName = "TinyBold";
			var lineColor = Color.Red;

			CircleAnnotationRenderable PointRenderFunc(WPos p, Color color, int thickness = DefaultThickness) =>
				new(p, new WDist(PointRadius), thickness, color, true);

			static CircleAnnotationRenderable CircleRenderFunc((WPos, WDist) c, Color color, int thickness = DefaultThickness) =>
				new(c.Item1, c.Item2, thickness, color, false);

			TextAnnotationRenderable TextRenderFunc(WPos p, string text, Color color, string fontname)
			{
				fontname ??= DefaultFontName;
				var font = Game.Renderer.Fonts[fontname];
				return new(font, p, 0, color, text);
			}

			LineAnnotationRenderableWithZIndex LineRenderFunc(WPos start, WPos end, Color color, int thickness = DefaultThickness) =>
				new(start, end, thickness, color, color, (EndPointRadius, EndPointThickness, color));

			// Render Texts
			foreach (var (pos, text, color, persist, fontname, key) in texts.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return TextRenderFunc(pos, text, color, fontname);

			// Render Points
			foreach (var (point, color, persist, thickness, key) in points.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return PointRenderFunc(point, color, thickness);

			// Render Circles
			foreach (var (circle, color, persist, thickness, key) in circles.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return CircleRenderFunc(circle, color, thickness);

			// Render Lines
			foreach (var (line, color, persist, thickness, key) in lines.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return LineRenderFunc(line[0], line[1], color, thickness);
		}

		public void AddPoint(WPos pos, Color color, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				points.RemoveAll(p => p.Key == key);
			else
				for (var i = 0; i < points.Count; i++)
					if (points[i].Key == key)
						points[i] = (pos, color, persist--, thickness, key);
			points.Add((pos, color, persist, thickness, key));
		}
		public void RemovePoint(WPos pos) { points.RemoveAll(ap => ap.Point == pos); }
		public void AddText(WPos pos, string text, Color color, int persist = (int)PersistConst.Always, string fontname = null, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				texts.RemoveAll(l => l.Key == key);
			else
				for (var i = 0; i < texts.Count; i++)
					if (texts[i].Key == key)
						texts[i] = (pos, text, color, persist--, fontname, key);
			texts.Add((pos, text, color, persist, fontname, key));
		}

		public void RemoveText(WPos pos) { texts.RemoveAll(t => t.Pos == pos); }
		public void RemoveText(string text) { texts.RemoveAll(t => t.Text == text); }
		public void RemoveText(WPos pos, string text) { texts.RemoveAll(t => t.Pos == pos && t.Text == text); }
		public void AddLine(List<WPos> line, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null) =>
			AddLine(line, defaultColor, persist, thickness, key);
		public void AddLine(List<WPos> line, Color color, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				lines.RemoveAll(l => l.Key == key);
			else
				for (var i = 0; i < lines.Count; i++)
					if (lines[i].Key == key)
						lines[i] = (line, color, persist--, thickness, key);
			lines.Add((line, color, persist, thickness, key));
		}

		public void AddLine(WPos start, WPos end, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null) =>
			AddLine(start, end, defaultColor, persist, thickness, key);
		public void AddLine(WPos start, WPos end, Color color, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null) =>
			AddLine(new List<WPos>() { start, end }, color, persist, thickness, key);
		public void RemoveLine(List<WPos> line) { lines.RemoveAll(l => l.Line == line); }
		public void AddCircle((WPos P, WDist D) circle, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null) =>
			AddCircle(circle, defaultColor, persist, thickness, key);
		public void AddCircle((WPos P, WDist D) circle, Color color, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				circles.RemoveAll(c => c.Key == key);
			else
				for (var i = 0; i < lines.Count; i++)
					if (circles[i].Key == key)
						circles[i] = (circle, color, persist--, thickness, key);
			circles.Add((circle, color, persist, DefaultThickness, key));
		}

		public void RemoveCircle((WPos P, WDist D) circle) { circles.RemoveAll(c => c.Circle == circle); }
		public void ClearLines() { lines.Clear(); }
		public void ClearPoints() { points.Clear(); }
		public void ClearCircles() { circles.Clear(); }
		public void ClearTexts() { texts.Clear(); }

		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
