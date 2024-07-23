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
using static OpenRA.Mods.Common.Activities.MoveOffGrid;
using static OpenRA.Mods.Common.Pathfinder.ThetaStarPathSearch;
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
		readonly WDist localAvoidanceDist;

		MoveType moveType = MoveType.Undefined;

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
		Color pickedColor;

		ThetaPathfinderExecutionManager thetaPFexecManager;
		bool secondThetaRun = false;
		List<Actor> actorsSharingMove = new List<Actor>();
		bool pathFound = false;
		List<WPos> pathRemaining = new List<WPos>();
		bool reachedMaxExpansions = false;
		int thetaIters = 0;
		int maxThetaIters = 3;
		WPos currPathTarget;
		WPos lastPathTarget;
		bool isBlocked = false;
		bool firstMove = false;

		WPos FinalPathTarget => pathRemaining.Count > 0 ? pathRemaining.Last() : currPathTarget;

		public enum MoveType { Undefined, Formation, NonFormation }

		private void RequeueTargetAndSetCurrTo(WPos target)
		{
			pathRemaining.Insert(0, currPathTarget);
			currPathTarget = target;
		}

		public WDist MaxRangeToTarget()
		{
			var actorMobileOgs = actorsSharingMove.Where(a => !a.IsDead).Select(a => a.TraitsImplementing<MobileOffGrid>().FirstEnabledTraitOrDefault())
												  .Where(m => m != null);

			return actorMobileOgs.Any() ? new WDist(actorMobileOgs.Select(m => m.UnitRadius.Length).Sum()
														/ Math.Max(actorsSharingMove.Count, 1)
														* Exts.ISqrt(actorsSharingMove.Count, Exts.ISqrtRoundMode.Ceiling)) : WDist.Zero;
		}

		private WPos PopNextTarget()
		{
			var nextTarget = pathRemaining.FirstOrDefault();
			pathRemaining.RemoveAt(0);
			return nextTarget;
		}

		bool GetNextTargetOrComplete(Actor self, bool completeCurrTarg = true)
		{
			if (completeCurrTarg)
			{
				mobileOffGrid.PathComplete.Add(currPathTarget);
				mobileOffGrid.LastCompletedTarget = currPathTarget;
			}
			if (pathRemaining.Count > 0)
			{
				lastPathTarget = currPathTarget; // for validation purposes
				if (firstMove)
				{
					currPathTarget = AdjustedCurrPathTarget(self, PopNextTarget());
					firstMove = false;
				}
				else
					currPathTarget = PopNextTarget();
				Console.WriteLine($"currPathTarget: {currPathTarget}");
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
			renderLine.AddRange(line);

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
		public static void RenderLineCollDebug(Actor self, WPos pos1, WPos pos2)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine);
		}
		public static void RenderLineWithColorCollDebug(Actor self, WPos pos1, WPos pos2, Color color)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault()
				.AddLineWithColor(renderLine, color);
		}

		public static void RenderCircle(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius)); }

		public static void RenderCircleCollDebug(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius)); }

		public static void RenderCircleWithColor(Actor self, WPos pos, WDist radius, Color color)
		{
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>()
				.FirstEnabledTraitOrDefault().AddCircleWithColor((pos, radius), color);
		}
		public static void RemoveCircle(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().RemoveCircle((pos, radius)); }
		public static void RemoveCircle(World world, WPos pos, WDist radius)
		{ world.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().RemoveCircle((pos, radius)); }

		public static void RenderPoint(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos); }
		public static void RenderPoint(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color); }
		public static void RenderPointCollDebug(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos); }
		public static void RenderPointCollDebug(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color); }

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
			firstMove = true;
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

			thetaPFexecManager = self.World.WorldActor.TraitsImplementing<ThetaPathfinderExecutionManager>().FirstOrDefault();

			if (usePathFinder)
			{
				// mobileOffGrid.thetaStarSearch = new ThetaStarPathSearch(self.World, self, mobileOffGrid.CenterPosition, target.CenterPosition
				//										  , actorsSharingMove);
				// IMPORTANT: This AddMoveOrder call relies on _all_ MoveOffGrid Activities
				// being called _before_ any traits are called. This is because all of the actors
				// must be processed collectively in order for the grouped pathfinding to work.
				// Otherwise pathfinding will be inefficient and slow.
				thetaPFexecManager.AddMoveOrder(self, target.CenterPosition, actorsSharingMove);
				thetaIters++;
			}

			if (!usePathFinder)
				pathRemaining = new List<WPos>() { target.CenterPosition };
		}

		public void EndingActions()
		{
			mobileOffGrid.SeekVectors.Clear();
			mobileOffGrid.FleeVectors.Clear();
		}

		public WVec AvgOfVectors(List<WVec> wvecList)
		{
			if (wvecList.Count == 0)
				return WVec.Zero;

			var newWvec = WVec.Zero;
			foreach (var wvec in wvecList)
				newWvec += wvec;
			return newWvec / wvecList.Count;
		}

		public WVec GenMovementSpeedVec()
		{ return mobileOffGrid.MovementSpeed * new WVec(new WDist(1024), WRot.FromYaw(Delta.Yaw)) / 1024; }

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

		public WPos PadCCifCC(Actor self, PathPos pp)
		{ return pp.ccPos != CCPos.Zero ? PadCC(self.World, self, locomotor, mobileOffGrid, pp.ccPos) : pp.wPos; }

		public List<WPos> GetThetaPathAndConvert(Actor self)
		{ return mobileOffGrid.thetaStarSearch.path.Select(pp => PadCCifCC(self, pp)).ToList(); }

		public WPos GetCenterOfUnits(List<Actor> actorsSharingMove)
		{
			return actorsSharingMove.Where(a => !a.IsDead).Select(a => a.TraitsImplementing<MobileOffGrid>().FirstOrDefault().CenterPosition).Average();
		}

		public WVec GetDistToCenterOfUnits(Actor self, List<Actor> actorsSharingMove, bool requireLineOfSight = true)
		{
			var centerOfActorsSharingMove = GetCenterOfUnits(actorsSharingMove);
			if (!requireLineOfSight ||
				IsPathObservable(self.World, self, mobileOffGrid.Locomotor, centerOfActorsSharingMove, mobileOffGrid.CenterPosition))
			{
				Console.WriteLine($"GetCenterOfUnits(actorsSharingMove): {GetCenterOfUnits(actorsSharingMove)}, mobileOffGrid.CenterPosition: {mobileOffGrid.CenterPosition}");
				return GetCenterOfUnits(actorsSharingMove) - mobileOffGrid.CenterPosition;
			}
			else
				return WVec.Zero;
		}

		public struct ActorWithMoveProps
		{
			public MobileOffGrid MobOffGrid;
			public Actor Act;
			public WVec OffsetToCenterOfActors;
			public bool IsOffsetCloseEnough;
			public WPos OffsetTarget;
			public bool IsOffsetTargetObservable;
		}

		public WPos AdjustedCurrPathTarget(Actor self, WPos currPathTarget)
		{
			var centerOfActorsSharingMove = GetCenterOfUnits(actorsSharingMove);

			WPos FormationMove()
			{
				moveType = MoveType.Formation;
				Console.WriteLine("Using actorsSharingMove move");
				//RenderLineCollDebug(self, GetCenterOfUnits(actorsSharingMove), mobileOffGrid.CenterPosition);
				//RenderLineWithColorCollDebug(self, currPathTarget, currPathTarget - (centerOfActorsSharingMove - mobileOffGrid.CenterPosition),
				//	self.TraitsImplementing<MobileOffGrid>().FirstOrDefault().debugColor);
				return currPathTarget - (centerOfActorsSharingMove - mobileOffGrid.CenterPosition);
			}

			WPos NonFormationMove()
			{
				moveType = MoveType.NonFormation;
				return currPathTarget;
			}

			var actorsSharingMoveActivitiesMoveType = actorsSharingMove.Where(a => a.CurrentActivity is MoveOffGrid && a.CurrentActivity.State == ActivityState.Active)
																		.Select(a => a.CurrentActivity.ActivitiesImplementing<MoveOffGrid>(false).FirstOrDefault().moveType);
			if (actorsSharingMoveActivitiesMoveType.Any(t => t == MoveType.NonFormation))
				return NonFormationMove();
			// Because we check all actorsSharingMove, any shared actor that has a Formation move type is confirmation that we can move in formation
			else if (actorsSharingMoveActivitiesMoveType.Any(t => t == MoveType.Formation))
				return FormationMove();

			var actorsSharingMoveWithProps = actorsSharingMove.Select(a => new ActorWithMoveProps
			{
				MobOffGrid = a.TraitsImplementing<MobileOffGrid>().FirstOrDefault(),
				Act = a
			}).Select(am => { am.OffsetToCenterOfActors = centerOfActorsSharingMove - am.MobOffGrid.CenterPosition; return am; })
			.Select(am2 =>
			{
				am2.OffsetTarget = currPathTarget - am2.OffsetToCenterOfActors;
				am2.IsOffsetCloseEnough = am2.OffsetToCenterOfActors.HorizontalLengthSquared < 1024 * 1024 * 40;
				am2.IsOffsetTargetObservable = IsPathObservable(am2.Act.World, am2.Act, am2.MobOffGrid.Locomotor,
					currPathTarget - am2.OffsetToCenterOfActors, mobileOffGrid.CenterPosition);
				return am2;
			}).ToList();
			var actorsSharingMoveXYBounds = actorsSharingMoveWithProps.Select(a => new { a.MobOffGrid.CenterPosition, a.MobOffGrid.UnitRadius })
				.Aggregate(
					new
					{
						MinX = int.MaxValue,
						MaxX = int.MinValue,
						MinY = int.MaxValue,
						MaxY = int.MinValue,
					},
					(accumulator, o) => new
					{
						MinX = Math.Min(o.CenterPosition.X - o.UnitRadius.Length, accumulator.MinX),
						MaxX = Math.Max(o.CenterPosition.X + o.UnitRadius.Length, accumulator.MaxX),
						MinY = Math.Min(o.CenterPosition.Y - o.UnitRadius.Length, accumulator.MinY),
						MaxY = Math.Max(o.CenterPosition.Y + o.UnitRadius.Length, accumulator.MaxY)
					});

#pragma warning disable IDE0061 // Use expression body for local function
			// TODO: Make radius part of accumulator
			static bool TargetWithinBounds(int left, int right, int top, int bottom, WPos target)
			{
				return left <= target.X && right >= target.X && top <= target.Y && bottom >= target.Y;
			}
#pragma warning restore IDE0061 // Use expression body for local function

			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MinY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MaxY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MaxX, actorsSharingMoveXYBounds.MinY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MaxX, actorsSharingMoveXYBounds.MaxY, 0));

			if (actorsSharingMove.Count > 1 && actorsSharingMoveWithProps.All(a => a.IsOffsetTargetObservable && a.IsOffsetCloseEnough)
				&& !TargetWithinBounds(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MaxX,
									  actorsSharingMoveXYBounds.MinY, actorsSharingMoveXYBounds.MaxY, currPathTarget))
				return FormationMove();
			else
				return NonFormationMove();
		}

		public override bool Tick(Actor self)
		{
			// NOTE: Do not check if the pathfinder is running, as it will automatically turn off after the path is found
			if (mobileOffGrid.thetaStarSearch != null && mobileOffGrid.thetaStarSearch.pathFound
				&& !pathFound)
			{
				// -- Expansion now managed by execution manager
				//using (new Support.PerfTimer("ThetaStar"))
				//	thetaStarSearch.Expand();

				pathRemaining = GetThetaPathAndConvert(self);
				reachedMaxExpansions = mobileOffGrid.thetaStarSearch.HitTotalExpansionLimit;
				if (pathRemaining.Count == 0)
					pathRemaining = new List<WPos>() { target.CenterPosition };

				if (!secondThetaRun)
					GetNextTargetOrComplete(self);
				else
				{
					List<WPos> thetaToNextTarg;
					thetaToNextTarg = GetThetaPathAndConvert(self);
					reachedMaxExpansions = mobileOffGrid.thetaStarSearch.HitTotalExpansionLimit;
					thetaIters++;
					if (thetaToNextTarg.Count > 1)
					{
						pathRemaining = thetaToNextTarg.Concat(pathRemaining).ToList();
						GetNextTargetOrComplete(self, false);
						EndingActions();
						mobileOffGrid.PositionBuffer.Clear();
						isBlocked = false; // only unblock if we have found a better path
					}
					searchingForNextTarget = false;
				}
				mobileOffGrid.thetaStarSearch = null;
				pathFound = true;
				secondThetaRun = false;
			}

			// Will not move unless there is a path to move on
			if (pathRemaining.Count == 0 && currPathTarget == WPos.Zero)
				return false;

			var nearbyActorsSharingMove = GetNearbyActorsSharingMove(self, false);

			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);

			if (IsCanceling)
			{
				EndingActions();
				return true;
			}

			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			mobileOffGrid.SetDesiredFacing(-WAngle.ArcTan(mobileOffGrid.CenterPosition.Y - currPathTarget.Y, mobileOffGrid.CenterPosition.X - currPathTarget.X) + new WAngle(256));

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

			bool UnitHasNotCollided(WVec mv) => mobileOffGrid.GetFirstMoveCollision(self, mv, localAvoidanceDist, locomotor, attackingUnitsOnly: true) == null;

			// Update SeekVector if there is none
			var deltaMoveVec = mobileOffGrid.MovementSpeed * new WVec(new WDist(1024), WRot.FromYaw(Delta.Yaw)) / 1024;
			if (mobileOffGrid.SeekVectors.Count == 0)
				mobileOffGrid.SeekVectors = new List<MvVec>() { new(deltaMoveVec) };
			// moveVec is equal to the Seekector
			var moveVec = mobileOffGrid.SeekVectors[0].Vec;
			// Only change the SeekVector if either we are not searching for the next target, or we are colliding with an object, otherwise continue
			// Revert to deltaMoveVec if we are no longer searching for the next target
			if (!(useLocalAvoidance && !UnitHasNotCollided(moveVec)) && !searchingForNextTarget)
			{
				mobileOffGrid.SeekVectors = new List<MvVec>() { new(deltaMoveVec) };
				moveVec = deltaMoveVec;
			}
			// Since the pathfinder avoids map obstacles, this must be a unit obstacle, so we employ our local avoidance strategy
			else if ((useLocalAvoidance && !UnitHasNotCollided(moveVec)) || isBlocked)
			{
				var revisedMoveVec = moveVec;
				int localAvoidanceAngleOffset;
				var i = 0;
				do
				{
					localAvoidanceAngleOffset = localAvoidanceAngleOffsets[i];
					// Do not go back in the same direction that you went forward, unless this is the last available local avoidance angle offset
					var newAngle = new WAngle(moveVec.Yaw.Angle + localAvoidanceAngleOffset);
					// System.Console.WriteLine($"existing deg: {moveVec.Yaw.Angle}, new deg: {newAngle.Angle}, offset: {localAvoidanceAngleOffset}");
					var testMoveVec = new WVec(localAvoidanceDist, WRot.FromYaw(newAngle));
					var actualMoveVec = new WVec(new WDist(moveVec.Length), WRot.FromYaw(newAngle));
					var propActorMove = mobileOffGrid.GenFinalWVec(new List<MvVec>() { new(testMoveVec) },
																	   mobileOffGrid.FleeVectors);
					var propActorPos = self.CenterPosition + propActorMove;
					if (!mobileOffGrid.TraversedCirclesBuffer.Any(c =>
								HitShapes.CircleShape.PosIsInsideCircle(c, mobileOffGrid.UnitRadius.Length, propActorPos))
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
					moveVec = revisedMoveVec;
					mobileOffGrid.SeekVectors = new List<MvVec>() { new(moveVec) };
					searchingForNextTarget = true;
					isBlocked = false;
				}
			}
			else if (useLocalAvoidance && currLocalAvoidanceAngleOffset != 0 && searchingForNextTarget && UnitHasNotCollided(pastMoveVec))
			{
				pastMoveVec = new WVec(0, 0, 0);
				currLocalAvoidanceAngleOffset = 0;
				searchingForNextTarget = false;
			}

			//// Check collision with walls
			var cellsCollidingSet = new List<CPos>();
			//cellsCollidingSet.AddRange(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 3, locomotor));
			//cellsCollidingSet.AddRange(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 2, locomotor));
			cellsCollidingSet.AddRange(mobileOffGrid.CellsCollidingWithActor(self, moveVec, 1, locomotor));

			var fleeVecToUse = cellsCollidingSet.Distinct().Select(c => self.World.Map.CenterOfCell(c))
														 .Select(wp => RepulsionVecFunc(mobileOffGrid.CenterPosition, wp)).ToList();

			mobileOffGrid.FleeVectors.AddRange(fleeVecToUse.ConvertAll(v => new MvVec(v, 1)));

			//for (var i = 0; i < cellsCollidingSet.Count; i++)
			//{
			//	var currCellsColliding = cellsCollidingSet.ElementAt(i);
			//	if (currCellsColliding.Count > 0)
			//	{
			//		var fleeVecs = currCellsColliding.Select(c => self.World.Map.CenterOfCell(c))    // Note: we add scalar prop. to i
			//										 .Select(wp => RepulsionVecFunc(mobileOffGrid.CenterPosition, wp)).ToList();
			//		var fleeVecToUse = AvgOfVectors(fleeVecs);
			//		mobileOffGrid.FleeVectors.Add(new MvVec(fleeVecToUse, 1));
			//	}
			//}

			var move = mobileOffGrid.GenFinalWVec();

			if (mobileOffGrid.PositionBuffer.Count >= 3)
			{
				var lengthMoved = (mobileOffGrid.PositionBuffer.Last() - mobileOffGrid.PositionBuffer[0]).Length;
				var deltaFirst = currPathTarget - mobileOffGrid.PositionBuffer[0];
				var deltaLast = currPathTarget - mobileOffGrid.PositionBuffer.Last();
				//System.Console.WriteLine($"lengthMoved: {lengthMoved}");

				//if (lengthMoved < mobileOffGrid.MovementSpeed * 2) // && !searchingForNextTarget)
				if (lengthMoved < mobileOffGrid.MovementSpeed) // && !searchingForNextTarget)
				{
					// Create new path to the next path, then join it to the remainder of the existing path
					// Make sure we are not re-running this if we received the same result last time
					// Also do not re-run if max expansions were reached last time
					isBlocked = true;
					System.Console.WriteLine("Blocked!");
					if (currPathTarget != lastPathTarget)
					{
						System.Console.WriteLine("Blocked and not last target!");
						if (!reachedMaxExpansions && thetaIters < maxThetaIters)
						{
							System.Console.WriteLine("Expand Theta when blocked");
							searchingForNextTarget = true;
							// mobileOffGrid.thetaStarSearch = new ThetaStarPathSearch(self.World, self, mobileOffGrid.CenterPosition, currPathTarget
							//										  , actorsSharingMove);
							// IMPORTANT: This AddMoveOrder call relies on _all_ MoveOffGrid Activities
							// being called _before_ any traits are called. This is because all of the actors
							// must be processed collectively in order for the grouped pathfinding to work.
							// Otherwise pathfinding will be inefficient and slow.
							thetaPFexecManager.AddMoveOrder(self, currPathTarget, actorsSharingMove, secondThetaRun: true);
							secondThetaRun = true;
							pathFound = false;
							thetaIters++;
						}
						else if (mobileOffGrid.PositionBuffer.Count >= 15) // 3 seconds
						{
							System.Console.WriteLine("Clear Position Buffer when >= 15");
							isBlocked = false;
							EndingActions();
							return Complete();
						}
					}
				}
				else if (nearbyActorsSharingMove.Count <= 1 && deltaLast.LengthSquared + 512 * 512 > deltaFirst.LengthSquared)
				{
					EndingActions();
					mobileOffGrid.PositionBuffer.Clear();
				}
			}

			var selfHasReachedGoal = Delta.HorizontalLengthSquared < mobileOffGrid.UnitRadius.LengthSquared;
			//var selfHasReachedGoal = Delta.Length < move.Length * 2;
			var completedTargsOfNearbyActors = CompletedTargetsOfActors(GetNearbyActorsSharingMove(self));
			tickCount = tickCount >= maxTicksBeforeLOScheck ? tickCount = 0 : tickCount + 1;
			var nearbyActorHasReachedGoal = tickCount == 0 && completedTargsOfNearbyActors.Contains(currPathTarget) &&
								  pathRemaining.Count == 0 && Delta.Length < move.Length + MaxRangeToTarget().Length;
			var hasReachedGoal = selfHasReachedGoal || nearbyActorHasReachedGoal;

			//RenderPointCollDebug(self, currPathTarget, Color.LightGreen);

			if (hasReachedGoal)
			{
				//System.Console.WriteLine($"selfHasReachedGoal: {selfHasReachedGoal}, nearbyActorHasReachedGoal: {nearbyActorHasReachedGoal}\n" +
				//						 $"Delta.HorizontalLengthSquared {Delta.HorizontalLengthSquared}, mobileOffGrid.UnitRadius.LengthSquared:{mobileOffGrid.UnitRadius.LengthSquared}");
				#if DEBUG || DEBUGWITHOVERLAY
				// System.Console.WriteLine($"if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared) = {Delta.HorizontalLengthSquared < move.HorizontalLengthSquared}");
				#endif

				if (Delta.HorizontalLengthSquared != 0 && selfHasReachedGoal)
				{
					// Ensure we don't include a non-zero vertical component here that would move us away from CruiseAltitude
					var deltaMove = move;
					mobileOffGrid.SeekVectors.Clear();
					//mobileOffGrid.SetDesiredFacing(WAngle.ArcTan(mobileOffGrid.CenterPosition.Y, mobileOffGrid.CenterPosition.X));
					mobileOffGrid.SetDesiredAltitude(dat);
					mobileOffGrid.SetDesiredMove(deltaMove);
					mobileOffGrid.SetIgnoreZVec(true);
				}

				// for debugging onlyad
				//var pathString = "";
				//foreach (var node in pathRemaining)
				//{
				//	pathString += $"({node.X}, {node.Y}), ";
				//}
				//Console.WriteLine(pathString);

				EndingActions();
				searchingForNextTarget = false;
				return GetNextTargetOrComplete(self);
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
										.Select(a => a.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).FirstOrDefault())
										.Where(m => m != null);
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
