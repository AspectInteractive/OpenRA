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
using OpenRA.Mods.Common.Activities;
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

	class CPosCoordsDebugOverlay : IWorldLoaded, IChatCommand
	{
		World world;
		WorldRenderer wr;
		public readonly List<Command> Comms;
		readonly List<TextAnnotationRenderable> annotations = new();

		public bool Enabled;

		readonly SpriteFont font;

		public CPosCoordsDebugOverlay(Actor self, CPosCoordsDebugOverlayInfo info)
		{
			font = Game.Renderer.Fonts[info.Font];
			Comms = new List<Command>()
			{
				new("cpos-coords", "toggles the cpos coordinates debug overlay.", true),
				new("thetall", "toggles all anya pathfinder overlays.", false),
				new("colldebug", "toggles collision debug overlay.", false)
			};
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			this.wr = wr;
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
			if (Comms.Any(comm => comm.Name == name))
				Enabled ^= true;

			if (Enabled)
				GenerateText(wr);
			else
				annotations.Clear();
		}

		void GenerateText(WorldRenderer wr)
		{
			foreach (var uv in wr.Viewport.VisibleCellsInsideBounds.CandidateMapCoords)
			{
				if (world.ShroudObscures(uv))
					continue;

				var cell = uv.ToCPos(wr.World.Map);
				var textPos = wr.World.Map.CenterOfCell(cell) - new WVec(0, 512, 0);
				var color = Color.White;
				string cellText;

				//var locomotorActor = wr.World.ActorsHavingTrait<MobileOffGrid>().FirstOrDefault();
				//Locomotor locomotor = null;
				//if (locomotorActor != null)
				//	locomotor = locomotorActor.TraitsImplementing<MobileOffGrid>().FirstOrDefault().Locomotor;
				//if (locomotor != null)
				//	cellText = $"({locomotor.MovementCostToEnterCell(locomotorActor, cell, BlockedByActor.All, null, false, SubCell.FullCell)})";
				//else

				cellText = $"({cell.X},{cell.Y})";
				annotations.Add(new TextAnnotationRenderable(wr.World, font, textPos, 0, color, cellText));
			}
		}
	}
}
