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
using System.Net;
using System.Xml.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
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
		[TranslationReference]
		const string MobileOffGridGeometryDescription = "description-mobile-off-grid-geometry";

		World world;
		public readonly List<Command> Comms;
		const int DefaultThickness = 3;
		const int DefaultArrowThickness = 2;
		const int DefaultSharpnessDegrees = 45; // angle in degrees that each side of the arrow head of an arrow should be
		readonly WDist defaultArrowLength = new(128);
		readonly DebugVisualizations debugVis;
		readonly List<string> enabledOverlays = new();
		bool NoFiltering => enabledOverlays.Count == 0;

		public class RenderObject
		{
			public readonly Color color;
			public int persist;
			public readonly int thickness;
			public readonly string key;

			public RenderObject(Color color, int persist, int thickness, string key)
			{
				this.color = color;
				this.persist = persist;
				this.thickness = thickness;
				this.key = key;
			}
		}

		public class Line : RenderObject
		{
			public readonly List<WPos> line;
			public readonly LineEndPoint endPoints;

			public Line(List<WPos> line, Color color, int persist, LineEndPoint endPoints, int thickness, string key)
				: base(color, persist, thickness, key)
			{
				this.line = line;
				this.endPoints = endPoints;
			}
		}

		public class Circle : RenderObject
		{
			public readonly WPos center;
			public readonly WDist radius;

			public Circle(WPos center, WDist radius, Color color, int persist, int thickness, string key)
				: base(color, persist, thickness, key)
			{
				this.center = center;
				this.radius = radius;
			}
		}

		public class Point : RenderObject
		{
			public readonly WPos pos;
			public readonly int radius = 32;

			public Point(WPos pos, int radius, Color color, int persist, int thickness, string key)
				: base(color, persist, thickness, key)
			{
				this.pos = pos;
				this.radius = radius;
			}
		}

		public class Text : RenderObject
		{
			public readonly WPos pos;
			public readonly string text;
			public readonly string fontname;

			public Text(WPos pos, string text, Color color, int persist, string fontname, string key)
				: base(color, persist, 0, key)
			{
				this.pos = pos;
				this.text = text;
				this.fontname = fontname;
			}
		}

		// Note: Key is used for persist condition, so that objects matching the Key (e.g. a specific kind of collision)
		// can be removed independently from other objects (different collision renders for example)
		readonly List<Line> lines = new();
		readonly List<Circle> circles = new();
		readonly List<Point> points = new();
		readonly List<Text> texts = new();
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

		public enum LineEndPoint { None, Circle, EndArrow, StartArrow, BothArrows }

		public struct OverlayKeyStrings
		{
			public const string LocalAvoidance = "local";  // toggles local avoidance overlays.
			public const string SeekVectors = "seek"; // toggles flee vector overlays.
			public const string FleeVectors = "flee"; // toggles seek vector overlays.
			public const string AllVectors = "allvec"; // toggles combined seek + flee vector overlays.
			public const string Collision = "coll"; // toggles visual of collisions
			public const string PathingText = "pathingtext"; // toggles seek, flee, and combined vector overlays.
			public const string PFNumber = "pfnum"; // toggles the index of the Theta PF used
		}

		public MobileOffGridOverlay(Actor self)
		{
			Comms = new List<Command>() { new("mogg", MobileOffGridGeometryDescription, true) };

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

		readonly List<string> validChatCommandArgs =
			new()
			{
				OverlayKeyStrings.LocalAvoidance,
				OverlayKeyStrings.SeekVectors,
				OverlayKeyStrings.FleeVectors,
				OverlayKeyStrings.AllVectors,
				OverlayKeyStrings.Collision,
				OverlayKeyStrings.PFNumber,
			};

		readonly List<string> validVectorArgs =
			new()
			{
				OverlayKeyStrings.SeekVectors,
				OverlayKeyStrings.FleeVectors,
				OverlayKeyStrings.AllVectors,
			};

		void IChatCommand.InvokeCommand(string command, string arg)
		{
			// If the overlay does not exist and cannot be removed, then we add it
			if (validChatCommandArgs.Any(validArg => arg == validArg))
			{
				if (command == OverlayKeyStrings.AllVectors)
				{
					if (enabledOverlays.Any(o => validVectorArgs.Any(a => a == o)))
					{
						foreach (var key in validVectorArgs)
							enabledOverlays.Remove(key);
					}
					else
					{
						foreach (var key in validVectorArgs)
						{
							enabledOverlays.Remove(key);
							enabledOverlays.Add(key);
						}
					}
				}
				else if (!enabledOverlays.Remove(arg))
					enabledOverlays.Add(arg);
			}
		}

		void INotifyCreated.Created(Actor self)
		{
			world = self.World;
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

		static List<LineAnnotationRenderableWithZIndex> RenderArrowhead(WPos start, WVec directionAndLength, Color color,
			int thickness = DefaultArrowThickness, int sharpnessDegrees = DefaultSharpnessDegrees)
		{
			var linesToRender = new List<LineAnnotationRenderableWithZIndex>();
			var end = start - directionAndLength;
			var endOfLeftSide = start + directionAndLength.Rotate(WRot.FromYaw(WAngle.FromDegrees(sharpnessDegrees + 180)));
			var endOfRightSide = start + directionAndLength.Rotate(WRot.FromYaw(WAngle.FromDegrees(-sharpnessDegrees + 180)));

			linesToRender.Add(new(start, end, thickness, color));
			linesToRender.Add(new(start, endOfLeftSide, thickness, color));
			linesToRender.Add(new(start, endOfRightSide, thickness, color));

			return linesToRender;
		}

		IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr)
		{
			const int EndPointRadius = 100;
			const int EndPointThickness = 3;
			const string DefaultFontName = "TinyBold";
			var lineColor = Color.Red;

			CircleAnnotationRenderable PointRenderFunc(WPos p, int radius, Color color, int thickness = DefaultThickness) =>
				new(p, new WDist(radius), thickness, color, true);

			static CircleAnnotationRenderable CircleRenderFunc(WPos center, WDist radius, Color color, int thickness = DefaultThickness) =>
				new(center, radius, thickness, color, false);

			TextAnnotationRenderable TextRenderFunc(WPos p, string text, Color color, string fontname)
			{
				fontname ??= DefaultFontName;
				var font = Game.Renderer.Fonts[fontname];
				return new(font, p, 0, color, text);
			}

			List<LineAnnotationRenderableWithZIndex> LineRenderFunc(WPos start, WPos end, Color color,
				LineEndPoint endPoints = LineEndPoint.None, int thickness = DefaultThickness)
			{
				var linesToRender = new List<LineAnnotationRenderableWithZIndex>();

				if (endPoints == LineEndPoint.None)
					linesToRender.Add(new(start, end, thickness, color, color, (0, 0, color)));
				else if (endPoints == LineEndPoint.Circle)
					linesToRender.Add(new(start, end, thickness, color, color, (EndPointRadius, EndPointThickness, color)));
				else if (endPoints == LineEndPoint.StartArrow || endPoints == LineEndPoint.EndArrow || endPoints == LineEndPoint.BothArrows)
				{
					var arrowVec = new WVec(defaultArrowLength, WRot.FromYaw((end - start).Yaw));
					linesToRender.Add(new(start, end, thickness, color, color, (0, 0, color)));
					if (endPoints == LineEndPoint.StartArrow || endPoints == LineEndPoint.BothArrows)
						linesToRender = linesToRender.Union(RenderArrowhead(start, -arrowVec, color)).ToList();
					if (endPoints == LineEndPoint.EndArrow || endPoints == LineEndPoint.BothArrows)
						linesToRender = linesToRender.Union(RenderArrowhead(end, arrowVec, color)).ToList();
				}

				return linesToRender;
			}

			// Render Texts
			foreach (var obj in texts.Where(o => NoFiltering || enabledOverlays.Contains(o.key)))
				yield return TextRenderFunc(obj.pos, obj.text, obj.color, obj.fontname);

			// Render Points
			foreach (var obj in points.Where(o => NoFiltering || enabledOverlays.Contains(o.key)))
				yield return PointRenderFunc(obj.pos, obj.radius, obj.color, obj.thickness);

			// Render Circles
			foreach (var obj in circles.Where(o => NoFiltering || enabledOverlays.Contains(o.key)))
				yield return CircleRenderFunc(obj.center, obj.radius, obj.color, obj.thickness);

			// Render Lines
			foreach (var obj in lines.Where(o => NoFiltering || enabledOverlays.Contains(o.key)))
				foreach (var renderLine in LineRenderFunc(obj.line[0], obj.line[1], obj.color, obj.endPoints, obj.thickness))
					yield return renderLine;
		}

		public void AddPoint(WPos pos, Color color, int persist = (int)PersistConst.Always, int radius = 32, int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				points.RemoveAll(p => p.key == key);
			else
				for (var i = 0; i < points.Count; i++)
					if (points[i].key == key && points[i].persist != (int)PersistConst.Always)
						points[i].persist--;

			points.RemoveAll(o => o.persist <= 0 && o.persist != (int)PersistConst.Always);
			points.Add(new(pos, radius, color, persist, thickness, key));
		}

		public void RemovePoint(WPos pos) { points.RemoveAll(ap => ap.pos == pos); }
		public void AddText(WPos pos, string text, Color color, int persist = (int)PersistConst.Always, string fontname = null, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				texts.RemoveAll(l => l.key == key);
			else
				for (var i = 0; i < texts.Count; i++)
					if (texts[i].key == key && texts[i].persist != (int)PersistConst.Always)
						texts[i].persist--;

			texts.RemoveAll(o => o.persist <= 0 && o.persist != (int)PersistConst.Always);
			texts.Add(new(pos, text, color, persist, fontname, key));
		}

		public void RemoveText(WPos pos) { texts.RemoveAll(t => t.pos == pos); }
		public void RemoveText(string text) { texts.RemoveAll(t => t.text == text); }
		public void RemoveText(WPos pos, string text) { texts.RemoveAll(t => t.pos == pos && t.text == text); }
		public void AddLine(List<WPos> line, int persist = (int)PersistConst.Always, LineEndPoint endPoints = LineEndPoint.Circle,
			int thickness = DefaultThickness, string key = null)
		{
			AddLine(line, defaultColor, persist, endPoints, thickness, key);
		}

		public void AddLine(List<WPos> line, Color color, int persist = (int)PersistConst.Always, LineEndPoint endPoints = LineEndPoint.Circle,
			int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				lines.RemoveAll(l => l.key == key);
			else
				for (var i = 0; i < lines.Count; i++)
					if (lines[i].key == key && lines[i].persist != (int)PersistConst.Always)
						lines[i].persist--;

			lines.RemoveAll(o => o.persist <= 0 && o.persist != (int)PersistConst.Always);
			lines.Add(new(line, color, persist, endPoints, thickness, key));
		}

		public void AddLine(WPos start, WPos end, int persist = (int)PersistConst.Always, LineEndPoint endPoints = LineEndPoint.Circle,
			int thickness = DefaultThickness, string key = null)
		{
			AddLine(start, end, defaultColor, persist, endPoints, thickness, key);
		}

		public void AddLine(WPos start, WPos end, Color color, int persist = (int)PersistConst.Always, LineEndPoint endPoints = LineEndPoint.Circle,
			int thickness = DefaultThickness, string key = null)
		{
			AddLine(new List<WPos>() { start, end }, color, persist, endPoints, thickness, key);
		}

		public void RemoveLine(List<WPos> line) { lines.RemoveAll(l => l.line == line); }
		public void AddCircle(WPos pos, WDist dist, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null) =>
			AddCircle(pos, dist, defaultColor, persist, thickness, key);
		public void AddCircle(WPos pos, WDist dist, Color color, int persist = (int)PersistConst.Always, int thickness = DefaultThickness, string key = null)
		{
			if (persist == (int)PersistConst.Never || persist == 0)
				circles.RemoveAll(c => c.key == key);
			else
				for (var i = 0; i < lines.Count; i++)
					if (circles[i].key == key && circles[i].persist != (int)PersistConst.Always)
						circles[i].persist--;

			circles.RemoveAll(o => o.persist <= 0 && o.persist != (int)PersistConst.Always);
			circles.Add(new(pos, dist, color, persist, DefaultThickness, key));
		}

		public void RemoveCircle(WPos center, WDist radius) { circles.RemoveAll(c => c.center == center && c.radius == radius); }
		public void ClearLines() { lines.Clear(); }
		public void ClearPoints() { points.Clear(); }
		public void ClearCircles() { circles.Clear(); }
		public void ClearTexts() { texts.Clear(); }

		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}
}
