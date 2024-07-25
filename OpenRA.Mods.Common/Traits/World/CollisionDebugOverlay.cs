﻿#region Copyright & License Information
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
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Pathfinder.ThetaStarPathSearch;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay of additional collision debug traits")]
	public class CollisionDebugOverlayInfo : TraitInfo<CollisionDebugOverlay> { }

	public class CollisionDebugOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		public readonly List<Command> Comms;
		readonly List<(List<WPos>, Color)> linesWithColors = new();
		readonly List<((WPos, WDist), Color, int)> circlesWithColors = new();
		readonly List<(WPos, Color, int)> pointsWithColors = new();
		List<(CCState, Color)> statesWithColors = new();
		readonly List<List<WPos>> paths = new();
		readonly List<List<WPos>> lines = new();

		// Set this to true to display annotations showing the cost at each cell
		readonly bool showCosts = true;

		public bool Enabled;
		const int DefaultThickness = 3;
		float currHue = Color.Blue.ToAhsv().H; // 0.0 - 1.0
		readonly float pointHue = Color.Red.ToAhsv().H; // 0.0 - 1.0
		readonly float circleHue = Color.LightGreen.ToAhsv().H; // 0.0 - 1.0
		readonly float pathHue = Color.Yellow.ToAhsv().H; // 0.0 - 1.0
		readonly float lineHue = Color.LightBlue.ToAhsv().H; // 0.0 - 1.0
		float currSat = 1.0F; // 0.0 - 1.0
		float currLight = 0.7F; // 0.0 - 1.0 with 1.0 being brightest
		float lineColorIncrement = 0.05F;
		public Action<string> ToggleVisibility;

		public CollisionDebugOverlay()
		{
			Comms = new List<Command>()
			{
				new("colldebug", "toggles the collision debug overlay.", true)
			};
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			var help = w.WorldActor.TraitOrDefault<HelpCommand>();

			if (console == null || help == null)
				return;

			foreach (var comm in Comms)
			{
				console.RegisterCommand(comm.Name, this);
				if (comm.InHelp)
					help.RegisterHelp(comm.Name, comm.Desc);
			}

			ToggleVisibility = arg => DevCommands.Visibility(arg, w);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (Comms.Any(comm => comm.Name == name))
			{
				Enabled ^= true;
				ToggleVisibility("");
			}
		}

		public static void GenericLinkedPointsFunc<T1>(List<T1> pointList, int pointListLen, Action<T1, T1> funcOnLinkedPoints)
		{
			for (var i = 0; i < pointListLen - 1; i++)
			{
				var currItem = pointList[i];
				var nextItem = pointList[i + 1];
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static void GenericLinkedPointsFunc<T1, T2>(List<T1> pointList, int pointListLen, Func<T1, T2> pointUnpacker,
														Action<T2, T2> funcOnLinkedPoints)
		{
			for (var i = 0; i < pointListLen - 1; i++)
			{
				var currItem = pointUnpacker(pointList[i]);
				var nextItem = pointUnpacker(pointList[i + 1]);
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static List<LineAnnotationRenderableWithZIndex> GetPathRenderableSet(List<WPos> path, int lineThickness, Color lineColor, int endPointRadius,
																	int endPointThickness, Color endPointColor)
		{
			var linesToRender = new List<LineAnnotationRenderableWithZIndex>();
			void FuncOnLinkedPoints(WPos wpos1, WPos wpos2) => linesToRender.Add(new LineAnnotationRenderableWithZIndex(wpos1, wpos2,
																			lineThickness, lineColor, lineColor,
																			(endPointRadius, endPointThickness, endPointColor)));
			GenericLinkedPointsFunc(path, path.Count, FuncOnLinkedPoints);
			return linesToRender;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			var pointRadius = 100;
			var lineThickness = 3;
			var endPointRadius = 100;
			var endPointThickness = 3;
			var fontName = "TinyBold";
			var font = Game.Renderer.Fonts[fontName];
			Color lineColor;

			CircleAnnotationRenderable PointRenderFunc(WPos p, Color color, int thickness = DefaultThickness) =>
				new(p, new WDist(pointRadius), thickness, color, true);

			static CircleAnnotationRenderable CircleRenderFunc((WPos, WDist) c, Color color, int thickness = DefaultThickness) =>
				new(c.Item1, c.Item2, thickness, color, false);

			// Render States
			foreach (var (ccState, color) in statesWithColors)
			{
				yield return PointRenderFunc(wr.World.Map.WPosFromCCPos(ccState.CC), color);
				if (showCosts)
					yield return new TextAnnotationRenderable(font, wr.World.Map.WPosFromCCPos(ccState.CC), 0,
															color, $"({ccState.Gval})");
			}

			// Render Points
			foreach (var (point, color, thickness) in pointsWithColors)
				yield return PointRenderFunc(point, color, thickness);

			// Render Circles
			foreach (var (circle, color, thickness) in circlesWithColors)
				yield return CircleRenderFunc(circle, color, thickness);

			// Render Paths
			lineColor = Color.FromAhsv(pathHue, currSat, currLight);
			foreach (var path in paths)
			{
				var linesToRender = GetPathRenderableSet(path, lineThickness, lineColor, endPointRadius, endPointThickness, lineColor);
				foreach (var line in linesToRender)
					yield return line;
			}

			// Render Lines
			lineColor = Color.FromAhsv(lineHue, currSat, currLight);
			foreach (var line in lines)
			{
				var linesToRender = GetPathRenderableSet(line, lineThickness, lineColor, endPointRadius, endPointThickness, lineColor);
				foreach (var l in linesToRender)
					yield return l;
			}

			// Render LinesWithColours
			foreach (var (line, color) in linesWithColors)
			{
				var linesToRender = GetPathRenderableSet(line, lineThickness, color, endPointRadius, endPointThickness, color);
				foreach (var l in linesToRender)
					yield return l;
			}
		}

		public void UpdatePointColors()
		{
			var newPointsWithColor = new List<(CCState, Color)>();
			currHue = 0.0F;
			var currColor = Color.FromAhsv(currHue, currSat, currLight);
			lineColorIncrement = 1.0F / statesWithColors.Count;

			for (var i = 0; i < statesWithColors.Count; i++)
			{
				newPointsWithColor.Add((statesWithColors[i].Item1, currColor));
				currHue = (currHue + lineColorIncrement) % (1.0F + float.Epsilon);
				currColor = Color.FromAhsv(currHue, currSat, currLight);
			}

			statesWithColors = newPointsWithColor;
		}

		public void AddState(CCState ccState)
		{
			statesWithColors.Add((ccState, Color.FromAhsv(currHue, currSat, currLight)));
			UpdatePointColors();
		}

		public void RemoveState(CCState ccState)
		{
			statesWithColors.RemoveAll(stateWithColor => stateWithColor.Item1 == ccState);
			UpdatePointColors();
		}

		public void AddPoint(WPos pos)
		{
			pointsWithColors.Add((pos, Color.FromAhsv(pointHue, currSat, currLight), DefaultThickness));
			UpdatePointColors();
		}

		public void AddPoint(WPos pos, Color color, int thickness = DefaultThickness)
		{
			pointsWithColors.Add((pos, color, thickness));
			UpdatePointColors();
		}

		public void RemovePoint(WPos pos)
		{
			foreach (var (currPos, currColor, thickness) in pointsWithColors)
				if (currPos == pos)
					pointsWithColors.Remove((pos, currColor, thickness));
			UpdatePointColors();
		}

		public void AddPath(List<WPos> path) { paths.Add(path); }
		public void RemovePath(List<WPos> path) { paths.RemoveAll(p => p == path); }
		public void AddLine(List<WPos> line) { lines.Add(line); }
		public void RemoveLine(List<WPos> line) { lines.RemoveAll(l => l == line); }
		public void AddLineWithColor(List<WPos> line, Color color) { linesWithColors.Add((line, color)); }
		public void RemoveLineWithColor(List<WPos> line) { linesWithColors.RemoveAll(lwc => lwc.Item1 == line); }
		public void AddCircle((WPos P, WDist D) circle, int thickness = DefaultThickness)
		{ circlesWithColors.Add((circle, Color.FromAhsv(circleHue, currSat, currLight), DefaultThickness)); }
		public void AddCircleWithColor((WPos P, WDist D) circle, Color color, int thickness = DefaultThickness) { circlesWithColors.Add((circle, color, DefaultThickness)); }
		public void RemoveCircle((WPos P, WDist D) circle) { circlesWithColors.RemoveAll(c => c.Item1 == circle); }
		public void ClearIntervals() { statesWithColors.Clear(); }
		public void ClearPaths() { paths.Clear(); }
		public void ClearLines() { lines.Clear(); }
		public void ClearLinesWithColors() { linesWithColors.Clear(); }
		public void ClearStates() { statesWithColors.Clear(); }
		public void ClearPoints() { pointsWithColors.Clear(); }
		public void ClearCircles() { circlesWithColors.Clear(); }
		public void ClearRadiuses() { circlesWithColors.Clear(); }

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
