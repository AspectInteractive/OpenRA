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

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Displays the CPos coordinates for each cell.")]
	class CPosCoordsDebugOverlayInfo : TraitInfo
	{
		public readonly string Font = "TinyBold";

		public override object Create(ActorInitializer init) { return new CPosCoordsDebugOverlay(init.Self, this); }
	}

	class CPosCoordsDebugOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		public readonly List<Command> Comms;

		public bool Enabled;

		readonly SpriteFont font;

		public CPosCoordsDebugOverlay(Actor self, CPosCoordsDebugOverlayInfo info)
		{
			font = Game.Renderer.Fonts[info.Font];
			Comms = new List<Command>()
			{
				new Command("cpos-coords", "toggles the cpos coordinates debug overlay.", true),
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

			foreach (var uv in wr.Viewport.VisibleCellsInsideBounds.CandidateMapCoords)
			{
				if (self.World.ShroudObscures(uv))
					continue;

				var cell = uv.ToCPos(wr.World.Map);
				var center = wr.World.Map.CenterOfCell(cell);
				var color = Color.White;
				var locomotorActor = wr.World.ActorsHavingTrait<MobileOffGrid>().FirstOrDefault();
				Locomotor locomotor = null;
				if (locomotorActor != null)
					locomotor = locomotorActor.TraitsImplementing<MobileOffGrid>().FirstOrDefault().Locomotor;
				string cellText;
				if (locomotor != null)
					cellText = $"({locomotor.MovementCostToEnterCell(locomotorActor, cell, BlockedByActor.All, null, false, SubCell.FullCell)})";
				else
					cellText = $"({cell.X},{cell.Y})";

				yield return new TextAnnotationRenderable(font, center, 0, color, cellText);
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
