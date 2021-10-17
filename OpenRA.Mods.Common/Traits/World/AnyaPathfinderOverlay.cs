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
using static OpenRA.Mods.Common.Pathfinder.AnyaPathSearch;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay of the Anya Pathfinder intervals and paths. Attach this to the world actor.")]
	public class AnyaPathfinderOverlayInfo : TraitInfo<AnyaPathfinderOverlay> { }

	public class AnyaPathfinderOverlay : IRenderAnnotations, IWorldLoaded, IChatCommand
	{
		public readonly List<Command> Comms;

		private List<(Interval, Color)> intervalsWithColors = new List<(Interval, Color)>();
		private List<List<WPos>> paths = new List<List<WPos>>();

		public bool Enabled;
		private float currHue = Color.Blue.ToAhsv().H; // 0.0 - 1.0
		private float pathHue = Color.Yellow.ToAhsv().H; // 0.0 - 1.0
		private float currSat = 1.0F; // 0.0 - 1.0
		private float currLight = 0.7F; // 0.0 - 1.0 with 1.0 being brightest
		private float lineColorIncrement = 0.05F;
		public Action<string> ToggleVisibility;

		public AnyaPathfinderOverlay()
		{
			Comms = new List<Command>()
			{
				new Command("anya", "toggles the anya pathfinder overlay.", true),
				new Command("anyall", "toggles all anya pathfinder overlays.", false)
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
			if (Comms.Where(comm => comm.Name == name).Any())
			{
				Enabled ^= true;
				ToggleVisibility("");
			}
		}

		public static void GenericLinkedPointsFunc<T1>(List<T1> pointList, int pointListLen, Action<T1, T1> funcOnLinkedPoints)
		{
			for (var i = 0; i < (pointListLen - 1); i++)
			{
				var currItem = pointList[i];
				var nextItem = pointList[i + 1];
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static void GenericLinkedPointsFunc<T1, T2>(List<T1> pointList, int pointListLen, Func<T1, T2> pointUnpacker,
														Action<T2, T2> funcOnLinkedPoints)
		{
			for (var i = 0; i < (pointListLen - 1); i++)
			{
				var currItem = pointUnpacker(pointList[i]);
				var nextItem = pointUnpacker(pointList[i + 1]);
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static List<LineAnnotationRenderable> GetPathRenderableSet(List<WPos> path, int lineThickness, Color lineColor, int endPointRadius,
																	int endPointThickness, Color endPointColor)
		{
			var linesToRender = new List<LineAnnotationRenderable>();
			Action<WPos, WPos> funcOnLinkedPoints = (wpos1, wpos2) => linesToRender.Add(new LineAnnotationRenderable(wpos1, wpos2,
																			lineThickness, lineColor, lineColor,
																			(endPointRadius, endPointThickness, endPointColor), 3));
			GenericLinkedPointsFunc(path, path.Count, funcOnLinkedPoints);
			return linesToRender;
		}

		public static List<LineAnnotationRenderable> GetIntervalRenderableSet(Interval interval, int lineThickness, Color lineColor, int endPointRadius,
																	int endPointThickness, Color endPointColor, World world)
		{
			var linesToRender = new List<LineAnnotationRenderable>();
			Func<CCPos, WPos> pointUnpacker = cc => world.Map.WPosFromCCPos(cc);
			Action<WPos, WPos> funcOnLinkedPoints = (wpos1, wpos2) => linesToRender.Add(new LineAnnotationRenderable(wpos1, wpos2,
																			lineThickness, lineColor, lineColor,
																			(endPointRadius, endPointThickness, endPointColor), 2));
			GenericLinkedPointsFunc(interval.CCs, interval.CCs.Count, pointUnpacker, funcOnLinkedPoints);
			return linesToRender;
		}

		/*public static List<LineAnnotationRenderable> GetIntervalRenderableSet(Interval interval, int lineThickness, Color lineColor, int endPointRadius,
																	int endPointThickness, Color endPointColor, World world)
		{
			var linesToRender = new List<LineAnnotationRenderable>();
			for (var i = 0; i < (interval.CCs.Count - 1); i++)
			{
				var iWPos = world.Map.WPosFromCCPos(interval.CCs[i]);
				var iNextWPos = world.Map.WPosFromCCPos(interval.CCs[i + 1]);
				linesToRender.Add(new LineAnnotationRenderable(iWPos, iNextWPos, lineThickness,
														  lineColor, lineColor, (endPointRadius, endPointThickness, endPointColor), 2));
			}

			return linesToRender;
		}*/

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			var lineThickness = 3;
			var endPointRadius = 100;
			var endPointThickness = lineThickness;

			// Render Intervals
			foreach (var (interval, color) in intervalsWithColors)
			{
				var linesToRender = GetIntervalRenderableSet(interval, lineThickness, color,
															endPointRadius, endPointThickness, color, wr.World);
				foreach (var line in linesToRender)
					yield return line;
			}

			// Render Paths
			var lineColor = Color.FromAhsv(pathHue, currSat, currLight);
			foreach (var path in paths)
			{
				var linesToRender = GetPathRenderableSet(path, lineThickness, lineColor,
														endPointRadius, endPointThickness, lineColor);
				foreach (var line in linesToRender)
					yield return line;
			}
		}

		public void AddInterval(Interval interval)
		{
			currHue = (currHue + lineColorIncrement) % (1.0F + float.Epsilon); // each interval has a new colour to show recency
			/* System.Console.WriteLine($"Writing Color: {currHue}, {currSat}, {currLight}"); */
			intervalsWithColors.Add((interval, Color.FromAhsv(currHue, currSat, currLight)));
		}

		public void RemoveInterval(Interval interval)
		{
			foreach (var (currInterval, currColor) in intervalsWithColors)
			{
				if (currInterval == interval)
					intervalsWithColors.Remove((currInterval, currColor));
			}
		}

		public void AddPath(List<WPos> path) { paths.Add(path); }
		public void RemovePath(List<WPos> path)
		{
			foreach (var currPath in paths)
				if (currPath == path)
					paths.Remove(currPath);
		}

		public void ClearIntervals() { intervalsWithColors.Clear(); }
		public void ClearPaths() { paths.Clear(); }

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
