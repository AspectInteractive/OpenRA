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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay showing the terrain cells. Attach this to the world actor.")]
	public class TerrainGeometryOverlayInfo : TraitInfo<TerrainGeometryOverlay> { }

	public class TerrainGeometryOverlay : IRenderAnnotations, IWorldLoaded, IChatCommand
	{
		public readonly List<Command> Comms;

		[TranslationReference]
		const string CommandDescription = "description-terrain-geometry-overlay";

		public bool Enabled;

		public TerrainGeometryOverlay()
		{
			Comms = new List<Command>()
			{
				new Command("terrain-geometry", "toggles the terrain geometry overlay.", true),
				new Command("thetall", "toggles all anya pathfinder overlays.", false)
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
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (Comms.Where(comm => comm.Name == name).Any())
				Enabled ^= true;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			var map = wr.World.Map;
			var colors = wr.World.Map.Rules.TerrainInfo.HeightDebugColors;
			var mouseCell = wr.Viewport.ViewToWorld(Viewport.LastMousePos).ToMPos(wr.World.Map);

			/* // For testing cellList Output only
			var testP0 = self.World.Map.CenterOfCell(new CPos(40, 30));
			var testP1 = self.World.Map.CenterOfCell(new CPos(50, 50));
			var testColor = Color.Green;
			var cellList = ThetaStarPathSearch.GetAllCellsUnderneathALine(self.World, testP0, testP1, 1);
			yield return new LineAnnotationRenderable(testP0, testP1, 3, testColor, testColor, 4);
			foreach (var cell in cellList)
				yield return new CircleAnnotationRenderable(self.World.Map.CenterOfCell(cell), new WDist(500),
															5, Color.Yellow, true, 2);*/


			// Define Blocked and Open Cell Lists
			var blockedVisibleCells = new List<MPos>();
			var openVisibleCells = new List<MPos>();
			var locomotor = wr.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			foreach (var cell in wr.Viewport.AllVisibleCells.CandidateMapCoords)
			{
				if (locomotor.MovementCostToEnterCell(default, cell.ToCPos(map), BlockedByActor.Immovable, null) == short.MaxValue)
					blockedVisibleCells.Add(cell);
				else
					openVisibleCells.Add(cell);
			}


			// Go through Open Cell List second so that it overlays on top of the blocked list
			foreach (var uv in openVisibleCells)
			{
				if (!map.Height.Contains(uv) || self.World.ShroudObscures(uv))
					continue;

				var height = (int)map.Height[uv];
				var r = map.Grid.Ramps[map.Ramp[uv]];
				var pos = map.CenterOfCell(uv.ToCPos(map)) - new WVec(0, 0, r.CenterHeightOffset);
				var te = map.TopEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var be = map.BottomEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var le = map.LeftEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var re = map.RightEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var thickness = uv == mouseCell ? 3 : 1;

				var blockedColor = Color.LightYellow;
				var endPointColor = blockedColor;

				// Colors change between points, so render separately
				foreach (var p in r.Polygons)
				{
					for (var i = 0; i < p.Length; i++)
					{
						var j = (i + 1) % p.Length;
						var start = pos + p[i];
						var end = pos + p[j];
						var startColor = colors[height + p[i].Z / 512];
						var endColor = colors[height + p[j].Z / 512];
						yield return new LineAnnotationRenderableWithZIndex(start, end, thickness, startColor, endColor);
					}
				}
			}

			// Go through Blocked Cell List first
			foreach (var uv in blockedVisibleCells)
			{
				if (!map.Height.Contains(uv) || self.World.ShroudObscures(uv))
					continue;

				var height = (int)map.Height[uv];
				var r = map.Grid.Ramps[map.Ramp[uv]];
				var pos = map.CenterOfCell(uv.ToCPos(map)) - new WVec(0, 0, r.CenterHeightOffset);
				var te = map.TopEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var be = map.BottomEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var le = map.LeftEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var re = map.RightEdgeOfCell(uv.ToCPos(map)).Select(p => p - new WVec(0, 0, r.CenterHeightOffset));
				var thickness = uv == mouseCell ? 3 : 1;

				var blockedColor = Color.LightYellow;
				var endPointColor = blockedColor;

				foreach (var p in r.Polygons)
				{
					for (var i = 0; i < p.Length; i++)
					{
						var j = (i + 1) % p.Length;
						var start = pos + p[i];
						var end = pos + p[j];
#if DEBUG || DEBUGWITHOVERLAY
						//yield return new LineAnnotationRenderableWithZIndex(te.ElementAt(0), te.ElementAt(1), 3, Color.Red, Color.Red);
						//yield return new LineAnnotationRenderableWithZIndex(be.ElementAt(0), be.ElementAt(1), 3, Color.Blue, Color.Blue);
						//yield return new LineAnnotationRenderableWithZIndex(le.ElementAt(0), le.ElementAt(1), 3, Color.Orange, Color.Orange);
						//yield return new LineAnnotationRenderableWithZIndex(re.ElementAt(0), re.ElementAt(1), 3, Color.Pink, Color.Pink);
						yield return new LineAnnotationRenderableWithZIndex(start, end, thickness,
																	blockedColor, blockedColor, (100, 3, endPointColor));
#else
#endif
					}
				}
			}

			// Projected cell coordinates for the current cell
			var projectedCorners = map.Grid.Ramps[0].Corners;
			foreach (var puv in map.ProjectedCellsCovering(mouseCell))
			{
				var pos = map.CenterOfCell(((MPos)puv).ToCPos(map));
				for (var i = 0; i < 4; i++)
				{
					var j = (i + 1) % 4;
					var start = pos + projectedCorners[i] - new WVec(0, 0, pos.Z);
					var end = pos + projectedCorners[j] - new WVec(0, 0, pos.Z);
					yield return new LineAnnotationRenderable(start, end, 3, Color.Navy);
				}
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
