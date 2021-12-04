using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Pathfinder
{
	[TraitLocation(SystemActors.World)]
	[Desc("Manages the queuing and prioritisation of Theta Pathfinder calculations, to ensure the computer is not overloded.")]

	public class ActorID
	{
		public uint ID;
		public ActorID(uint actorID) { ID = actorID; }
	}

	public class ThetaPathfinderExecutionManagerInfo : TraitInfo<ThetaPathfinderExecutionManagerInfo> { }
	public class ThetaPathfinderExecutionManager : ITick
	{
		public Dictionary<ActorID, ThetaStarPathSearch> ThetaPathfinders = new Dictionary<ActorID, ThetaStarPathSearch>();
		void ITick.Tick(Actor world)
		{
			Tick();
		}

		private void Tick()
		{
			foreach (var thetaPF in ThetaPathfinders)
				if (thetaPF.Value.pathFound == false)
					thetaPF.Value.Expand();
		}
	}
}
