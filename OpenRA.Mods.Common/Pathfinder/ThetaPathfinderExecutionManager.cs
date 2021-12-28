using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;

#pragma warning disable SA1108 // Block statements should not contain embedded comments

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Manages the queuing and prioritisation of Theta Pathfinder calculations, to ensure the computer is not overloded.")]

	public class ThetaPathfinderExecutionManagerInfo : TraitInfo<ThetaPathfinderExecutionManager> { }
	public class ThetaPathfinderExecutionManager : ITick, IResolveGroupedOrder
	{
		public class CircleOfActors
		{
			public struct SliceGroup
			{
				public int First;
				public int Last;
				public SliceGroup(int first, int last)
				{
					First = first;
					Last = last;
				}
			}

			public WPos CircleCenter;
			public WDist CircleRadius;
			public List<SliceGroup> SliceGroups = new List<SliceGroup>();
			public bool SliceGroupsSet;

			public CircleOfActors(WPos circleCenter, WDist circleRadius, bool sliceGroupsSet = false)
			{
				CircleCenter = circleCenter;
				CircleRadius = circleRadius;
				SliceGroupsSet = sliceGroupsSet;
			}
		}

		public struct ActorPFIndex
		{
			public PlayerCircleIndex PlayerCI;
			public int CircleIndex;
			public int CircleSliceIndex;

			public ActorPFIndex(PlayerCircleIndex playerCircleIndex, int circleIndex, int circleSliceIndex)
			{
				PlayerCI = playerCircleIndex;
				CircleIndex = circleIndex;
				CircleSliceIndex = circleSliceIndex;
			}
		}
		public struct PlayerCircleIndex
		{
			public Player PlayerOwner;
			public WPos PFTarget;

			public PlayerCircleIndex(Player playerOwner, WPos pfTarget)
			{
				PlayerOwner = playerOwner;
				PFTarget = pfTarget;
			}
		}

		public struct ActorWithOrder
		{
			public Actor Actor;
			public WPos TargetPos;
			public List<Actor> SharedMoveActors;

			public ActorWithOrder(Actor actor, WPos targetPos, List<Actor> sharedMoveActors)
			{
				Actor = actor;
				TargetPos = targetPos;
				SharedMoveActors = sharedMoveActors;
			}
		}

		int maxCurrExpansions = 200;
		int radiusForSharedThetas = 1024 * 10;
		int minDistanceForCircles = 1024 * 28;
		int sliceAngle = 10;
		int maxCircleSlices = 36;
		Dictionary<PlayerCircleIndex, List<CircleOfActors>> playerCircles = new Dictionary<PlayerCircleIndex, List<CircleOfActors>>();
		public Dictionary<ActorPFIndex, List<ActorWithOrder>> ActorOrdersInCircleSlices = new Dictionary<ActorPFIndex,
																									  List<ActorWithOrder>>();
		public List<ThetaStarPathSearch> ThetaPFsToRun = new List<ThetaStarPathSearch>();

		public bool GreaterThanMinDistanceForCircles(Actor actor, WPos targetPos)
		{ return (targetPos - actor.CenterPosition).LengthSquared > minDistanceForCircles * minDistanceForCircles; }

		public int GetOppositeSlice(int slice) { return (int)(((Fix64)slice + (Fix64)maxCircleSlices / (Fix64)2) % (Fix64)maxCircleSlices); }

		public bool SliceIsBlocked(Actor self, WPos circleCenter, int slice)
		{
			var sliceAbsoluteAngle = (int)(sliceAngle * (slice - 0.5)); // We subtract 0.5 as we want the middle of the slice
			var lookAheadDist = new WDist(radiusForSharedThetas);
			var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			var move = new WVec(lookAheadDist, WRot.FromYaw(WAngle.FromDegrees(sliceAbsoluteAngle)));
			if (self.Trait<MobileOffGrid>().GetFirstCollision(circleCenter, move, lookAheadDist, locomotor,
														      includeCellCollision: true, includeActorCollision: false) != null)
				return true;
			return false;
		}

		public ThetaPathfinderExecutionManager() { }

		void ITick.Tick(Actor self) { Tick(self.World); }

		// Add all actors to their corresponding circles before performing any actions
		void IResolveGroupedOrder.ResolveGroupedOrder(Actor self, Order order)
		{
			foreach (var actor in order.GroupedActors)
				AddMoveOrder(actor, order.Target.CenterPosition, order.GroupedActors.ToList());
			GenSharedMoveThetaPFs(self.World);
		}

		public void AddMoveOrder(Actor actor, WPos targetPos, List<Actor> sharedMoveActors, bool secondThetaRun = false)
		{
			// Bypass circle logic if distance to target is small enough
			if (!GreaterThanMinDistanceForCircles(actor, targetPos) || secondThetaRun)
			{
				var rawThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
																 targetPos, sharedMoveActors);
				rawThetaStarSearch.running = true;
				actor.Trait<MobileOffGrid>().thetaStarSearch = rawThetaStarSearch;
				AddPF(rawThetaStarSearch);
			}
			else
			{
				// If no circle exists, generate one
				var playerCircleIndex = new PlayerCircleIndex(actor.Owner, targetPos);
				if (!playerCircles.ContainsKey(playerCircleIndex))
					playerCircles[playerCircleIndex] = new List<CircleOfActors>();
				if (playerCircles[playerCircleIndex].Count == 0)
					playerCircles[playerCircleIndex].Add(new CircleOfActors(actor.CenterPosition, new WDist(radiusForSharedThetas)));

				// Loop through each circle, attempting to find one that contains the actor
				var circleFound = false;
				for (var circleIndex = 0; circleIndex < playerCircles[playerCircleIndex].Count; circleIndex++)
				{
					var circle = playerCircles[playerCircleIndex].ElementAt(circleIndex);
					if (CircleShape.PosIsInsideCircle(circle.CircleCenter, circle.CircleRadius.Length, actor.CenterPosition))
					{
						MoveOffGrid.RenderCircle(actor, circle.CircleCenter, circle.CircleRadius);
						var circleSliceIndex = CircleShape.CircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																			actor.CenterPosition, sliceAngle);
						var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, circleSliceIndex);
						MoveOffGrid.RenderLineWithColor(actor, sliceLine.ElementAt(0), sliceLine.ElementAt(1),
														Color.DarkBlue);
						var actorPFindex = new ActorPFIndex(playerCircleIndex, circleIndex, circleSliceIndex);
						if (!ActorOrdersInCircleSlices.ContainsKey(actorPFindex))
							ActorOrdersInCircleSlices[actorPFindex] = new List<ActorWithOrder>();
						ActorOrdersInCircleSlices[actorPFindex].Add(new ActorWithOrder(actor, targetPos, sharedMoveActors));
						circleFound = true;
					}
				}

				// If no valid circle found, create one
				if (!circleFound)
				{
					// Create the circle
					playerCircles[playerCircleIndex].Add(new CircleOfActors(actor.CenterPosition, new WDist(radiusForSharedThetas)));
					var circle = playerCircles[playerCircleIndex].Last();
					var circleIndex = playerCircles[playerCircleIndex].Count - 1;
					var circleSliceIndex = CircleShape.CircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																		actor.CenterPosition, sliceAngle);
					var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, circleSliceIndex);
					MoveOffGrid.RenderLineWithColor(actor, sliceLine.ElementAt(0), sliceLine.ElementAt(1),
													Color.DarkBlue);
					var actorPFindex = new ActorPFIndex(playerCircleIndex, circleIndex, circleSliceIndex);
					ActorOrdersInCircleSlices[actorPFindex].Add(new ActorWithOrder(actor, targetPos, sharedMoveActors));
				}
			}
		}

		public void AddPF(ThetaStarPathSearch thetaPF) { ThetaPFsToRun.Add(thetaPF); }

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
			/*// Where target distance exceeds the min distance for circles, add all actor PFs to their own circle and slice
			sharedMoveActors.Where(a => GreaterThanMinDistanceForCircles(a, order)).ToList()
							.ForEach(a => AddMoveOrder(a, order));

			// Where target distance is below min distance for circles, add all actor PFs directly
			sharedMoveActors.Where(a => !GreaterThanMinDistanceForCircles(a, order)).ToList()
							.ForEach(a => ThetaPFsToRun.Add(new ThetaStarPathSearch(world, a, a.CenterPosition,
																					order.Target.CenterPosition,
																					order.GroupedActors.ToList())));*/

			// Add slice groups to circles with no slice groups
			/*foreach (var (_, playerCircle) in playerCircles)
				foreach (var circle in playerCircle.Where(pc => pc.SliceGroupsSet == false))
				{
					var sliceGroupFirst = 0;
					for (var sliceIndex = 0; sliceIndex < 360 / sliceAngle; sliceIndex++)
					{
						if (ActorOrdersInCircleSlices.Count > 0)
						{
							var (_, aoList) = ActorOrdersInCircleSlices.First();
							var actor = aoList.First().Actor;
							if (SliceIsBlocked(actor, circle.CircleCenter, sliceIndex) ||
								SliceIsBlocked(actor, circle.CircleCenter, GetOppositeSlice(sliceIndex)))
							{
								circle.SliceGroups.Add(new CircleOfActors.SliceGroup(sliceGroupFirst, sliceIndex));
								sliceGroupFirst = sliceIndex + 1;
							}
						}
					}
				}*/

			foreach (var (playerCircleIndex, circleList) in playerCircles)
			{
				var player = playerCircleIndex.PlayerOwner;
				foreach (var (circle, circleIndex) in circleList.Select((value, index) => (value, index)))
				{
					// Disable this if you want to see the circles persist
					// MoveOffGrid.RemoveCircle(world, circle.CircleCenter, circle.CircleRadius);

					// The below actor is only used for SliceIsBlocked, and is guaranteed to exist since playerCircle
					// only exists
					var blockTestActor = ActorOrdersInCircleSlices.FirstOrDefault().Value.FirstOrDefault().Actor;

					// Generate slice groups (split at each collision)
					if (!circle.SliceGroupsSet)
					{
						var sliceGroupFirst = 0;
						for (var sliceIndex = 0; sliceIndex < 360 / sliceAngle; sliceIndex++)
						{
							// var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
							// MoveOffGrid.RenderLineWithColor(blockTestActor, sliceLine.ElementAt(0), sliceLine.ElementAt(1),
							//							 	Color.DarkGreen);
							if (SliceIsBlocked(blockTestActor, circle.CircleCenter, sliceIndex) ||
								SliceIsBlocked(blockTestActor, circle.CircleCenter, GetOppositeSlice(sliceIndex)) ||
								sliceIndex == 360 / sliceAngle - 1)
							{
								circle.SliceGroups.Add(new CircleOfActors.SliceGroup(sliceGroupFirst, sliceIndex));
								sliceGroupFirst = sliceIndex + 1;
							}
						}
					}

					foreach (var sliceGroup in circle.SliceGroups)
					{
						// Get Actors Within Slice Group
						var actorOrdersInSliceGroup = new List<ActorWithOrder>();
						for (var sliceIndex = sliceGroup.First; sliceIndex <= sliceGroup.Last; sliceIndex++)
						{
							if (ActorOrdersInCircleSlices.ContainsKey(new ActorPFIndex(playerCircleIndex,
																					   circleIndex, sliceIndex)))
								actorOrdersInSliceGroup = actorOrdersInSliceGroup
															.Union(ActorOrdersInCircleSlices[new ActorPFIndex(playerCircleIndex,
																										  circleIndex, sliceIndex)]).ToList();
						}

						// Generate Average Theta PF for Slice Group if at least one actor order exists within it
						if (actorOrdersInSliceGroup.Count > 0)
						{
							var firstActorOrder = actorOrdersInSliceGroup.First();
							var avgSourcePosOfGroup = IEnumerableExtensions.Average(actorOrdersInSliceGroup
																					   .Select(ao => ao.Actor.CenterPosition));
							var newAvgThetaStarSearch = new ThetaStarPathSearch(firstActorOrder.Actor.World,
																			 firstActorOrder.Actor, avgSourcePosOfGroup,
																			 firstActorOrder.TargetPos,
																			 firstActorOrder.SharedMoveActors.ToList());

							// Add Averaged Theta PF back to Actors, and to the GroupedThetaPF list
							foreach (var actor in actorOrdersInSliceGroup.Select(ao => ao.Actor))
							{
								actor.Trait<MobileOffGrid>().thetaStarSearch = newAvgThetaStarSearch;
							}
							newAvgThetaStarSearch.running = true;
							AddPF(newAvgThetaStarSearch);
						}
					}
				}
			}

			// IMPORTANT: Need to clear circles once done to avoid re-using pathfinders.
			// Since we have added all PFs to ThetaPFsToRun, the circle logic is no longer necessary.
			playerCircles.Clear();
			ActorOrdersInCircleSlices.Clear();
		}

		private void Tick(World world)
		{
			// If there are new playerCircles to resolve, we resolve these first to populate ThetaPFsToRun.
			if (playerCircles.Count > 0)
				GenSharedMoveThetaPFs(world);

			for (var i = ThetaPFsToRun.Count; i > 0; i--) // Iterate backwards since we may remove PFs that are no longer expanding
			{
				var thetaPF = ThetaPFsToRun.ElementAt(i - 1);
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
