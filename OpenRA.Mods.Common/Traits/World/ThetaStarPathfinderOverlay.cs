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
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Pathfinder.ThetaStarPathSearch;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay of the Anya Pathfinder intervals and paths. Attach this to the world actor.")]
	public class ThetaStarPathfinderOverlayInfo : TraitInfo<ThetaStarPathfinderOverlay> { }

	public class ThetaStarPathfinderOverlay : IRenderAnnotations, IWorldLoaded, IChatCommand
	{
		World world;
		public readonly List<Command> Comms;
		readonly List<(List<WPos>, Color C, string Key)> linesWithColors = new();
		readonly List<((WPos Pos, WDist Dist), Color C, string Key)> circlesWithColors = new();
		readonly List<(WPos Pos, Color C, string Key)> pointsWithColors = new();
		List<(CCState, Color)> statesWithColors = new();
		readonly List<string> enabledOverlays = new();
		bool NoFiltering => enabledOverlays.Count == 0;
		readonly List<(List<WPos> Path, Color? C)> paths = new();
		readonly List<(List<WPos> Line, string Key)> lines = new();

		public struct OverlayKeyStrings
		{
			public const string Path = "path";  // toggles the theta path
			public const string HeatMap = "heatmap"; // toggles the theta path heat map
			public const string Circles = "circles"; // toggles the circles and slices in the theta PF execution manager
		}

		// Set this to true to display annotations showing the cost at each cell
		private readonly bool showCosts = true;

		public bool Enabled;
		private float currHue = Color.Blue.ToAhsv().H; // 0.0 - 1.0
		private float pointHue = Color.Red.ToAhsv().H; // 0.0 - 1.0
		private float circleHue = Color.LightGreen.ToAhsv().H; // 0.0 - 1.0
		private float pathHue = Color.Yellow.ToAhsv().H; // 0.0 - 1.0
		private float lineHue = Color.LightBlue.ToAhsv().H; // 0.0 - 1.0
		private float currSat = 1.0F; // 0.0 - 1.0
		private float currLight = 0.7F; // 0.0 - 1.0 with 1.0 being brightest
		private float lineColorIncrement = 0.05F;
		public Action<string> ToggleVisibility;

		readonly List<string> validChatCommandArgs =
			new()
			{
				OverlayKeyStrings.Path,
				OverlayKeyStrings.HeatMap,
				OverlayKeyStrings.Circles,
			};

		public ThetaStarPathfinderOverlay()
		{
			Comms = new List<Command>()
			{
				new("theta", "toggles the theta star pathfinder overlay.", true),
				new("thetall", "toggles all theta star pathfinder overlays.", true)
			};
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
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
			// Only enable/disable if no argument is passed
			if (Comms.Any(comm => comm.Name == name) && string.IsNullOrEmpty(arg))
			{
				Enabled ^= true;
				ToggleVisibility("");
			}

			// If the overlay does not exist and cannot be removed, then we add it
			if (validChatCommandArgs.Any(validArg => arg == validArg) && !enabledOverlays.Remove(arg))
				enabledOverlays.Add(arg);
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
			var pointThickness = 3;
			var lineThickness = 3;
			var endPointRadius = 100;
			var endPointThickness = 3;
			var fontName = "TinyBold";
			var font = Game.Renderer.Fonts[fontName];
			Color lineColor;

			CircleAnnotationRenderable PointRenderFunc(WPos p, Color color)	=> new(p, new WDist(pointRadius), pointThickness, color, true);
			CircleAnnotationRenderable CircleRenderFunc((WPos, WDist) c, Color color) => new(c.Item1, c.Item2, pointThickness, color, false);

			// Render States
			if (NoFiltering || enabledOverlays.Contains(OverlayKeyStrings.HeatMap))
			{
				foreach (var (ccState, color) in statesWithColors)
				{
					yield return PointRenderFunc(wr.World.Map.WPosFromCCPos(ccState.CC), color);
					if (showCosts)
						yield return new TextAnnotationRenderable(font, wr.World.Map.WPosFromCCPos(ccState.CC), 0,
																color, $"({ccState.Gval})");
				}
			}

			// Render Points
			foreach (var (point, color, _) in pointsWithColors.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return PointRenderFunc(point, color);

			// Render Circles
			foreach (var (circle, color, _) in circlesWithColors.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
				yield return CircleRenderFunc(circle, color);

			// Render Paths
			lineColor = Color.FromAhsv(pathHue, currSat, currLight);
			if (NoFiltering || enabledOverlays.Contains(OverlayKeyStrings.Path))
			{
				foreach (var (path, color) in paths)
				{
					var linesToRender = GetPathRenderableSet(path, lineThickness, color ?? lineColor, endPointRadius, endPointThickness, lineColor);
					foreach (var line in linesToRender)
						yield return line;
				}
			}

			// Render Lines
			lineColor = Color.FromAhsv(lineHue, currSat, currLight);
			foreach (var (line, _) in lines.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
			{
				var linesToRender = GetPathRenderableSet(line, lineThickness, lineColor, endPointRadius, endPointThickness, lineColor);
				foreach (var l in linesToRender)
					yield return l;
			}

			// Render LinesWithColours
			foreach (var (line, color, _) in linesWithColors.Where(o => NoFiltering || enabledOverlays.Contains(o.Key)))
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

		public void AddPoint(WPos pos, string key)
		{
			pointsWithColors.Add((pos, Color.FromAhsv(pointHue, currSat, currLight), key));
			UpdatePointColors();
		}

		public void AddPoint(WPos pos, Color color, string key)
		{
			pointsWithColors.Add((pos, color, key));
			UpdatePointColors();
		}

		public void RemovePoint(WPos pos)
		{
			pointsWithColors.RemoveAll(p => p.Pos == pos);
			UpdatePointColors();
		}

		public void AddPath(List<WPos> path, Color? color = null) { paths.Add((path, color)); }
		public void RemovePath(List<WPos> path)	{ paths.RemoveAll(p => p.Path == path); }
		public void AddLine(List<WPos> line, string key) { lines.Add((line, key)); }
		public void RemoveLine(List<WPos> line) { lines.RemoveAll(l => l.Line == line); }
		public void AddLineWithColor(List<WPos> line, Color color, string key) { linesWithColors.Add((line, color, key)); }
		public void RemoveLineWithColor(List<WPos> line) { linesWithColors.RemoveAll(lwc => lwc.Item1 == line); }
		public void AddCircle((WPos Pos, WDist Dist) circle, string key) { circlesWithColors.Add((circle, Color.FromAhsv(circleHue, currSat, currLight), key)); }
		public void AddCircleWithColor((WPos Pos, WDist Dist) circle, Color color, string key) { circlesWithColors.Add((circle, color, key)); }
		public void RemoveCircle((WPos Pos, WDist Dist) circle) { circlesWithColors.RemoveAll(c => c.Item1 == circle); }
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
