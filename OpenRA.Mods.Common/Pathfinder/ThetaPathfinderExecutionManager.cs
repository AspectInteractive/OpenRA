using System.Collections.Generic;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Manages the queuing and prioritisation of Theta Pathfinder calculations, to ensure the computer is not overloded.")]

	public class ThetaPathfinderExecutionManagerInfo : TraitInfo<ThetaPathfinderExecutionManager> { }
	public class ThetaPathfinderExecutionManager : ITick
	{
		int maxCurrExpansions = 200;

		public ThetaPathfinderExecutionManager() { }

		public Dictionary<Actor, ThetaStarPathSearch> ThetaPathfinders = new Dictionary<Actor, ThetaStarPathSearch>();
		void ITick.Tick(Actor world) { Tick(); }

		public void AddPF(Actor actor, ThetaStarPathSearch thetaPF)
		{
			if (!ThetaPathfinders.ContainsKey(actor))
				ThetaPathfinders.Add(actor, thetaPF);
			ThetaPathfinders[actor] = thetaPF;
		}

		private void Tick()
		{
			foreach (var (_, thetaPF) in ThetaPathfinders)
				if (thetaPF.pathFound == false)
					thetaPF.Expand((int)Fix64.Ceiling((Fix64)maxCurrExpansions / (Fix64)ThetaPathfinders.Count));
		}
	}
}
