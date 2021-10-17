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
		public readonly List<Command> Comms;

		private List<(CCState, Color)> pointsWithColors = new List<(CCState, Color)>();
		private List<List<WPos>> paths = new List<List<WPos>>();

		public bool Enabled;
		private float currHue = Color.Blue.ToAhsv().H; // 0.0 - 1.0
		private float pathHue = Color.Yellow.ToAhsv().H; // 0.0 - 1.0
		private float currSat = 1.0F; // 0.0 - 1.0
		private float currLight = 0.7F; // 0.0 - 1.0 with 1.0 being brightest
		private float lineColorIncrement = 0.05F;
		public Action<string> ToggleVisibility;

		public ThetaStarPathfinderOverlay()
		{
			Comms = new List<Command>()
			{
				new Command("theta", "toggles the theta star pathfinder overlay.", true),
				new Command("thetall", "toggles all theta star pathfinder overlays.", true)
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

			Func<WPos, Color, CircleAnnotationRenderable> pointRenderFunc = (p, color) =>
				{ return new CircleAnnotationRenderable(p, new WDist(pointRadius), pointThickness, color, true, 2); };

			// Render Points
			foreach (var (ccState, color) in pointsWithColors)
			{
				yield return pointRenderFunc(wr.World.Map.WPosFromCCPos(ccState.CC), color);
				yield return new TextAnnotationRenderable(font, wr.World.Map.WPosFromCCPos(ccState.CC), 0,
															color, $"({ccState.Gval / 1024 / 512})", 4);
			}

			// Render Paths
			var lineColor = Color.FromAhsv(pathHue, currSat, currLight);
			foreach (var path in paths)
			{
				var linesToRender = GetPathRenderableSet(path, lineThickness, lineColor, endPointRadius, endPointThickness, lineColor);
				foreach (var line in linesToRender)
					yield return line;
			}
		}

		public void AddState(CCState ccState)
		{
			pointsWithColors.Add((ccState, Color.FromAhsv(currHue, currSat, currLight)));
			UpdatePointColors();
		}

		public void UpdatePointColors()
		{
			var newPointsWithColor = new List<(CCState, Color)>();
			currHue = 0.0F;
			var currColor = Color.FromAhsv(currHue, currSat, currLight);
			lineColorIncrement = 1.0F / pointsWithColors.Count;

			for (var i = 0; i < pointsWithColors.Count; i++)
			{
				newPointsWithColor.Add((pointsWithColors.ElementAt(i).Item1, currColor));
				currHue = (currHue + lineColorIncrement) % (1.0F + float.Epsilon);
				currColor = Color.FromAhsv(currHue, currSat, currLight);
			}

			pointsWithColors = newPointsWithColor;
		}

		public void RemoveState(CCState ccState)
		{
			foreach (var (currCCstate, currColor) in pointsWithColors)
				if (currCCstate == ccState)
					pointsWithColors.Remove((ccState, currColor));
			UpdatePointColors();
		}

		public void AddPath(List<WPos> path) { paths.Add(path); }
		public void RemovePath(List<WPos> path)
		{
			foreach (var currPath in paths)
				if (currPath == path)
					paths.Remove(currPath);
		}

		public void ClearIntervals() { pointsWithColors.Clear(); }
		public void ClearPaths() { paths.Clear(); }

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
