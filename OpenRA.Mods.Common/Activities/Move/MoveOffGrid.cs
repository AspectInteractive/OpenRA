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

#pragma warning disable SA1512 // SingleLineCommentsMustNotBeFollowedByBlankLine
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1005 // Single line comments should begin with single space
#pragma warning disable SA1513 // Closing brace should be followed by blank line

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

		// LOS Checking interval
		int tickCount = 0;
		int maxTicksBeforeLOScheck = 3;
		readonly List<WPos> positionBuffer = new List<WPos>();
		readonly Locomotor locomotor;

		List<WPos> pathRemaining = new List<WPos>();
		ThetaStarPathSearch thetaStarSearch;
		WPos currPathTarget;

		private void RequeueTargetAndSetCurrTo(WPos target)
		{
			pathRemaining.Insert(0, currPathTarget);
			currPathTarget = target;
		}

		private WPos PopNextTarget()
		{
			var nextTarget = pathRemaining.FirstOrDefault();
			pathRemaining.RemoveAt(0);
			return nextTarget;
		}

		private bool GetNextTargetOrComplete()
		{
			if (pathRemaining.Count > 0)
				currPathTarget = PopNextTarget();
			else
			{
				System.Console.WriteLine("GetNextTargetOrComplete(): No more targets");
				return true;
			}

			System.Console.WriteLine("GetNextTargetOrComplete(): More targets available");
			return false;
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

		public static void RenderPoint(Actor self, WPos pos)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos); }
		public static void RenderPoint(Actor self, WPos pos, Color color)
		{ self.World.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault().AddPoint(pos, color); }


		/* --- Not Working ---
		 * public static WVec CombineDistAndYawVecWithRotation(WDist dist, WVec yawVec, WRot rotation)
		{ return new WVec(dist, WRot.FromYaw(yawVec.Rotate(rotation).Yaw)); }*/

		public MoveOffGrid(Actor self, in Target t, WDist nearEnough, WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, t, initialTargetPosition, targetLineColor)
		{
			this.nearEnough = nearEnough;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}

		public MoveOffGrid(Actor self, in Target t, WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			mobileOffGrid = self.Trait<MobileOffGrid>();
			target = t;
			this.targetLineColor = targetLineColor;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				|| target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
				lastVisibleTarget = Target.FromPos(target.CenterPosition);
			else if (initialTargetPosition.HasValue)
				lastVisibleTarget = Target.FromPos(initialTargetPosition.Value);
		}

		public MoveOffGrid(Actor self, in Target t, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, t, initialTargetPosition, targetLineColor)
		{
			#if DEBUG
			System.Console.WriteLine("MoveOffGrid created at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
			#endif
			this.maxRange = maxRange;
			this.minRange = minRange;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}

		public static WVec GenFinalVector(MobileOffGrid mobileOG)
		{
			var finalVec = WVec.Zero;
			foreach (var vec in mobileOG.SeekVectors)
				finalVec += vec;
			foreach (var vec in mobileOG.FleeVectors)
				finalVec += vec;
			return finalVec;
		}

		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOG, WAngle desiredFacing, WDist desiredAltitude,
										   in WVec moveOverride, bool idleTurn = false, bool ignoreZVec = false)
		{

			var dat = self.World.Map.DistanceAboveTerrain(mobileOG.CenterPosition);
			var speed = mobileOG.MovementSpeed;

			var move = moveOverride == WVec.Zero ? GenFinalVector(mobileOG) : moveOverride;
			if (ignoreZVec)
				move = new WVec(move.X, move.Y, 0);

			var oldFacing = mobileOG.Facing;
			var turnSpeed = mobileOG.GetTurnSpeed(idleTurn);
			mobileOG.Facing = Util.TickFacing(mobileOG.Facing, (desiredFacing == WAngle.Zero ? move.Yaw : desiredFacing), turnSpeed);

			var roll = idleTurn ? mobileOG.Info.IdleRoll ?? mobileOG.Info.Roll : mobileOG.Info.Roll;
			if (roll != WAngle.Zero)
			{
				var desiredRoll = mobileOG.Facing == desiredFacing ? WAngle.Zero :
					new WAngle(roll.Angle * Util.GetTurnDirection(mobileOG.Facing, oldFacing));

				mobileOG.Roll = Util.TickFacing(mobileOG.Roll, desiredRoll, mobileOG.Info.RollSpeed);
			}

			if (mobileOG.Info.Pitch != WAngle.Zero)
				mobileOG.Pitch = Util.TickFacing(mobileOG.Pitch, mobileOG.Info.Pitch, mobileOG.Info.PitchSpeed);

			// Note: we assume that if move.Z is not zero, it's intentional and we want to move in that vertical direction instead of towards desiredAltitude.
			// If that is not desired, the place that calls this should make sure moveOverride.Z is zero.
			if (dat != desiredAltitude || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * mobileOG.Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (desiredAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			System.Console.WriteLine($"move: {move}, moveHLS: {move.HorizontalLengthSquared}");
			mobileOG.SetPosition(self, mobileOG.CenterPosition + move);
		}
		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOffGrid, WAngle desiredFacing, WDist desiredAltitude, bool idleTurn = false)
		{
			MoveOffGridTick(self, mobileOffGrid, desiredFacing, desiredAltitude, WVec.Zero, idleTurn);
		}
		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOffGrid, WDist desiredAltitude)
		{
			MoveOffGridTick(self, mobileOffGrid, WAngle.Zero, desiredAltitude, WVec.Zero);
		}

		protected override void OnFirstRun(Actor self)
		{
			usePathFinder = true;
			useLocalAvoidance = false;

			if (usePathFinder)
			{
				thetaStarSearch = new ThetaStarPathSearch(self.World, self);
				using (new Support.PerfTimer("ThetaStar"))
					pathRemaining = thetaStarSearch.ThetaStarFindPath(mobileOffGrid.CenterPosition, target.CenterPosition);
			}

			if (!usePathFinder || pathRemaining.Count == 0)
				pathRemaining = new List<WPos>() { target.CenterPosition };

			GetNextTargetOrComplete();
		}

		public override bool Tick(Actor self)
		{

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
				mobileOffGrid.SeekVectors.Clear();
				return true;
			}

			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
			{
				mobileOffGrid.SeekVectors.Clear();
				return true;
			}

			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;
			var pos = mobileOffGrid.GetPosition();
			var delta = currPathTarget - pos;

			// Inside the target annulus, so we're done
			var insideMaxRange = maxRange.Length > 0 && PosInRange(currPathTarget, pos, maxRange);
			var insideMinRange = minRange.Length > 0 && PosInRange(currPathTarget, pos, minRange);

			if (insideMaxRange && !insideMinRange)
			{
				mobileOffGrid.SeekVectors.Clear();
				return true;
			}

			var moveVec = mobileOffGrid.MovementSpeed * new WVec(new WDist(1024), WRot.FromYaw(delta.Yaw)) / 1024;
			var maxVecs = 5;
			if (delta != WVec.Zero && mobileOffGrid.SeekVectors.Count < maxVecs)
				mobileOffGrid.SeekVectors.Add(moveVec / maxVecs);

			var move = GenFinalVector(mobileOffGrid);

			// Inside the minimum range, so reverse if we CanSlide, otherwise face away from the target.
			if (insideMinRange)
			{
				MoveOffGridTick(self, mobileOffGrid, mobileOffGrid.Facing, mobileOffGrid.Info.CruiseAltitude);
				return false;
			}

			// HACK: Consider ourselves blocked if we have moved by less than 64 WDist in the last five ticks
			// Stop if we are blocked and close enough
			if (positionBuffer.Count >= 5 && (positionBuffer.Last() - positionBuffer[0]).LengthSquared < 4096 &&
				delta.HorizontalLengthSquared <= nearEnough.LengthSquared)
			{
				mobileOffGrid.SeekVectors.Clear();
				return true;
			}

			// The next move would overshoot, so consider it close enough and set final position
			System.Console.WriteLine($"delta.HorizontalLengthSquared: {delta.HorizontalLengthSquared}, " +
									 $"move.HorizontalLengthSquared: {move.HorizontalLengthSquared}");
			if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared)
			{
				#if DEBUG || DEBUGWITHOVERLAY
				System.Console.WriteLine($"if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared) = {delta.HorizontalLengthSquared < move.HorizontalLengthSquared}");
				#endif

				if (delta.HorizontalLengthSquared != 0)
				{
					// Ensure we don't include a non-zero vertical component here that would move us away from CruiseAltitude
					var deltaMove = new WVec(delta.X, delta.Y, 0);
					mobileOffGrid.SeekVectors.Clear();
					MoveOffGridTick(self, mobileOffGrid, mobileOffGrid.Facing, dat, deltaMove, ignoreZVec: true);
				}

				mobileOffGrid.SeekVectors.Clear();
				searchingForNextTarget = false;
				return GetNextTargetOrComplete();
			}

			if (useLocalAvoidance && searchingForNextTarget)
			{
				if (tickCount >= maxTicksBeforeLOScheck)
					tickCount = 0;
				else if (tickCount == 0)
				{
					// We can abandon the local avoidance strategy 
					if (TargetInLOS(self, move, locomotor, pathRemaining.FirstOrDefault()))
					{
						searchingForNextTarget = false;
						return GetNextTargetOrComplete();
					}
				}
				tickCount++;
			}

			positionBuffer.Add(self.CenterPosition);
			if (positionBuffer.Count > 5)
				positionBuffer.RemoveAt(0);

			MoveOffGridTick(self, mobileOffGrid, mobileOffGrid.Info.CruiseAltitude);

			return false;
		}

		// Cheap LOS checking to validate whether we can see the next target or not (and cancel any existing local avoidance strategy)
		private bool TargetInLOS(Actor self, WVec move, Locomotor locomotor, WPos targPos)
		{
			var stepsToTarg = (int)((mobileOffGrid.CenterPosition - targPos).HorizontalLengthSquared / move.HorizontalLengthSquared);
			var stepDelta = (mobileOffGrid.CenterPosition - targPos) / stepsToTarg;
			var n = 1;
			while (n < stepsToTarg)
			{
				if (!HasNotCollided(self, stepDelta * n, 3, locomotor))
					return false;
				n++;
			}
			return true;
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

		public static bool HasNotCollided(Actor self, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0)
		{
			var destActorsWithinRange = self.World.FindActorsOnCircle(self.CenterPosition, new WDist(move.Length * 2)); // we double move length to widen search radius
			var selfCenterPos = self.CenterPosition.XYToInt2();
			var selfCenterPosWithMoves = new List<int2>();
			var startI = skipLookAheadAmt == 0 ? 0 : skipLookAheadAmt - 1;
			for (var i = startI; i < lookAhead; i++)
				selfCenterPosWithMoves.Add((self.CenterPosition + move * i).XYToInt2());
			/*var cellsToCheck = Util.ExpandFootprint(self.World.Map.CellContaining(selfCenterPosWithMove_WPos), true);*/

			// for each actor we are potentially colliding with
			var selfShapes = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
			foreach (var selfShape in selfShapes)
			{
				var hitShapeCorners = selfShape.Info.Type.GetCorners(selfCenterPos);
				foreach (var corner in hitShapeCorners)
				{
					var cornerWithMove = corner + move * 2;
					/*System.Console.WriteLine($"checking corner {corner} of shape {selfShape.Info.Type}");*/
					var cell = self.World.Map.CellContaining(corner);
					var cellWithMovesBlocked = new List<bool>();
					Func<CPos, bool> cellIsBlocked = c =>
						{ return locomotor.MovementCostToEnterCell(default, c, BlockedByActor.None, self) == short.MaxValue; };
					for (var i = startI; i < lookAhead; i++)
						cellWithMovesBlocked.Add(cellIsBlocked(self.World.Map.CellContaining(corner + move * i)));
					if ((!incOrigin || cellIsBlocked(cell)) && cellWithMovesBlocked.Any(c => c == true))
					{
						/*System.Console.WriteLine($"collided with cell {cell} with value {short.MaxValue}");*/
						return false;
					}
				}

				foreach (var destActor in destActorsWithinRange)
				{
					var destActorCenterPos = destActor.CenterPosition.XYToInt2();
					if ((destActor.TraitsImplementing<Building>().Any() || destActor.TraitsImplementing<Mobile>().Any())
						 && !(self.CurrentActivity is MobileOffGrid.ReturnToCellActivity))
					{
						var destShapes = destActor.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
						foreach (var destShape in destShapes)
						{
							var hasCollision = destShape.Info.Type.IntersectsWithHitShape(selfCenterPos, destActorCenterPos, selfShape);
							var posWithMovesBlocked = new List<bool>();
							Func<int2, bool> posIsColliding = p =>
								{ return destShape.Info.Type.IntersectsWithHitShape(p, destActorCenterPos, selfShape); };
							for (var i = 0; i < lookAhead; i++)
								posWithMovesBlocked.Add(posIsColliding(selfCenterPosWithMoves.ElementAt(i)));
							// checking hasCollisionWithMove ensures we don't get stuck if moving out/away from the collision
							if ((!incOrigin || hasCollision) && posWithMovesBlocked.Any(p => p == true))
								return false;
						}
					}
				}
			}

			return true;
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
