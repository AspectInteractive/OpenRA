using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;
using RVO;

namespace OpenRA.Mods.Common.Traits
{
	public class RVOManagerInfo : TraitInfo<RVOManager> { }
	public class RVOManager : IWorldLoaded, ITick
	{
		World world;
		Circle rvoCircle;
		Blocks rvoBlocks;
		Roadmap rvoRoadmap;
		bool RVOtest = false;
		CollisionDebugOverlay collDebugOverlay;

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			collDebugOverlay = w.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();
		}

		public void Tick(Actor self)
		{
			var rvoObject = rvoBlocks;

			// Call RVO Tick
			if (RVOtest && collDebugOverlay.Enabled && rvoObject == null)
			{
				Simulator.Instance.Clear();
				//rvoObject = new RVO.Circle();
				//rvoCircle = rvoObject;
				rvoObject = new Blocks();
				rvoBlocks = rvoObject;
			}
			else if (RVOtest && collDebugOverlay.Enabled)
			{
				collDebugOverlay.ClearAll();

				var agentPositions = rvoObject.getAgentPositions();
				var agentSpawnLocation = new WPos(world.Map.MapSize.X * 1024 / 2, world.Map.MapSize.Y * 1024 / 2, 0);

				var obstacleLines = Simulator.Instance.getObstacles().Select(o => (o.point_, o.next_.point_));
				foreach (var ol in obstacleLines)
					collDebugOverlay.AddLine(new WPos((int)ol.Item1.x(), (int)ol.Item1.y(), 0) + (WVec)agentSpawnLocation,
											 new WPos((int)ol.Item2.x(), (int)ol.Item2.y(), 0) + (WVec)agentSpawnLocation, 3);

				foreach (var agentPos in agentPositions)
				{
					var agentPosSpawn = new WPos((int)agentPos.x(), (int)agentPos.y(), 0) + (WVec)agentSpawnLocation;
					MoveOffGrid.RenderCircleColorCollDebug(world.WorldActor, agentPosSpawn, new WDist(300), Color.Purple, 3);
				}

				rvoObject.Tick();
			}
		}
	}
}
