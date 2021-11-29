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
using OpenRA.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.MobileOffGrid;

#pragma warning disable SA1512 // SingleLineCommentsMustNotBeFollowedByBlankLine
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1005 // Single line comments should begin with single space
#pragma warning disable SA1513 // Closing brace should be followed by blank line
#pragma warning disable SA1108 // Block statements should not contain embedded comments

namespace OpenRA.Mods.Common.Activities
{
	public class WVecRotComparer : IComparer<(WVec, int)>
	{
		public int Compare((WVec, int) x, (WVec, int) y)
		{
			var xRotOffset = x.Item2 > 180 ? 360 - x.Item2 : x.Item2;
			var yRotOffset = y.Item2 > 180 ? 360 - y.Item2 : y.Item2;

			if (xRotOffset == yRotOffset)
				return 0;
			else if (xRotOffset > yRotOffset)
				return 1;
			else // <
				return -1;
		}
	}

	public class MoveOffGrid : Activity
	{
		readonly MobileOffGrid mobileOffGrid;
		WVec Delta => currPathTarget - mobileOffGrid.CenterPosition;
		readonly WDist maxRange;
		readonly WDist minRange;
		readonly Color? targetLineColor;
		readonly WDist nearEnough;

		Target target;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;
		bool searchingForNextTarget = false;

		// Options for pathfinder (chosen in the constructor)
		bool usePathFinder = true;
		bool useLocalAvoidance = true;
		WDist localAvoidanceDist;
		/*List<int> localAvoidanceAngleOffsets = new List<int>()
		{
			64, -64, // +-22.5 deg
			128, -128, // +-45 deg
			192, -192, // +-67.5 deg
			256, -256, // +-90 deg
			320, -320, // +-112.5 deg
			384, -384, // +-135 deg
			448, -448, // +-157.5 deg
			512		   // +180 deg (same as -180 deg)
		};*/
		#pragma warning disable SA1137 // Elements should have the same indentation
		List<int> localAvoidanceAngleOffsets = new List<int>()
		{
			  64,  128,  192,
			 -64, -128, -192,
			 256,  320,  384,
			-256, -320, -384,
			 448,  512,
			-448
		};
#pragma warning restore SA1137 // Elements should have the same indentation*/
#pragma warning disable SA1137 // Elements should have the same indentation
		/*List<int> localAvoidanceAngleOffsets = new List<int>()
		{
			  192,  128,   64,
			 -192, -128,  -64,
			  384,  320,  256,
			 -384, -320, -256,
			  512,  448,
		 	 -448
		};*/
#pragma warning restore SA1137 // Elements should have the same indentation
		int currLocalAvoidanceAngleOffset = 0;
		WVec pastMoveVec;

		// LOS Checking interval
		int tickCount = 0;
		int maxTicksBeforeLOScheck = 3;
		readonly Locomotor locomotor;

		List<Actor> actorsSharingMove = new List<Actor>();
		List<WPos> pathRemaining = new List<WPos>();
		bool reachedMaxExpansions = false;
		int thetaIters = 0;
		int maxThetaIters = 3;
		WPos currPathTarget;
		WPos lastPathTarget;
		WPos FinalPathTarget => pathRemaining.Count > 0 ? pathRemaining.Last() : currPathTarget;

		private void RequeueTargetAndSetCurrTo(WPos target)
		{
			pathRemaining.Insert(0, currPathTarget);
			currPathTarget = target;
		}

		public WDist MaxRangeToTarget() => mobileOffGrid.UnitRadius * Exts.ISqrt(actorsSharingMove.Count, Exts.ISqrtRoundMode.Ceiling);

		private WPos PopNextTarget()
		{
			var nextTarget = pathRemaining.FirstOrDefault();
			pathRemaining.RemoveAt(0);
			return nextTarget;
		}

		private bool GetNextTargetOrComplete(bool completeCurrTarg = true)
		{
			if (completeCurrTarg)
			{
				mobileOffGrid.PathComplete.Add(currPathTarget);
				mobileOffGrid.LastCompletedTarget = currPathTarget;
			}
			if (pathRemaining.Count > 0)
			{
				lastPathTarget = currPathTarget; // for validation purposes
				currPathTarget = PopNextTarget();
				mobileOffGrid.CurrPathTarget = currPathTarget;
			}
			else
			{
				return Complete();
			}

			// System.Console.WriteLine("GetNextTargetOrComplete(): More targets available");
			return false;
		}

		public bool Complete()
		{
			// System.Console.WriteLine("GetNextTargetOrComplete(): No more targets");
			mobileOffGrid.PathComplete.Clear();
			mobileOffGrid.PositionBuffer.Clear();
			mobileOffGrid.CurrPathTarget = WPos.Zero;
			return true;
		}

		public static bool PosInRange(WPos pos, WPos origin, WDist range)
		{
			return (pos - origin).HorizontalLengthSquared <= range.LengthSquared;
		}

		public static int Nearest90Deg(int deg)
		{
			var midPoint = 128; // Use this to avoid division to preserve fixed point math
			var degMod = deg < 0 ? deg + 1024 : deg % 1024;
			var angles = new List<int>() { 0, 256, 512, 768, 1024 };

			for (var i = 0; i < angles.Count - 1; i++)
				if (degMod >= angles[i] && degMod < angles[i + 1])
					return degMod < angles[i] + midPoint ? angles[i] : angles[i + 1];

			return -1; // Should never happen
		}

		public static WPos NearestPosInMap(Actor self, WPos pos)
		{
			var map = self.World.Map;
			var topLeftPos = map.TopLeftOfCell(new CPos(0, 0));
			var botRightPos = map.BottomRightOfCell(new CPos(map.MapSize.X - 1, map.MapSize.Y - 1));
			return new WPos(Math.Max(topLeftPos.X, Math.Min(botRightPos.X, pos.X)),
							Math.Max(topLeftPos.Y, Math.Min(botRightPos.Y, pos.Y)), pos.Z);
		}
		public static int MaxLengthInMap(Actor self)
		{
			var map = self.World.Map;
			var topLeftPos = map.TopLeftOfCell(new CPos(0, 0));
			var botRightPos = map.BottomRightOfCell(new CPos(map.MapSize.X - 1, map.MapSize.Y - 1));
			return (botRightPos - topLeftPos).Length;
		}
		public static void RenderLine(Actor self, List<WPos> line)
		{
			var renderLine = new List<WPos>();
			foreach (var pos in line)
				renderLine.Add(pos);

			if (line.Count > 1) // cannot render a path of length 1
				self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine);
		}

		public static void RenderLine(Actor self, WPos pos1, WPos pos2)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine);
		}
		public static void RenderLineWithColor(Actor self, WPos pos1, WPos pos2, Color color)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault()
				.AddLineWithColor(renderLine, color);
		}

		public static void RenderCircle(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius)); }
		public static void RenderCircleWithColor(Actor self, WPos pos, WDist radius, Color color)
		{
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>()
				.FirstEnabledTraitOrDefault().AddCircleWithColor((pos, radius), color);
		}
		public static void RemoveCircle(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().RemoveCircle((pos, radius)); }

		public static void RenderPoint(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos); }
		public static void RenderPoint(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color); }

		/* --- Not Working ---
		 * public static WVec CombineDistAndYawVecWithRotation(WDist dist, WVec yawVec, WRot rotation)
		{ return new WVec(dist, WRot.FromYaw(yawVec.Rotate(rotation).Yaw)); }*/

		public MoveOffGrid(Actor self, in List<Actor> groupedActors, in Target t, WDist nearEnough, WPos? initialTargetPosition = null,
							Color? targetLineColor = null)
			: this(self, groupedActors, t, initialTargetPosition, targetLineColor)
		{
			this.nearEnough = nearEnough;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}
		public MoveOffGrid(Actor self, in Target t, WDist nearEnough, WPos? initialTargetPosition = null,
							Color? targetLineColor = null)
			: this(self, new List<Actor>(), t, nearEnough, initialTargetPosition, targetLineColor) { }

		public MoveOffGrid(Actor self, in List<Actor> groupedActors, in Target t, WPos? initialTargetPosition = null,
							Color? targetLineColor = null)
		{
			mobileOffGrid = self.Trait<MobileOffGrid>();
			localAvoidanceDist = mobileOffGrid.UnitRadius * 2;
			target = t;
			this.targetLineColor = targetLineColor;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();

			actorsSharingMove = groupedActors;
			/*System.Console.WriteLine($"groupedActors: {groupedActors.Count}");
			foreach (var ga in groupedActors)
				System.Console.WriteLine($"groupedActor at {self.World.Map.CellContaining(ga.CenterPosition)}");*/

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				|| target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
				lastVisibleTarget = Target.FromPos(target.CenterPosition);
			else if (initialTargetPosition.HasValue)
				lastVisibleTarget = Target.FromPos(initialTargetPosition.Value);
		}
		public MoveOffGrid(Actor self, in Target t, WPos? initialTargetPosition = null,
							Color? targetLineColor = null)
			: this(self, new List<Actor>(), t, initialTargetPosition, targetLineColor) { }

		// Attack Move Move
		public MoveOffGrid(Actor self, in List<Actor> groupedActors, in Target t, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, groupedActors, t, initialTargetPosition, targetLineColor)
		{
			System.Console.WriteLine($"Target is {target}");
			#if DEBUG || DEBUGWITHOVERLAY
			System.Console.WriteLine("MoveOffGrid created at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
			#endif
			this.maxRange = maxRange;
			this.minRange = minRange;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}
		public MoveOffGrid(Actor self, in Target t, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, new List<Actor>(), t, initialTargetPosition, targetLineColor) { }

		protected override void OnFirstRun(Actor self)
		{
			usePathFinder = true;
			useLocalAvoidance = true;

			if (usePathFinder)
			{
				var thetaStarSearch = new ThetaStarPathSearch(self.World, self);
				using (new Support.PerfTimer("ThetaStar"))
					(pathRemaining, reachedMaxExpansions) = thetaStarSearch.ThetaStarFindPath(mobileOffGrid.CenterPosition,
																							  target.CenterPosition);
				thetaIters++;
			}

			if (!usePathFinder || pathRemaining.Count == 0)
				pathRemaining = new List<WPos>() { target.CenterPosition };

			GetNextTargetOrComplete();
		}
		public void EndingActions()
		{
			mobileOffGrid.SeekVectors.Clear();
			mobileOffGrid.FleeVectors.Clear();
		}

		public WVec AvgOfVectors(List<WVec> wvecList)
		{
			var newWvec = WVec.Zero;
			foreach (var wvec in wvecList)
				newWvec += wvec;
			return newWvec / wvecList.Count;
		}

		public WVec RepulsionVecFunc(WPos selfPos, WPos cellPos)
		{
			var repulsionDelta = cellPos - selfPos;
			var distToMove = Math.Min(repulsionDelta.Length, mobileOffGrid.MovementSpeed);
			return -new WVec(new WDist(distToMove), WRot.FromYaw(repulsionDelta.Yaw));
		}
		public WVec? LocalAvoidanceFunc(WVec? avoidanceVec)
		{
			if (avoidanceVec != null)
			{
				var distToMove = Math.Min(((WVec)avoidanceVec).Length, mobileOffGrid.MovementSpeed);
				return -new WVec(new WDist(distToMove), WRot.FromYaw(((WVec)avoidanceVec).Yaw));
			}
			return null;
		}

		public override bool Tick(Actor self)
		{

			var nearbyActorsSharingMove = GetNearbyActorsSharingMove(self, false);
			var sharedMoveAvgDelta = WVec.Zero;
			if (nearbyActorsSharingMove.Count >= 1)
				sharedMoveAvgDelta = AvgOfVectors(GetNearbyActorsSharingMove(self, false).Where(a => !a.IsDead)
										.Select(a => a.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).FirstOrDefault())
										.Select(a => a.Delta).ToList());
			else sharedMoveAvgDelta = Delta;

			// Refuse to take off if it would land immediately again.
			if (mobileOffGrid.ForceLanding)
				Cancel(self);

			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);
			var isLanded = false; /*dat <= mobileOffGrid.LandAltitude;*/

			// HACK: Prevent paused (for example, EMP'd) mobileOffGrid from taking off.
			// This is necessary until the TODOs in the IsCanceling block below are adressed.
			if (isLanded && mobileOffGrid.IsTraitPaused)
				return false;

			if (IsCanceling)
			{
				// We must return the actor to a sensible height before continuing.
				// If the mobileOffGrid is on the ground we queue TakeOff to manage the influence reservation and takeoff sounds etc.
				// TODO: It would be better to not take off at all, but we lack the plumbing to detect current airborne/landed state.
				// If the mobileOffGrid lands when idle and is idle, we let the default idle handler manage this.
				// TODO: Remove this after fixing all activities to work properly with arbitrary starting altitudes.
				var landWhenIdle = mobileOffGrid.Info.IdleBehavior == IdleBehaviorType.Land;
				var skipHeightAdjustment = landWhenIdle && self.CurrentActivity.IsCanceling && self.CurrentActivity.NextActivity == null;
				EndingActions();
				return true;
			}

			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
			{
				EndingActions();
				return true;
			}

			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;
			var pos = mobileOffGrid.GetPosition();
			//var delta = currPathTarget - pos;

			// For Attack Move, we stop when we are within firing range of the target
			if (minRange != WDist.Zero || maxRange != WDist.Zero)
			{
				var insideMinRange = minRange.Length > 0 && PosInRange(FinalPathTarget, pos, minRange);
				var insideMaxRange = maxRange.Length > 0 && PosInRange(FinalPathTarget, pos, maxRange);
				if (insideMaxRange && !insideMinRange)
				{
					EndingActions();
					return true;
				}
			}

			var moveVec = mobileOffGrid.MovementSpeed * new WVec(new WDist(1024), WRot.FromYaw(Delta.Yaw)) / 1024;

			// Check collision with walls
			var cellsCollidingSet = new List<List<CPos>>();
			cellsCollidingSet.Add(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 3, locomotor));
			cellsCollidingSet.Add(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 2, locomotor));
			cellsCollidingSet.Add(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 1, locomotor));
			for (var i = 0; i < cellsCollidingSet.Count; i++)
			{
				var currCellsColliding = cellsCollidingSet.ElementAt(i);
				if (currCellsColliding.Count > 0)
				{
					var fleeVecs = currCellsColliding.Select(c => self.World.Map.CenterOfCell(c))    // Note: we add scalar prop. to i
												     .Select(wp => RepulsionVecFunc(mobileOffGrid.CenterPosition, wp)).ToList();
					var fleeVecToUse = AvgOfVectors(fleeVecs);
					mobileOffGrid.FleeVectors.Add(new MvVec(fleeVecToUse, 1));
				}
			}

			/*bool UnitHasNotCollided(WVec mv)
			{ return mobileOffGrid.HasNotCollided(self, mv, 4, locomotor, skipLookAheadAmt: 2, attackingUnitsOnly: true); }*/

			bool UnitHasNotCollided(WVec mv)
			{ return mobileOffGrid.GetFirstMoveCollision(self, mv, localAvoidanceDist, locomotor, attackingUnitsOnly: true) == null; }

			if (useLocalAvoidance && searchingForNextTarget && UnitHasNotCollided(moveVec))
			{
				pastMoveVec = new WVec(0, 0, 0);
				currLocalAvoidanceAngleOffset = 0;
				searchingForNextTarget = false;
			}

			//// Only update the SeekVector if either we are not searching for the next target, or we are colliding with an object, otherwise continue
			//if (!(useLocalAvoidance && !UnitHasNotCollided(moveVec)) && !searchingForNextTarget)
			//	mobileOffGrid.SeekVectors = new List<MvVec>() { new MvVec(moveVec) };
			//// Since the pathfinder avoids map obstacles, this must be a unit obstacle, so we employ our local avoidance strategy
			//else if (useLocalAvoidance && !UnitHasNotCollided(moveVec))
			//{
			//	WVec? revisedMoveVec = moveVec;
			//	var i = 0;
			//	do
			//	{
			//		revisedMoveVec = LocalAvoidanceFunc(mobileOffGrid.GetCollisionVector(self, (WVec)revisedMoveVec, 4, locomotor,
			//													skipLookAheadAmt: 2, attackingUnitsOnly: true));
			//		i++;
			//	} while (revisedMoveVec != null && !UnitHasNotCollided((WVec)revisedMoveVec) && i < 5);
			//	if (revisedMoveVec != null && UnitHasNotCollided((WVec)revisedMoveVec))
			//	{
			//		#if DEBUGWITHOVERLAY
			//		System.Console.WriteLine($"move.Yaw {moveVec.Yaw}, revisedMove.Yaw: {((WVec)revisedMoveVec).Yaw}");
			//		/*RenderLine(self, mobileOffGrid.CenterPosition, mobileOffGrid.CenterPosition + revisedMoveVec);
			//		RenderPoint(self, mobileOffGrid.CenterPosition + revisedMoveVec, Color.LightGreen);*/
			//		#endif
			//		//mobileOffGrid.AddToChangedAngleBuffer(localAvoidanceAngleOffset);
			//		//currLocalAvoidanceAngleOffset = localAvoidanceAngleOffset;
			//		pastMoveVec = moveVec;
			//		mobileOffGrid.SeekVectors = new List<MvVec>() { new MvVec((WVec)revisedMoveVec) };
			//		moveVec = (WVec)revisedMoveVec;
			//		searchingForNextTarget = true;
			//	}
			//}

			// Only update the SeekVector if either we are not searching for the next target, or we are colliding with an object, otherwise continue
			if (!(useLocalAvoidance && !UnitHasNotCollided(moveVec)) && !searchingForNextTarget)
				mobileOffGrid.SeekVectors = new List<MvVec>() { new MvVec(moveVec) };
			// Since the pathfinder avoids map obstacles, this must be a unit obstacle, so we employ our local avoidance strategy
			else if (useLocalAvoidance && !UnitHasNotCollided(moveVec))
			{
				var revisedMoveVec = moveVec;
				int localAvoidanceAngleOffset;
				var i = 0;
				do
				{
					localAvoidanceAngleOffset = localAvoidanceAngleOffsets.ElementAt(i);
					// Do not go back in the same direction that you went forward, unless this is the last available local avoidance angle offset
					var newAngle = new WAngle(moveVec.Yaw.Angle + localAvoidanceAngleOffset);
					// System.Console.WriteLine($"existing deg: {moveVec.Yaw.Angle}, new deg: {newAngle.Angle}, offset: {localAvoidanceAngleOffset}");
					var testMoveVec = new WVec(localAvoidanceDist, WRot.FromYaw(newAngle));
					var actualMoveVec = new WVec(new WDist(moveVec.Length), WRot.FromYaw(newAngle));
					var propActorMove = mobileOffGrid.GenFinalWVec(new List<MvVec>() { new MvVec(testMoveVec) },
																	   mobileOffGrid.FleeVectors);
					var propActorPos = self.CenterPosition + propActorMove;
					if (!mobileOffGrid.TraversedCirclesBuffer.Where(c =>
								HitShapes.CircleShape.PosIsInsideCircle(c, mobileOffGrid.UnitRadius.Length, propActorPos)).Any()
						|| i == localAvoidanceAngleOffsets.Count - 1)
						revisedMoveVec = actualMoveVec;
					// RenderLine(self, mobileOffGrid.CenterPosition, mobileOffGrid.CenterPosition + revisedMoveVec);
					i++;
				}
				while (!UnitHasNotCollided(revisedMoveVec) && i < localAvoidanceAngleOffsets.Count);

				if (UnitHasNotCollided(revisedMoveVec))
				{
					#if DEBUGWITHOVERLAY
					System.Console.WriteLine($"move.Yaw {moveVec.Yaw}, revisedMove.Yaw: {revisedMoveVec.Yaw}");
					/*RenderLine(self, mobileOffGrid.CenterPosition, mobileOffGrid.CenterPosition + revisedMoveVec);
					RenderPoint(self, mobileOffGrid.CenterPosition + revisedMoveVec, Color.LightGreen);*/
					#endif
					mobileOffGrid.AddToTraversedCirclesBuffer(self.CenterPosition + revisedMoveVec);
					RenderCircleWithColor(self, mobileOffGrid.CenterPosition + revisedMoveVec, mobileOffGrid.UnitRadius, Color.LightGreen);
					currLocalAvoidanceAngleOffset = localAvoidanceAngleOffset;
					pastMoveVec = moveVec;
					mobileOffGrid.SeekVectors = new List<MvVec>() { new MvVec(revisedMoveVec) };
					moveVec = revisedMoveVec;
					searchingForNextTarget = true;
				}
			}
			else if (useLocalAvoidance && currLocalAvoidanceAngleOffset != 0 && searchingForNextTarget && UnitHasNotCollided(pastMoveVec))
			{
				pastMoveVec = new WVec(0, 0, 0);
				currLocalAvoidanceAngleOffset = 0;
				searchingForNextTarget = false;
			}

			mobileOffGrid.SeekVectors = new List<MvVec>() { new MvVec(moveVec) };
			var move = mobileOffGrid.GenFinalWVec();

			if (mobileOffGrid.PositionBuffer.Count >= 100)
			{
				var lengthMoved = (mobileOffGrid.PositionBuffer.Last() - mobileOffGrid.PositionBuffer.ElementAt(0)).Length;
				var deltaFirst = currPathTarget - mobileOffGrid.PositionBuffer.ElementAt(0);
				var deltaLast = currPathTarget - mobileOffGrid.PositionBuffer.Last();

				if (lengthMoved < 512 && !searchingForNextTarget)
				{
					// Create new path to the next path, then join it to the remainder of the existing path
					// Make sure we are not re-running this if we received the same result last time
					// Also do not re-run if max expansions were reached last time
					if (currPathTarget != lastPathTarget)
					{
						if (!reachedMaxExpansions && thetaIters < maxThetaIters)
						{
							searchingForNextTarget = true;
							var thetaStarSearch = new ThetaStarPathSearch(self.World, self);
							List<WPos> thetaToNextTarg;
							(thetaToNextTarg, reachedMaxExpansions) = thetaStarSearch.ThetaStarFindPath(mobileOffGrid.CenterPosition,
																											currPathTarget);
							thetaIters++;
							if (thetaToNextTarg.Count > 1)
							{
								pathRemaining = thetaToNextTarg.Concat(pathRemaining).ToList();
								GetNextTargetOrComplete(false);
								EndingActions();
								mobileOffGrid.PositionBuffer.Clear();
							}
							searchingForNextTarget = false;
						}
						else if (mobileOffGrid.PositionBuffer.Count >= 180) // 3 seconds
						{
							EndingActions();
							return Complete();
						}
					}
				}
				else if (nearbyActorsSharingMove.Count <= 1 && deltaLast.LengthSquared > deltaFirst.LengthSquared)
				{
					EndingActions();
					mobileOffGrid.PositionBuffer.Clear();
				}
			}

			var selfHasReachedGoal = Delta.HorizontalLengthSquared < move.HorizontalLengthSquared;
			var completedTargsOfNearbyActors = CompletedTargetsOfActors(GetNearbyActorsSharingMove(self));
			tickCount = tickCount >= maxTicksBeforeLOScheck ? tickCount = 0 : tickCount + 1;
			var hasReachedGoal = selfHasReachedGoal ||
								 (tickCount == 0 && completedTargsOfNearbyActors.Contains(currPathTarget) &&
								  ((pathRemaining.Count == 0 && Delta.Length < (move.Length + MaxRangeToTarget().Length))
								  || ThetaStarPathSearch.IsPathObservable(self.World, self, locomotor,
																	mobileOffGrid.CenterPosition, pathRemaining.FirstOrDefault())));
								  //TargetInLOS(self, move, locomotor, pathRemaining.FirstOrDefault()));

			if (hasReachedGoal)
			{
				#if DEBUG || DEBUGWITHOVERLAY
				// System.Console.WriteLine($"if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared) = {Delta.HorizontalLengthSquared < move.HorizontalLengthSquared}");
				#endif

				if (Delta.HorizontalLengthSquared != 0 && selfHasReachedGoal)
				{
					// Ensure we don't include a non-zero vertical component here that would move us away from CruiseAltitude
					var deltaMove = new WVec(Delta.X, Delta.Y, 0);
					mobileOffGrid.SeekVectors.Clear();
					mobileOffGrid.SetDesiredFacingToFacing();
					mobileOffGrid.SetDesiredAltitude(dat);
					mobileOffGrid.SetDesiredMove(deltaMove);
					mobileOffGrid.SetIgnoreZVec(true);
				}

				EndingActions();
				searchingForNextTarget = false;
				return GetNextTargetOrComplete();
			}

			mobileOffGrid.AddToPositionBuffer(self.CenterPosition);
			return false;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return target;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor.HasValue)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}

		public static WVec MoveStep(int speed, WAngle facing)
		{
			var dir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(facing));
			return speed * dir / 1024;
		}

		public List<Actor> GetNearbyActorsSharingMove(Actor self, bool excludeSelf = true)
		{
			/*RenderCircle(self, mobileOffGrid.CenterPosition, nearbyActorRange);*/
			var nearbyActors = self.World.FindActorsInCircle(mobileOffGrid.CenterPosition, MaxRangeToTarget());
			var actorsSharingMoveNearMe = new List<Actor>();
			foreach (var actor in nearbyActors)
				if (!actor.IsDead && (actor != self || !excludeSelf) && actor.Owner == self.Owner && actorsSharingMove.Contains(actor) &&
					!(actor.CurrentActivity is MobileOffGrid.ReturnToCellActivity))
					actorsSharingMoveNearMe.Add(actor); // No need to check if MobileOffGrid exists since all actors in actorsSharingMove
														// are moving
			return actorsSharingMoveNearMe;
		}

		public List<WPos> CompletedTargetsOfActors(List<Actor> actorList)
		{
			var actorListMobileOGs = actorList.Where(a => !a.IsDead)
										.Select(a => a.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).FirstOrDefault());
			var completedTargs = actorListMobileOGs.Select(m => m.PathComplete).SelectMany(p => p).Distinct().ToList();
			completedTargs = completedTargs.Union(actorListMobileOGs.Select(m => m.LastCompletedTarget)).ToList();
			return completedTargs;
		}

		public static int CalculateTurnRadius(int speed, WAngle turnSpeed)
		{
			// turnSpeed -> divide into 256 to get the number of ticks per complete rotation
			// speed -> multiply to get distance travelled per rotation (circumference)
			// 180 -> divide by 2*pi to get the turn radius: 180==1024/(2*pi), with some extra leeway
			return turnSpeed.Angle > 0 ? 180 * speed / turnSpeed.Angle : 0;
		}
	}
}
