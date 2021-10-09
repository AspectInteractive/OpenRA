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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay showing the terrain cells. Attach this to the world actor.")]
	public class TerrainGeometryOverlayInfo : TraitInfo<TerrainGeometryOverlay> { }

	public class TerrainGeometryOverlay : IRenderAnnotations, IWorldLoaded, IChatCommand
	{
		const string CommandName = "terrain-geometry";
		const string CommandDesc = "toggles the terrain geometry overlay.";

		public bool Enabled;

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			var help = w.WorldActor.TraitOrDefault<HelpCommand>();

			if (console == null || help == null)
				return;

			console.RegisterCommand(CommandName, this);
			help.RegisterHelp(CommandName, CommandDesc);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (name == CommandName)
				Enabled ^= true;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			var map = wr.World.Map;
			var colors = wr.World.Map.Rules.TerrainInfo.HeightDebugColors;
			var mouseCell = wr.Viewport.ViewToWorld(Viewport.LastMousePos).ToMPos(wr.World.Map);

			foreach (var uv in wr.Viewport.AllVisibleCells.CandidateMapCoords)
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

				var locomotor = wr.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
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
						if (locomotor.MovementCostToEnterCell(default, uv.ToCPos(map), BlockedByActor.Immovable, null) == short.MaxValue)
							yield return new LineAnnotationRenderable(start, end, thickness, blockedColor, blockedColor, 2);
						else
							yield return new LineAnnotationRenderable(start, end, thickness, startColor, endColor);
					}
				}

				foreach (var p in r.Polygons)
				{
					for (var i = 0; i < p.Length; i++)
					{
						if (locomotor.MovementCostToEnterCell(default, uv.ToCPos(map), BlockedByActor.Immovable, null) == short.MaxValue)
						{
							var j = (i + 1) % p.Length;
							var start = pos + p[i];
							var end = pos + p[j];
							#if DEBUG
							/*yield return new LineAnnotationRenderable(te.ElementAt(0), te.ElementAt(1), 3, Color.Red, Color.Red);
							yield return new LineAnnotationRenderable(be.ElementAt(0), be.ElementAt(1), 3, Color.Blue, Color.Blue);
							yield return new LineAnnotationRenderable(le.ElementAt(0), le.ElementAt(1), 3, Color.Orange, Color.Orange);
							yield return new LineAnnotationRenderable(re.ElementAt(0), re.ElementAt(1), 3, Color.Pink, Color.Pink);*/
							yield return new LineAnnotationRenderable(start, end, thickness,
																	  blockedColor, blockedColor, (100, 3, endPointColor), 2);
							#else
							#endif
						}
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
