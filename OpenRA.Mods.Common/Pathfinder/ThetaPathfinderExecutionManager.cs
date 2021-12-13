using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.HitShapes;
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

		int maxCurrExpansions = 200;
		int radiusForSharedThetas = 1024 * 7;
		int sliceAngle = 10;
		int maxCircleSlices = 36;
		Dictionary<PlayerCircleIndex, List<CircleOfActors>> playerCircles = new Dictionary<PlayerCircleIndex, List<CircleOfActors>>();
		public Dictionary<ActorPFIndex, List<ThetaStarPathSearch>> ThetaPFsInCircleSlices = new Dictionary<ActorPFIndex,
																									  List<ThetaStarPathSearch>>();
		public Dictionary<int, List<ThetaStarPathSearch>> GroupedThetaPFs = new Dictionary<int, List<ThetaStarPathSearch>>();

		public int GetOppositeSlice(int slice) { return (int)(((Fix64)slice + (Fix64)maxCircleSlices / (Fix64)2) % (Fix64)maxCircleSlices); }

		public bool SliceIsBlocked(Actor self, WPos circleCenter, int slice)
		{
			var sliceAbsoluteAngle = (int)(sliceAngle * (slice - 0.5)); // We subtract 0.5 as we want the middle of the slice
			var lookAheadDist = new WDist(radiusForSharedThetas);
			var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			var move = new WVec(lookAheadDist, WRot.FromYaw(WAngle.FromDegrees(sliceAbsoluteAngle)));
			if (self.Trait<MobileOffGrid>().GetFirstCollision(circleCenter, move, lookAheadDist, locomotor,
														      attackingUnitsOnly: true, includeCellCollision: true) != null)
				return true;
			return false;
		}

		public ThetaPathfinderExecutionManager() { }

		void ITick.Tick(Actor self) { Tick(self.World); }

		public void AddPF(Actor actor, ThetaStarPathSearch thetaPF)
		{
			// If no circle exists, generate one
			var playerCircleIndex = new PlayerCircleIndex(actor.Owner, thetaPF.Dest);
			if (playerCircles[playerCircleIndex].Count == 0)
				playerCircles[playerCircleIndex].Add(new CircleOfActors(actor.CenterPosition, new WDist(radiusForSharedThetas)));

			// Loop through each circle, attempting to find one that contains the actor
			var circleFound = false;
			for (var circleIndex = 0; circleIndex < playerCircles[playerCircleIndex].Count; circleIndex++)
			{
				var circle = playerCircles[playerCircleIndex].ElementAt(circleIndex);
				if (CircleShape.PosIsInsideCircle(circle.CircleCenter, circle.CircleRadius.Length, actor.CenterPosition))
				{
					var circleSliceIndex = CircleShape.CircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																	    actor.CenterPosition, sliceAngle);
					var actorPFindex = new ActorPFIndex(playerCircleIndex, circleIndex, circleSliceIndex);
					ThetaPFsInCircleSlices[actorPFindex].Add(thetaPF);
					circleFound = true;
				}
			}

			// If no valid circle found, create one
			if (!circleFound)
			{
				playerCircles[playerCircleIndex].Add(new CircleOfActors(actor.CenterPosition, new WDist(radiusForSharedThetas)));
				var circle = playerCircles[playerCircleIndex].Last();
				var circleIndex = playerCircles[playerCircleIndex].Count - 1;
				var circleSliceIndex = CircleShape.CircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																	actor.CenterPosition, sliceAngle);
				var actorPFindex = new ActorPFIndex(playerCircleIndex, circleIndex, circleSliceIndex);
				ThetaPFsInCircleSlices[actorPFindex].Add(thetaPF);
			}

			// Update circles with no slice groups
			foreach (var circle in playerCircles[playerCircleIndex].Where(pc => pc.SliceGroupsSet == false))
			{
				var sliceGroupFirst = 0;
				for (var sliceIndex = 0; sliceIndex < 360 / sliceAngle; sliceIndex++)
				{
					if (SliceIsBlocked(actor, circle.CircleCenter, sliceIndex) ||
						SliceIsBlocked(actor, circle.CircleCenter, GetOppositeSlice(sliceIndex)))
					{
						circle.SliceGroups.Add(new CircleOfActors.SliceGroup(sliceGroupFirst, sliceIndex));
						sliceGroupFirst = sliceIndex + 1;
					}
				}
			}
		}

		private void Tick(World world)
		{
			foreach (var (playerCircleIndex, circleList) in playerCircles)
			{
				var player = playerCircleIndex.PlayerOwner;
				foreach (var (circle, circleIndex) in circleList.Select((value, index) => (value, index)))
					foreach (var sliceGroup in circle.SliceGroups)
						for (var sliceIndex = sliceGroup.First; sliceIndex <= sliceGroup.Last; sliceIndex++)
						{
							var thetaPFsInCircleSlice = ThetaPFsInCircleSlices[new ActorPFIndex(playerCircleIndex, circleIndex, sliceIndex)];
							var avgSourcePosOfGroup = IEnumerableExtensions.Average(thetaPFsInCircleSlice.Select(thetaPF => thetaPF.Source));
							var firstThetaPF = thetaPFsInCircleSlice.First();
							var newThetaStarSearch = new ThetaStarPathSearch(world, firstThetaPF.self,
																avgSourcePosOfGroup, firstThetaPF.Dest,
																firstThetaPF.sharedMoveActors);

							foreach (var thetaPF thetaPFsInCircleSlice)
								if (thetaPF.pathFound == false)
									thetaPF.Expand((int)Fix64.Ceiling((Fix64)maxCurrExpansions / (Fix64)ThetaPFsInCircleSlices.Count));
						}
			}
		}
	}
}
