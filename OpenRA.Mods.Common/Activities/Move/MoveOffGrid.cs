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
using static OpenRA.Mods.Common.Pathfinder.ThetaStarPathSearch;
using static OpenRA.Mods.Common.Traits.MobileOffGrid;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;

#pragma warning disable SA1512 // SingleLineCommentsMustNotBeFollowedByBlankLine
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1005 // Single line comments should begin with single space
#pragma warning disable SA1513 // Closing brace should be followed by blank line
#pragma warning disable SA1108 // Block statements should not contain embedded comments

namespace OpenRA.Mods.Common.Activities
{
	public class MoveOffGrid : Activity
	{
		readonly MobileOffGrid mobileOffGrid;
		WVec Delta => currPathTarget - mobileOffGrid.CenterPosition;
		readonly WDist maxRange;
		readonly WDist minRange;
		readonly Color? targetLineColor;

		Target target;
		readonly WDist nearEnough;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;

		// Options for pathfinder (chosen in the constructor)
		bool usePathFinder = true;
		bool useLocalAvoidance = true;
		readonly WDist localAvoidanceDist;

		MoveType moveType = MoveType.Undefined;

#pragma warning disable SA1137 // Elements should have the same indentation

		readonly List<int> localAvoidanceAngleOffsetsLeft = new()
		{
			 0, -64, -128, -192,
			-256, -320, -384,
			-448, -512, -576,
			-640, -704, -768,
			-832, -896, -960,
			-1024
		};

		readonly List<int> localAvoidanceAngleOffsetsRight = new()
		{
			 0, 64,  128,  192,
			 256,  320,  384,
			 448,  512,  576,
			 640,  704,  768,
			 832,  896,  960,
			 1024
		};

#pragma warning restore SA1137 // Elements should have the same indentation*/

		int currLocalAvoidanceAngleOffset = 0;
		WVec pastMoveVec;

		// LOS Checking interval
		int tickCount = 0;
		readonly int maxTicksBeforeLOScheck = 3;
		readonly Locomotor locomotor;

		ThetaPathfinderExecutionManager thetaPFexecManager;
		public List<TraitPair<MobileOffGrid>> ActorsSharingMove = new();
		bool pathFound = false;
		List<WPos> pathRemaining = new();
		WPos currPathTarget;
		WPos lastPathTarget;
		bool firstMove = false;
		int thetaIters = 0;
		readonly int maxThetaIters = 3;

		WPos FinalPathTarget => pathRemaining.Count > 0 ? pathRemaining.Last() : currPathTarget;

		public enum MoveType { Undefined, Formation, NonFormation }

		public List<BlockedByCell> DirectionOfCellsBlockingPos(Actor self, WPos pos, List<CPos> cells)
		{
			var cellContainingPos = self.World.Map.CellContaining(pos);
			var blockedDirections = new List<BlockedByCell>();

			foreach (var cell in cells)
			{
				if (cell.X < cellContainingPos.X && cell.Y < cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.TopLeft);
				else if (cell.X == cellContainingPos.X && cell.Y < cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.Top);
				else if (cell.X > cellContainingPos.X && cell.Y < cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.TopRight);
				else if (cell.X < cellContainingPos.X && cell.Y == cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.Left);
				else if (cell.X > cellContainingPos.X && cell.Y == cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.Right);
				else if (cell.X < cellContainingPos.X && cell.Y > cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.BottomLeft);
				else if (cell.X == cellContainingPos.X && cell.Y > cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.Bottom);
				else if (cell.X > cellContainingPos.X && cell.Y > cellContainingPos.Y)
					blockedDirections.Add(BlockedByCell.BottomRight);
			}

			return blockedDirections;
		}

		public WDist MaxRangeToTarget()
		{
			var actorMobileOgs = ActorsSharingMove.Where(a => !a.Actor.IsDead).Select(a => a.Trait).ToList();

			return actorMobileOgs.Count > 0 ? new WDist(actorMobileOgs.Select(m => m.UnitRadius.Length).Sum()
														/ Math.Max(ActorsSharingMove.Count, 1)
														* Exts.ISqrt(ActorsSharingMove.Count, Exts.ISqrtRoundMode.Ceiling)) : WDist.Zero;
		}

		WPos PopNextTarget()
		{
			var nextTarget = pathRemaining.FirstOrDefault();
			pathRemaining.RemoveAt(0);
			return nextTarget;
		}

		bool GetNextTargetOrComplete(Actor self, bool completeCurrTarg = true)
		{
			//mobileOffGrid.Overlay.AddText(mobileOffGrid.CenterPosition, pathRemaining.Count.ToString(), Color.Yellow,
			//	(int)3, MobileOffGridOverlay.OverlayKeyStrings.Pathing);
			if (completeCurrTarg)
			{
				mobileOffGrid.PathComplete.Add(currPathTarget);
				mobileOffGrid.LastCompletedTarget = currPathTarget;
			}
			if (pathRemaining.Count > 0)
			{
				lastPathTarget = currPathTarget; // for validation purposes
				mobileOffGrid.LastPathTarget = currPathTarget; // for validation purposes
				if (firstMove)
				{
					currPathTarget = AdjustedCurrPathTarget(self, PopNextTarget());
					firstMove = false;
				}
				else
					currPathTarget = PopNextTarget();
				mobileOffGrid.CurrPathTarget = currPathTarget;
			}
			else
			{
				EndingActions();
				return Complete();
			}

			return false;
		}

		public void ResetVariables()
		{
			currPathTarget = WPos.Zero;
			mobileOffGrid.CurrPathTarget = WPos.Zero;
			lastPathTarget = WPos.Zero;
			mobileOffGrid.LastPathTarget = WPos.Zero;
			mobileOffGrid.IsBlocked = false;
			mobileOffGrid.SearchingForNextTarget = false;
			pathFound = false;
		}

		public bool Complete()
		{
			ResetVariables();
			mobileOffGrid.PathComplete.Clear();
			mobileOffGrid.PositionBuffer.Clear();
			mobileOffGrid.CurrPathTarget = WPos.Zero;
			return true;
		}

		public static bool PosInRange(WPos pos, WPos origin, WDist range)
		{
			return (pos - origin).HorizontalLengthSquared <= range.LengthSquared;
		}

		public static void RenderLine(Actor self, List<WPos> line, string key)
		{
			var renderLine = new List<WPos>();
			renderLine.AddRange(line);

			if (line.Count > 1) // cannot render a path of length 1
				self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine, key);
		}

		const int LineThickness = 1;

		public static void RenderLine(Actor self, WPos pos1, WPos pos2, string key)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine, key);
		}

		public static void RenderLineWithColor(Actor self, WPos pos1, WPos pos2, Color color, string key)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault()
				.AddLineWithColor(renderLine, color, key);
		}
		public static void RenderLineCollDebug(Actor self, WPos pos1, WPos pos2, int thickness = LineThickness)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddLine(renderLine, thickness);
		}
		public static void RenderLineWithColorCollDebug(Actor self, WPos pos1, WPos pos2, Color color,
			int thickness = LineThickness, LineEndPoint endpoints = LineEndPoint.None)
		{
			var renderLine = new List<WPos>() { pos1, pos2 };
			self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault()
				.AddLineWithColor(renderLine, color, thickness, endpoints);
		}

		public static void RenderCircle(Actor self, WPos pos, WDist radius, string key)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius), key); }

		public static void RenderCircleCollDebug(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius)); }
		public static void RenderCircleCollDebug(Actor self, WPos pos, WDist radius, int thickness)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddCircle((pos, radius), thickness); }
		public static void RenderCircleColorCollDebug(Actor self, WPos pos, WDist radius, Color color, int thickness)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddCircleWithColor((pos, radius), color, thickness); }

		public static void RenderCircleWithColor(Actor self, WPos pos, WDist radius, Color color, string key)
		{
			self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>()
				.FirstEnabledTraitOrDefault().AddCircleWithColor((pos, radius), color, key);
		}
		public static void RemoveCircle(Actor self, WPos pos, WDist radius)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().RemoveCircle((pos, radius)); }
		public static void RemoveCircle(World world, WPos pos, WDist radius)
		{ world.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().RemoveCircle((pos, radius)); }
		public static void RenderPoint(Actor self, WPos pos, string key)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, key); }
		public static void RenderPoint(Actor self, WPos pos, Color color, string key)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color, key); }
		public static void RenderPointCollDebug(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos); }
		public static void RenderPointCollDebug(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color); }
		public static void RenderPointCollDebug(Actor self, WPos pos, Color color, int thickness)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color, thickness); }
		public static void RenderActorPointCollDebug(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddActorPoint(self, pos); }
		public static void RenderActorPointCollDebug(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddActorPoint(self, pos, color); }
		public static void RenderActorPointCollDebug(Actor self, WPos pos, Color color, int thickness)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddActorPoint(self, pos, color, thickness); }
		public static void RemoveActorPointCollDebug(Actor self, Actor actor)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorPoint(actor); }
		public static void RemoveActorPointCollDebug(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorPoint(pos); }
		public static void RemoveActorPointCollDebug(Actor self, Actor actor, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorPoint(actor, pos); }
		public static void RenderTextCollDebug(Actor self, WPos pos, string text, Color color, string fontname = null)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddText(pos, text, color, fontname); }
		public static void RemoveTextCollDebug(Actor self, WPos pos, string text)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveText(pos, text); }
		public static void RenderActorTextCollDebug(Actor self, Actor actorToAdd, WPos pos, string text, Color color, string fontname = null)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().AddActorText(actorToAdd, pos, text, color, fontname); }
		public static void RemoveActorTextCollDebug(Actor self, Actor actorToRemove, WPos pos, string text)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorText(actorToRemove, pos, text); }
		public static void RemoveActorTextCollDebug(Actor self, Actor actorToRemove, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorText(actorToRemove, pos); }
		public static void RemoveActorTextCollDebug(Actor self, Actor actorToRemove, string text)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorText(actorToRemove, text); }
		public static void RemoveActorTextCollDebug(Actor self, Actor actorToRemove)
		{ self.World.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault().RemoveActorText(actorToRemove); }

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

			ActorsSharingMove = GetGroupedActorsWithMobileOGs(groupedActors);

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
#if DEBUG || DEBUGWITHOVERLAY
			//Console.WriteLine($"Target is {target}");
			//Console.WriteLine("MoveOffGrid created at " + DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
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
				thetaPFexecManager.AddMoveOrder(self, GetValidTargetPos(), ActorsSharingMove);
				thetaPFexecManager.PlayerCirclesLocked = false;
			}

			if (!usePathFinder)
				pathRemaining = new List<WPos>() { GetValidTargetPos() };
		}

		public WPos GetValidTargetPos()
		{
			if (target != default && target.Type != TargetType.Invalid)
				return target.CenterPosition;
			else
				return lastVisibleTarget.CenterPosition;
		}

		public void EndingActions()
		{
			mobileOffGrid.SeekVectors.Clear();
			mobileOffGrid.FleeVectors.Clear();
		}

		public WPos PadCCifCC(Actor self, PathPos pp)
		{ return pp.ccPos != CCPos.Zero ? PadCC(self.World, self, locomotor, mobileOffGrid, pp.ccPos) : pp.wPos; }

		public List<WPos> GetThetaPathAndConvert(Actor self)
		{ return mobileOffGrid.CurrThetaSearch.path.ConvertAll(pp => PadCCifCC(self, pp)); }

		public static WPos GetCenterOfUnits(List<TraitPair<MobileOffGrid>> actorsSharingMove)
		{
			return actorsSharingMove.Select(a => a.Trait.CenterPosition).Average();
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
			var centerOfActorsSharingMove = GetCenterOfUnits(ActorsSharingMove);

			WPos FormationMove()
			{
				moveType = MoveType.Formation;
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

			var actorsSharingMoveActivitiesMoveType = ActorsSharingMove
				.Where(a => a.Actor.CurrentActivity is MoveOffGrid && a.Actor.CurrentActivity.State == ActivityState.Active)
				.Select(a => a.Actor.CurrentActivity.ActivitiesImplementing<MoveOffGrid>(false).FirstOrDefault().moveType);
			if (actorsSharingMoveActivitiesMoveType.Any(t => t == MoveType.NonFormation))
				return NonFormationMove();
			// Because we check all actorsSharingMove, any shared actor that has a Formation move type is confirmation that we can move in formation
			else if (actorsSharingMoveActivitiesMoveType.Any(t => t == MoveType.Formation))
				return FormationMove();

			var actorsSharingMoveWithProps = ActorsSharingMove.Select(a => new ActorWithMoveProps
			{
				MobOffGrid = a.Trait,
				Act = a.Actor
			}).Select(am => { am.OffsetToCenterOfActors = centerOfActorsSharingMove - am.MobOffGrid.CenterPosition; return am; })
			.Select(am2 =>
			{
				am2.OffsetTarget = currPathTarget - am2.OffsetToCenterOfActors;
				am2.IsOffsetCloseEnough = am2.OffsetToCenterOfActors.HorizontalLengthSquared < 1024 * 1024 * 40;
				am2.IsOffsetTargetObservable = IsPathObservable(am2.Act.World, am2.Act, am2.MobOffGrid.Locomotor,
					currPathTarget - am2.OffsetToCenterOfActors, mobileOffGrid.CenterPosition, mobileOffGrid.UnitHitShape, true, 1);
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
			static bool TargetWithinBounds(int left, int right, int top, int bottom, WPos target)
			{
				return left <= target.X && right >= target.X && top <= target.Y && bottom >= target.Y;
			}
#pragma warning restore IDE0061 // Use expression body for local function

#if DEBUGWITHOVERLAY
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MinY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MaxY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MaxX, actorsSharingMoveXYBounds.MinY, 0));
			//RenderPointCollDebug(self, new WPos(actorsSharingMoveXYBounds.MaxX, actorsSharingMoveXYBounds.MaxY, 0));
#endif

			if (ActorsSharingMove.Count > 1 && actorsSharingMoveWithProps.All(a => a.IsOffsetTargetObservable && a.IsOffsetCloseEnough)
				&& !TargetWithinBounds(actorsSharingMoveXYBounds.MinX, actorsSharingMoveXYBounds.MaxX,
									  actorsSharingMoveXYBounds.MinY, actorsSharingMoveXYBounds.MaxY, currPathTarget))
				return FormationMove();
			else
				return NonFormationMove();
		}
		public override bool Tick(Actor self)
		{
			// NOTE: Do not check if the pathfinder is running, as it will automatically turn off after the path is found
			if (mobileOffGrid.CurrThetaSearch != null && mobileOffGrid.CurrThetaSearch.pathFound
				&& !pathFound)
			{
				mobileOffGrid.CurrMovementState = MovementState.Starting;
				pathRemaining = GetThetaPathAndConvert(self);

				// Default movement if no path is found
				if (pathRemaining.Count == 0)
					pathRemaining = new List<WPos>() { GetValidTargetPos() };

				GetNextTargetOrComplete(self);

				// start new ending actions
				EndingActions();
				mobileOffGrid.PositionBuffer.Clear();
				mobileOffGrid.IsBlocked = false;
				mobileOffGrid.SearchingForNextTarget = false;
				// end new ending actions
				mobileOffGrid.CurrThetaSearch = null;
				pathFound = true;
			}

			// Will not move unless there is a path to move on
			if (pathRemaining.Count == 0 && currPathTarget == WPos.Zero)
				return false;

			var nearbyActorsSharingMove = GetNearbyActorsSharingMove(self, false);
			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);
			var pos = mobileOffGrid.GetPosition();

			if (IsCanceling)
			{
				mobileOffGrid.CurrMovementState = MovementState.Ending;
				EndingActions();
				return Complete();
				// return true -- this was the original working code
			}

			// Handle Frozen Actors
			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);
			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
			{
				mobileOffGrid.CurrMovementState = MovementState.Ending;
				EndingActions();
				return true;
			}

			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

			// For Attack Move, we stop when we are within firing range of the target
			if (minRange != WDist.Zero || maxRange != WDist.Zero)
			{
				var insideMinRange = minRange.Length > 0 && PosInRange(FinalPathTarget, pos, minRange);
				var insideMaxRange = maxRange.Length > 0 && PosInRange(FinalPathTarget, pos, maxRange);
				if (insideMaxRange && !insideMinRange)
				{
					EndingActions();
					return Complete();
				}
			}

			if (mobileOffGrid.PositionBuffer.Count >= 3)
			{
				var lengthMoved = (mobileOffGrid.PositionBuffer.Last() - mobileOffGrid.PositionBuffer[0]).Length;
				var deltaFirst = currPathTarget - mobileOffGrid.PositionBuffer[0];
				var deltaLast = currPathTarget - mobileOffGrid.PositionBuffer.Last();

				if (lengthMoved < mobileOffGrid.MovementSpeed) // && !mobileOffGrid.SearchingForNextTarget)
				{
					// Create new path to the next path, then join it to the remainder of the existing path
					// Make sure we are not re-running this if we received the same result last time
					// Also do not re-run if max expansions were reached last time
					mobileOffGrid.IsBlocked = true;
					//Console.WriteLine("Blocked!");
					if (currPathTarget != lastPathTarget)
					{
						//Console.WriteLine("Blocked and not last target!");
						if (mobileOffGrid.PositionBuffer.Count >= 20) // 3 seconds
						{
							Console.WriteLine("Theta running a second time.");
							if (thetaIters < maxThetaIters)
							{
								mobileOffGrid.CurrMovementState = MovementState.Repathing;
								thetaPFexecManager.RemovePF(self);
								mobileOffGrid.CurrThetaSearch = null;
								mobileOffGrid.IsBlocked = false;
								EndingActions();
								Complete();
								thetaPFexecManager.AddMoveOrder(self, GetValidTargetPos());
								//RenderCircleColorCollDebug(self, mobileOffGrid.CenterPosition, mobileOffGrid.UnitRadius, Color.Purple, 3);
								thetaPFexecManager.PlayerCirclesLocked = false;
								thetaIters++;
							}
							else
							{
								mobileOffGrid.CurrMovementState = MovementState.FailedStuckButNotLastTarget;
								mobileOffGrid.IsBlocked = false;
								mobileOffGrid.PositionBuffer.Clear();
								EndingActions();
								return Complete();
							}
						}
					}
				}
				else if (nearbyActorsSharingMove.Count <= 1 && deltaLast.LengthSquared + 512 * 512 > deltaFirst.LengthSquared)
				{
					mobileOffGrid.CurrMovementState = MovementState.FailedStuck;
					EndingActions();
					mobileOffGrid.PositionBuffer.Clear();
				}
				// lengthMoved >= mobileOffGrid.MovementSpeed
				else
					mobileOffGrid.PositionBuffer.Clear();
			}

			var selfHasReachedGoal = Delta.HorizontalLengthSquared < mobileOffGrid.UnitRadius.LengthSquared;
			var completedTargsOfNearbyActors = CompletedTargetsOfActors(GetNearbyActorsSharingMove(self));
			tickCount = tickCount >= maxTicksBeforeLOScheck ? tickCount = 0 : tickCount + 1;
			var nearbyActorHasReachedGoal = tickCount == 0 &&
				completedTargsOfNearbyActors.Contains(currPathTarget) &&
				//pathRemaining.Count == 0 && // Enable this if you want line formation rather than grouped movement
				Delta.Length < mobileOffGrid.GenFinalWVec().Length + MaxRangeToTarget().Length;

			var hasReachedGoal = selfHasReachedGoal || nearbyActorHasReachedGoal;

			if (hasReachedGoal)
			{
#if DEBUG || DEBUGWITHOVERLAY
				mobileOffGrid.Overlay.AddCircle(mobileOffGrid.CenterPosition, mobileOffGrid.UnitRadius, Color.Pink,
					(int)MobileOffGridOverlay.PersistConst.Never, 3, MobileOffGridOverlay.OverlayKeyStrings.Pathing);
				// System.Console.WriteLine($"if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared) = {Delta.HorizontalLengthSquared < move.HorizontalLengthSquared}");
#endif

				if (Delta.HorizontalLengthSquared != 0 && selfHasReachedGoal)
				{
					// Ensure we don't include a non-zero vertical component here that would move us away from CruiseAltitude
					mobileOffGrid.Overlay.AddCircle(mobileOffGrid.CenterPosition, mobileOffGrid.UnitRadius, Color.HotPink,
						(int)MobileOffGridOverlay.PersistConst.Never, 3, MobileOffGridOverlay.OverlayKeyStrings.Pathing);
					mobileOffGrid.SeekVectors.Clear();
					mobileOffGrid.SetForcedAltitude(dat);
					mobileOffGrid.SetForcedMove(mobileOffGrid.GenFinalWVec()); // we may be able to get rid of this as it seems redundant
					mobileOffGrid.SetIgnoreZVec(true);
				}

				// for debugging only
				//var pathString = "";
				//foreach (var node in pathRemaining)
				//{
				//	pathString += $"({node.X}, {node.Y}), ";
				//}
				//Console.WriteLine(pathString);
				mobileOffGrid.CurrMovementState = MovementState.FinishedTarget;
				EndingActions();
				mobileOffGrid.SearchingForNextTarget = false;
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

		public List<TraitPair<MobileOffGrid>> GetNearbyActorsSharingMove(Actor self, bool excludeSelf = true)
		{
			var nearbyActors = self.World.FindActorsInCircle(mobileOffGrid.CenterPosition, MaxRangeToTarget());
			return ActorsSharingMove.Where(a => nearbyActors.Contains(a.Actor) && !a.Actor.IsDead && (a.Actor != self || !excludeSelf)
				&& a.Actor.Owner == self.Owner && a.Actor.CurrentActivity is not ReturnToCellActivity).ToList();
		}

		public List<WPos> CompletedTargetsOfActors(List<TraitPair<MobileOffGrid>> actorList)
		{
			var actorListMobileOGs = actorList.Where(a => !a.Actor.IsDead).Select(a => a.Trait).ToList();
			var completedTargs = actorListMobileOGs.Select(m => m.PathComplete).SelectMany(p => p).Distinct().ToList();
			completedTargs = completedTargs.Union(actorListMobileOGs.Select(m => m.LastCompletedTarget)).ToList();
			return completedTargs;
		}
	}
}
