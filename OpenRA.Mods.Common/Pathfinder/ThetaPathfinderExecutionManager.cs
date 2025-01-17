﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;


#pragma warning disable SA1108 // Block statements should not contain embedded comments

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Manages the queuing and prioritisation of Theta Pathfinder calculations, to ensure the computer is not overloded.")]

	public class ThetaPathfinderExecutionManagerInfo : TraitInfo<ThetaPathfinderExecutionManager> { }
	public class ThetaPathfinderExecutionManager : ITick, IResolveGroupedOrder, IWorldLoaded
	{
		RVO.Circle rvoCircle;
		RVO.Blocks rvoBlocks;
		RVO.Roadmap rvoRoadmap;

		public class ThetaCircle
		{
			public struct SliceGroup
			{
				public int StartIndex;
				public int EndIndex;
				public SliceGroup(int startIndex, int endIndex)
				{
					StartIndex = startIndex;
					EndIndex = endIndex;
				}
			}

			public WPos CircleCenter;
			public WDist CircleRadius;
			public List<SliceGroup> SliceGroups = new();
			public bool SliceGroupsAreSet;

			public ThetaCircle(WPos circleCenter, WDist circleRadius, bool sliceGroupsSet = false)
			{
				CircleCenter = circleCenter;
				CircleRadius = circleRadius;
				SliceGroupsAreSet = sliceGroupsSet;
			}
		}

		public struct CircleSliceIndex
		{
			public PlayerCircleGroupIndex PlayerCI;
			public int CircleIndex;
			public int SliceIndex;

			public CircleSliceIndex(PlayerCircleGroupIndex playerCircleIndex, int circleIndex, int sliceIndex)
			{
				PlayerCI = playerCircleIndex;
				CircleIndex = circleIndex;
				SliceIndex = sliceIndex;
			}
		}
		public struct PlayerCircleGroupIndex
		{
			public Player PlayerOwner;
			public WPos PFTarget;

			public PlayerCircleGroupIndex(Player playerOwner, WPos pfTarget)
			{
				PlayerOwner = playerOwner;
				PFTarget = pfTarget;
			}
		}

		public struct ActorWithOrder
		{
			public Actor Actor;
			public WPos TargetPos;

			public ActorWithOrder(Actor actor, WPos targetPos)
			{
				Actor = actor;
				TargetPos = targetPos;
			}
		}

		// The number of expansions allowed across all Theta pathfinders
		Locomotor locomotor;
		readonly int maxCurrExpansions = 500;
		readonly int radiusForSharedThetas = 1024 * 10;
		readonly int minDistanceForCircles = 0; // used to be 1024 * 28
		readonly int sliceAngle = 10;
		readonly int maxCircleSlices = 36;
		readonly Dictionary<PlayerCircleGroupIndex, List<ThetaCircle>> playerCircleGroups = new();
		public Dictionary<CircleSliceIndex, List<ActorWithOrder>> ActorOrdersInCircleSlices = new();
		public List<ThetaStarPathSearch> ThetaPFsToRun = new();
		List<(ThetaStarPathSearch, ThetaPFAction)> thetaPFActions = new();
		public bool PlayerCirclesLocked = false;

		enum ThetaPFAction { Add, Remove }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			locomotor = w.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}

		public bool GreaterThanMinDistanceForCircles(Actor actor, WPos targetPos)
		{ return (targetPos - actor.CenterPosition).LengthSquared > minDistanceForCircles * minDistanceForCircles; }

		public int GetOppositeSlice(int slice) { return (int)(((Fix64)slice + (Fix64)maxCircleSlices / (Fix64)2) % (Fix64)maxCircleSlices); }

		// Checks from the center of the circle outward to the end of the slice to see if any cell blocks it
		public bool SliceIsBlockedByCell(Actor self, WPos circleCenter, int sliceIndex)
		{
			var sliceAbsoluteAngle = (int)(sliceAngle * (sliceIndex - 0.5)); // We subtract 0.5 as we want the middle of the slice
			var move = new WVec(new WDist(radiusForSharedThetas), WRot.FromYaw(WAngle.FromDegrees(sliceAbsoluteAngle)));
			return self.Trait<MobileOffGrid>().HasCollidedWithCell(circleCenter, move, WDist.Zero, locomotor);
		}

		void ITick.Tick(Actor self) { Tick(self.World); }

		// Add all actors to their corresponding circles before performing any actions
		void IResolveGroupedOrder.ResolveGroupedOrder(Actor self, Order order)
		{
			foreach (var actor in order.GroupedActors)
				AddMoveOrder(actor, order.Target.CenterPosition, MobileOffGrid.GetGroupedActorsWithMobileOGs(order.GroupedActors.ToList()));
			PlayerCirclesLocked = false;
		}

		WPos GetUnblockedWPos(Actor self, World world, WPos checkPos)
		{
			var checkCCPos = ThetaStarPathSearch.GetNearestCCPos(world, checkPos);
			if (!ThetaStarPathSearch.CcinMap(checkCCPos, world) ||
				ThetaStarPathSearch.IsCellBlocked(self, locomotor, world.Map.CellContaining(checkPos), BlockedByActor.Immovable))
				checkCCPos = ThetaStarPathSearch.GetBestCandidateCCPos(self, world, locomotor, checkPos);
			else
				return checkPos;

			return world.Map.WPosFromCCPos(checkCCPos);
		}

		public void RemovePF(Actor actor) { RemovePF(actor, WPos.Zero); }

		public void RemovePF(Actor actor, WPos targetPos)
		{
			foreach (var thetaPF in ThetaPFsToRun)
				if (thetaPF.self == actor && (targetPos == WPos.Zero || thetaPF.Dest == targetPos))
					thetaPFActions.Add((thetaPF, ThetaPFAction.Remove));
		}

		public void AddMoveOrder(Actor actor, WPos targetPos, List<TraitPair<MobileOffGrid>> sharedMoveActors = null, bool secondThetaRun = false)
		{
			PlayerCirclesLocked = true;
			var world = actor.World;
			// Bypass circle logic if distance to target is small enough
			if (!GreaterThanMinDistanceForCircles(actor, targetPos) || sharedMoveActors == null)
			{
				var rawThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
																 targetPos)
				{
					running = true,
					ActorsSharingPF = new List<Actor>() { actor }
				};

				actor.Trait<MobileOffGrid>().CurrThetaSearch = rawThetaStarSearch;
				AddPF(rawThetaStarSearch);
			}
			else if (secondThetaRun || actor.CurrentActivity is MobileOffGrid.ReturnToCellActivity)
			{
				var rawThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
																 targetPos, 0)
				{
					running = true,
					ActorsSharingPF = new List<Actor>() { actor }
				};

				actor.Trait<MobileOffGrid>().CurrThetaSearch = rawThetaStarSearch;
				AddPF(rawThetaStarSearch);
			}
			else
			{
				// If no circle group exists for the player, generate one
				var playerCircleGroupIndex = new PlayerCircleGroupIndex(actor.Owner, targetPos);
				if (!playerCircleGroups.ContainsKey(playerCircleGroupIndex))
					playerCircleGroups[playerCircleGroupIndex] = new List<ThetaCircle>();
				if (playerCircleGroups[playerCircleGroupIndex].Count == 0)
					playerCircleGroups[playerCircleGroupIndex].Add(new ThetaCircle(MoveOffGrid.GetCenterOfUnits(sharedMoveActors), new WDist(radiusForSharedThetas)));

				// For the found Player Circle group, loop through each circle, attempting to find one that contains the actor
				var circleFound = false;
				for (var circleIndex = 0; circleIndex < playerCircleGroups[playerCircleGroupIndex].Count; circleIndex++)
				{
					var circle = playerCircleGroups[playerCircleGroupIndex].ElementAt(circleIndex);
					// Create a slice in the circle at the position of the actor and return the index of this slice
					var sliceIndex = CircleShape.CalcCircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																		actor.CenterPosition, sliceAngle);
					if (CircleShape.PosIsInsideCircle(circle.CircleCenter, circle.CircleRadius.Length, actor.CenterPosition) &&
						!SliceIsBlockedByCell(actor, circle.CircleCenter, sliceIndex) &&
						!actor.Trait<MobileOffGrid>().HasCollidedWithCell(actor.CenterPosition, circle.CircleCenter, locomotor))
					{
#if DEBUGWITHOVERLAY
						MoveOffGrid.RenderCircle(actor, circle.CircleCenter, circle.CircleRadius, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
						//Slice Line is the standard sliceAngle * index to get the slice
						var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
						MoveOffGrid.RenderLineWithColor(actor, sliceLine[0], sliceLine[1],
														Color.DarkBlue, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif

						var circleSliceIndex = new CircleSliceIndex(playerCircleGroupIndex, circleIndex, sliceIndex);
						if (!ActorOrdersInCircleSlices.ContainsKey(circleSliceIndex))
							ActorOrdersInCircleSlices[circleSliceIndex] = new List<ActorWithOrder>();
						ActorOrdersInCircleSlices[circleSliceIndex].Add(new ActorWithOrder(actor, targetPos));
						circleFound = true;
					}
				}

				// If no valid circle found, create one
				if (!circleFound)
				{
					// Create the circle
					playerCircleGroups[playerCircleGroupIndex].Add(new ThetaCircle(actor.CenterPosition, new WDist(radiusForSharedThetas)));
					var circle = playerCircleGroups[playerCircleGroupIndex].Last();
#if DEBUGWITHOVERLAY
					MoveOffGrid.RenderCircle(actor, circle.CircleCenter, circle.CircleRadius, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif
					var circleIndex = playerCircleGroups[playerCircleGroupIndex].Count - 1;
					var sliceIndex = CircleShape.CalcCircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																		actor.CenterPosition, sliceAngle);
#if DEBUGWITHOVERLAY
					//Slice Line is the standard sliceAngle * index to get the slice
					var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
					MoveOffGrid.RenderLineWithColor(actor, sliceLine[0], sliceLine[1],
													Color.DarkBlue, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif
					var circleSliceIndex = new CircleSliceIndex(playerCircleGroupIndex, circleIndex, sliceIndex);
					if (!ActorOrdersInCircleSlices.ContainsKey(circleSliceIndex))
						ActorOrdersInCircleSlices[circleSliceIndex] = new List<ActorWithOrder>();
					ActorOrdersInCircleSlices[circleSliceIndex].Add(new ActorWithOrder(actor, targetPos));
				}
			}
		}

		public void AddPF(ThetaStarPathSearch thetaPF) { thetaPFActions.Add((thetaPF, ThetaPFAction.Add)); }

		public List<WPos> GetSliceLine(WPos circleCenter, WDist circleRadius, int sliceAngle, int sliceIndex)
		{
			return new List<WPos>()
				{
					circleCenter,
					circleCenter + new WVec(circleRadius, WRot.FromYaw(WAngle.FromDegrees(sliceIndex * sliceAngle)))
				};
		}

		public void GenSharedMoveThetaPFs(World world)
		{
			// Do not generate shared moves until player circles have been unlocked (i.e. all player circle generation is complete)
			if (PlayerCirclesLocked)
				return;

			foreach (var (playerCircleGroupIndex, playerCircleGroup) in playerCircleGroups)
			{
				var player = playerCircleGroupIndex.PlayerOwner;
				foreach (var (circle, circleIndex) in playerCircleGroup.Select((value, index) => (value, index)))
				{
					// Disable this if you want to see the circles persist
					// MoveOffGrid.RemoveCircle(world, circle.CircleCenter, circle.CircleRadius);

					// The below actor is only used for SliceIsBlocked, and is guaranteed to exist since playerCircle
					// only exists
					var blockTestActor = ActorOrdersInCircleSlices.FirstOrDefault().Value.FirstOrDefault().Actor;

					// Generate slice groups (split at each collision)
					if (!circle.SliceGroupsAreSet)
					{
						var firstSliceIndex = 0;
						for (var sliceIndex = 0; sliceIndex < 360 / sliceAngle; sliceIndex++)
						{
							// var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
							// MoveOffGrid.RenderLineWithColor(blockTestActor, sliceLine.ElementAt(0), sliceLine.ElementAt(1),
							//							 	Color.DarkGreen);
							// If the slice is blocked by a cell in either direction, then we create a separate group of actors for this ThetaPF, since
							// they are not going to be able to path through the cell.
							if (SliceIsBlockedByCell(blockTestActor, circle.CircleCenter, sliceIndex) ||
								SliceIsBlockedByCell(blockTestActor, circle.CircleCenter, GetOppositeSlice(sliceIndex)) ||
								sliceIndex == 360 / sliceAngle - 1)
							{
								circle.SliceGroups.Add(new ThetaCircle.SliceGroup(firstSliceIndex, sliceIndex));
								firstSliceIndex = sliceIndex + 1;
							}
						}
					}

					// Slice Groups are sets of one or more slices that are not blocked. Because they are not blocked,
					// they can share the same pathfinder, since we can guarantee that the units are able to follow
					// the same path
					foreach (var sliceGroup in circle.SliceGroups)
					{
						// Get Actors Within Slice Group
						var actorOrdersInSliceGroup = new List<ActorWithOrder>();
						for (var sliceIndex = sliceGroup.StartIndex; sliceIndex <= sliceGroup.EndIndex; sliceIndex++)
						{
							if (ActorOrdersInCircleSlices.ContainsKey(new CircleSliceIndex(playerCircleGroupIndex,
																					   circleIndex, sliceIndex)))
								actorOrdersInSliceGroup = actorOrdersInSliceGroup
															.Union(ActorOrdersInCircleSlices[new CircleSliceIndex(playerCircleGroupIndex,
																										  circleIndex, sliceIndex)]).ToList();
						}

						// Generate Average Theta PF for Slice Group if at least one actor order exists within it
						if (actorOrdersInSliceGroup.Count > 0)
						{
							var firstActorOrder = actorOrdersInSliceGroup[0];
							var avgSourcePosOfGroup = IEnumerableExtensions.Average(actorOrdersInSliceGroup
																					   .Select(ao => ao.Actor.CenterPosition));
							var thetaSourcePos = GetUnblockedWPos(firstActorOrder.Actor, world, avgSourcePosOfGroup);
							var newAvgThetaStarSearch = new ThetaStarPathSearch(firstActorOrder.Actor.World,
																			 firstActorOrder.Actor, thetaSourcePos,
																			 firstActorOrder.TargetPos);

							// Add Averaged Theta PF back to Actors, and to the GroupedThetaPF list
							var individualPFUsed = 0;
							foreach (var (actor, targetPos) in actorOrdersInSliceGroup.Select(ao => (ao.Actor, ao.TargetPos)))
							{
								var actorMobileOffGrid = actor.Trait<MobileOffGrid>();
								if (ThetaStarPathSearch.IsPathObservable(world, actor, locomotor, actor.CenterPosition, thetaSourcePos,
									actorMobileOffGrid.UnitHitShape, true, 1))
								{
									if (newAvgThetaStarSearch.ActorsSharingPF != null)
										newAvgThetaStarSearch.ActorsSharingPF.Add(actor);
									else
										newAvgThetaStarSearch.ActorsSharingPF = new List<Actor> { actor };
									actorMobileOffGrid.CurrThetaSearch = newAvgThetaStarSearch;
								}
								else
								{
									individualPFUsed++;
									var individualAvgThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
										targetPos)
									{
										running = true,
										ActorsSharingPF = new List<Actor> { actor }
									};

									actorMobileOffGrid.CurrThetaSearch = individualAvgThetaStarSearch;
									AddPF(individualAvgThetaStarSearch);
								}
							}

							// If all actors are using an individual pathfinder then we do not use the averaged pathfinder
							if (individualPFUsed < actorOrdersInSliceGroup.Count)
							{
								newAvgThetaStarSearch.running = true;
								AddPF(newAvgThetaStarSearch);
							}
						}
					}
				}
			}

			// IMPORTANT: Need to clear circles once done to avoid re-using pathfinders.
			// Since we have added all PFs to ThetaPFsToRun, the circle logic is no longer necessary.
			playerCircleGroups.Clear();
			ActorOrdersInCircleSlices.Clear();
		}

		void Tick(World world)
		{
			var collDebugOverlay = world.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();

			var rvoObject = rvoBlocks;

			// Call RVO Tick
			if (collDebugOverlay.Enabled && rvoObject == null)
			{
				RVO.Simulator.Instance.Clear();
				//rvoObject = new RVO.Circle();
				//rvoCircle = rvoObject;
				rvoObject = new RVO.Blocks();
				rvoBlocks = rvoObject;
			}
			else if (collDebugOverlay.Enabled)
			{
				collDebugOverlay.ClearCircles();
				collDebugOverlay.ClearLines();

				var agentPositions = rvoObject.getAgentPositions();
				var agentSpawnLocation = new WPos(world.Map.MapSize.X * 1024 / 2, world.Map.MapSize.Y * 1024 / 2, 0);

				var obstacleLines = RVO.Simulator.Instance.getObstacles().Select(o => (o.point_, o.next_.point_));
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

			// We only add or remove Theta PFs during tick cycle to ensure integrity is maintained
			foreach (var (thetaPF, action) in thetaPFActions)
				if (action == ThetaPFAction.Remove)
					ThetaPFsToRun.Remove(thetaPF);
				else if (action == ThetaPFAction.Add)
					ThetaPFsToRun.Add(thetaPF);
			thetaPFActions.Clear();

			// If there are new playerCircles to resolve, we resolve these first to populate ThetaPFsToRun.
			if (playerCircleGroups.Count > 0)
				GenSharedMoveThetaPFs(world);

			for (var i = ThetaPFsToRun.Count; i > 0; i--) // Iterate backwards since we may remove PFs that are no longer expanding
			{
				var thetaPF = ThetaPFsToRun[i - 1];
				foreach (var actor in thetaPF.ActorsSharingPF)
				{
					var actorMobileOG = actor.TraitsImplementing<MobileOffGrid>().FirstOrDefault(Exts.IsTraitEnabled);
					actorMobileOG.Overlay.AddText(actorMobileOG.CenterPosition, i.ToString(), Color.Yellow, (int)PersistConst.Never,
						key: OverlayKeyStrings.PFNumber);
				}

				if (thetaPF.running && !thetaPF.pathFound)
				{
					if (thetaPF.currDelayToRun == 0)
						thetaPF.Expand((int)Fix64.Ceiling((Fix64)maxCurrExpansions / (Fix64)ThetaPFsToRun.Count));
					else if (thetaPF.currDelayToRun > 0)
						thetaPF.currDelayToRun--; // keep subtracting the delay each tick until 0 is reached
				}
				else
				{
					thetaPF.currDelayToRun = -1;
					ThetaPFsToRun.RemoveAt(i - 1); // Remove if no longer expanding
				}
			}
		}
	}
}
