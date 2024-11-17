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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay of the Anya Pathfinder intervals and paths. Attach this to the world actor.")]
	public class ClearThetaStarPathfinderOverlayInfo : TraitInfo<ClearThetaStarPathfinderOverlay> { }

	public class ClearThetaStarPathfinderOverlay : IWorldLoaded, IChatCommand
	{
		public readonly List<Command> Comms;
		public Action ClearFunc;
		public bool Enabled;

		public ClearThetaStarPathfinderOverlay()
		{
			Comms = new List<Command>()
			{
				new Command("clr", "clears any existing anya pathfinder overlay intervals.", true)
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

			ClearFunc = () =>
			{
				var thetaStarPathFinderTrait = w.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault();
				thetaStarPathFinderTrait.ClearAll();

				var collDebugOverlayTrait = w.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();
				collDebugOverlayTrait.ClearAll();

				var mobileOffGridOverlays = w.ActorsWithTrait<MobileOffGridOverlay>().Select(a => a.Trait).ToList();
				foreach (var overlay in mobileOffGridOverlays)
				{
					overlay.ClearLines();
					overlay.ClearPoints();
					overlay.ClearCircles();
					overlay.ClearTexts();
				}

			};
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (Comms.Where(comm => comm.Name == name).Any())
				ClearFunc();
		}
	}
}
