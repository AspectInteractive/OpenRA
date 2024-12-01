﻿using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;
using RVO;
using static OpenRA.Mods.Common.Traits.CollisionDebugOverlay;

namespace OpenRA.Mods.Common.Traits
{
	public class RVOManagerInfo : TraitInfo<RVOManager> { }
	public class RVOManager : IWorldLoaded, ITick
	{
		World world;
		Blocks rvoBlocks;
		bool RVOtest = true;
		CollisionDebugOverlay collDebugOverlay;
		BasicCellDomainManager bcdManager;

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			collDebugOverlay = w.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();
			bcdManager = world.WorldActor.TraitsImplementing<BasicCellDomainManager>().FirstEnabledTraitOrDefault();
		}

		public WPos GetAgentSpawnLocation(Map map) => new(map.MapSize.X * 1024 / 2, map.MapSize.Y * 1024 / 2, 0);

		// We subtract the agent spawn location as that is what RVO requires
		public WPos GetObstaclePos(WPos pos, WPos agentSpawnLoc) => pos - (WVec)agentSpawnLoc;

		public List<IList<Vector2>> GetObstacles(List<List<List<WPos>>> obstacles, WPos agentSpawnLoc)
		{
			var rvoObstacles = new List<IList<Vector2>>();

			foreach (var obstacle in obstacles)
				rvoObstacles.Add(EdgeSetToObstacle(obstacle, agentSpawnLoc));

			return rvoObstacles;
		}

		public Vector2 WPosToVector2(WPos pos, WPos agentSpawnLoc)
		{
			var obstaclePos = GetObstaclePos(pos, agentSpawnLoc);
			return new Vector2(obstaclePos.X, obstaclePos.Y);
		}

		public IList<Vector2> EdgeSetToObstacle(List<List<WPos>> obstacle, WPos agentSpawnLoc)
		{
			var rvoObstacle = new List<Vector2>
			{
				// Add the first starting position of the first edge
				WPosToVector2(obstacle[0][0], agentSpawnLoc)
			};

			// Add the end position of every subsequent edge
			foreach (var edge in obstacle)
				rvoObstacle.Add(WPosToVector2(edge[1], agentSpawnLoc));

			return rvoObstacle;
		}

		public void Tick(Actor self)
		{
			var rvoObject = rvoBlocks;

			// Call RVO Tick
			if (RVOtest && collDebugOverlay.Enabled && rvoObject == null && bcdManager.ObstaclesSet)
			{
				Simulator.Instance.Clear();
				var rvoObstacles = GetObstacles(bcdManager.Obstacles, GetAgentSpawnLocation(world.Map));
				rvoObject = new Blocks(rvoObstacles);
				rvoBlocks = rvoObject;
				collDebugOverlay.SetAgentAmount(rvoObject.getAgentCount());
			}
			else if (RVOtest && collDebugOverlay.Enabled)
			{
				//collDebugOverlay.ClearAll();

				//var agents = rvoObject.getAgents().ToList();
				//var agentSpawnLocation = GetAgentSpawnLocation(world.Map);

				//var obstacles = bcdManager.Obstacles;

				//var obstacleLines = Simulator.Instance.getObstacles().Select(o => (o.point_, o.next_.point_));
				//foreach (var ol in obstacleLines)
				//foreach (var obstacle in obstacles)
				//{
				//	foreach (var (_, edge) in obstacle)
				//	{
				//		var ol = (new Vector2(edge[0].X, edge[0].Y), new Vector2(edge[1].X, edge[1].Y));
				//		collDebugOverlay.AddLine(new WPos((int)ol.Item1.x(), (int)ol.Item1.y(), 0) + (WVec)agentSpawnLocation,
				//								 new WPos((int)ol.Item2.x(), (int)ol.Item2.y(), 0) + (WVec)agentSpawnLocation, 3);
				//	}
				//}

				//for (var i = 0; i < agents.Count; i++)
				//{
				//	var agentPosSpawn = new WPos((int)agents[i].position_.x(), (int)agents[i].position_.y(), 0) + (WVec)agentSpawnLocation;
				//	collDebugOverlay.AddOrUpdateRVONode(new RVONode(agentPosSpawn, rvoCircleRadius, agents[i].id_));
				//}

				rvoObject.Tick();
			}
		}
	}
}
