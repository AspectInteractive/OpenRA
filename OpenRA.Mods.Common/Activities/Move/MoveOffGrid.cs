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

namespace OpenRA.Mods.Common.Activities
{
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
		readonly List<WPos> positionBuffer = new List<WPos>();
		readonly Locomotor locomotor;

		public static void TestAnyaPathSearch_SplitIntervalAtCornerPoints(Actor self, Target t)
		{
			var anyaSearch = new AnyaPathSearch(self.World, self);
			var startCCPos = anyaSearch.GetNearestCCPos(self.CenterPosition);
			var destCCPos = anyaSearch.GetNearestCCPos(t.CenterPosition);
			var intervalLeft = anyaSearch.GetFirstInterval(startCCPos, 1, AnyaPathSearch.IntervalSide.Left);
			var intervalRight = anyaSearch.GetFirstInterval(startCCPos, 1, AnyaPathSearch.IntervalSide.Right);
			var intervalLeftAndRight = new AnyaPathSearch.Interval(intervalLeft.CCs.Union(intervalRight.CCs).ToList());
			var splitInterval = anyaSearch.SplitIntervalAtCornerPoints(intervalLeftAndRight);
		}

		public MoveOffGrid(Actor self, in Target t, WDist nearEnough, WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, t, initialTargetPosition, targetLineColor)
		{
			this.nearEnough = nearEnough;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}

		public MoveOffGrid(Actor self, in Target t, WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			// ISSUE 1: Self Actor needs to be ignored in block checks otherwise will never find a path to the right
			// ISSUE 2: CCs are not in the right order, larger X values appear earlier than smaller X values, but sometimes
			//          the reverse is true.

			#if DEBUG
			var anyaSearch = new AnyaPathSearch(self.World, self);
			anyaSearch.AnyaFindPath(self.CenterPosition, t.CenterPosition);
			#endif

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

		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOffGrid, WAngle desiredFacing, WDist desiredAltitude, in WVec moveOverride, bool idleTurn = false)
		{
			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);
			var move = mobileOffGrid.Info.CanSlide ? mobileOffGrid.FlyStep(desiredFacing) : mobileOffGrid.FlyStep(mobileOffGrid.Facing);
			if (moveOverride != WVec.Zero)
				move = moveOverride;

			var oldFacing = mobileOffGrid.Facing;
			var turnSpeed = mobileOffGrid.GetTurnSpeed(idleTurn);
			mobileOffGrid.Facing = Util.TickFacing(mobileOffGrid.Facing, desiredFacing, turnSpeed);

			var roll = idleTurn ? mobileOffGrid.Info.IdleRoll ?? mobileOffGrid.Info.Roll : mobileOffGrid.Info.Roll;
			if (roll != WAngle.Zero)
			{
				var desiredRoll = mobileOffGrid.Facing == desiredFacing ? WAngle.Zero :
					new WAngle(roll.Angle * Util.GetTurnDirection(mobileOffGrid.Facing, oldFacing));

				mobileOffGrid.Roll = Util.TickFacing(mobileOffGrid.Roll, desiredRoll, mobileOffGrid.Info.RollSpeed);
			}

			if (mobileOffGrid.Info.Pitch != WAngle.Zero)
				mobileOffGrid.Pitch = Util.TickFacing(mobileOffGrid.Pitch, mobileOffGrid.Info.Pitch, mobileOffGrid.Info.PitchSpeed);

			// Note: we assume that if move.Z is not zero, it's intentional and we want to move in that vertical direction instead of towards desiredAltitude.
			// If that is not desired, the place that calls this should make sure moveOverride.Z is zero.
			if (dat != desiredAltitude || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * mobileOffGrid.Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (desiredAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			mobileOffGrid.SetPosition(self, mobileOffGrid.CenterPosition + move);
		}

		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOffGrid, WAngle desiredFacing, WDist desiredAltitude, bool idleTurn = false)
		{
			MoveOffGridTick(self, mobileOffGrid, desiredFacing, desiredAltitude, WVec.Zero, idleTurn);
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
				return true;
			}

			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
				return true;

			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;
			var pos = mobileOffGrid.GetPosition();
			var delta = checkTarget.CenterPosition - pos;

			// Inside the target annulus, so we're done
			var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
			var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);
			if (insideMaxRange && !insideMinRange)
				return true;

			var isSlider = mobileOffGrid.Info.CanSlide;
			var desiredFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : mobileOffGrid.Facing;
			var move = isSlider ? mobileOffGrid.FlyStep(desiredFacing) : mobileOffGrid.FlyStep(mobileOffGrid.Facing);

			// Inside the minimum range, so reverse if we CanSlide, otherwise face away from the target.
			if (insideMinRange)
			{
				if (isSlider)
					MoveOffGridTick(self, mobileOffGrid, desiredFacing, mobileOffGrid.Info.CruiseAltitude, -move);
				else
				{
					MoveOffGridTick(self, mobileOffGrid, desiredFacing + new WAngle(512), mobileOffGrid.Info.CruiseAltitude, move);
				}

				return false;
			}

			// HACK: Consider ourselves blocked if we have moved by less than 64 WDist in the last five ticks
			// Stop if we are blocked and close enough
			if (positionBuffer.Count >= 5 && (positionBuffer.Last() - positionBuffer[0]).LengthSquared < 4096 &&
				delta.HorizontalLengthSquared <= nearEnough.LengthSquared)
				return true;

			// The next move would overshoot, so consider it close enough or set final position if we CanSlide
			if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared)
			{
				#if DEBUG
				System.Console.WriteLine($"if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared) = {delta.HorizontalLengthSquared < move.HorizontalLengthSquared}");
				#endif

				// For VTOL landing to succeed, it must reach the exact target position,
				// so for the final move it needs to behave as if it had CanSlide.
				if (isSlider || mobileOffGrid.Info.VTOL)
				{
					// Set final (horizontal) position
					if (delta.HorizontalLengthSquared != 0)
					{
						// Ensure we don't include a non-zero vertical component here that would move us away from CruiseAltitude
						var deltaMove = new WVec(delta.X, delta.Y, 0);
						MoveOffGridTick(self, mobileOffGrid, desiredFacing, dat, deltaMove);
					}
				}

				return true;
			}

			if (!HasNotCollided(self, move, locomotor))
				return false;

			if (!isSlider)
			{
				// Using the turn rate, compute a hypothetical circle traced by a continuous turn.
				// If it contains the destination point, it's unreachable without more complex manuvering.
				var turnRadius = CalculateTurnRadius(mobileOffGrid.MovementSpeed, mobileOffGrid.TurnSpeed);

				// The current facing is a tangent of the minimal turn circle.
				// Make a perpendicular vector, and use it to locate the turn's center.
				var turnCenterFacing = mobileOffGrid.Facing + new WAngle(Util.GetTurnDirection(mobileOffGrid.Facing, desiredFacing) * 256);

				var turnCenterDir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(turnCenterFacing));
				turnCenterDir *= turnRadius;
				turnCenterDir /= 1024;

				// Compare with the target point, and keep flying away if it's inside the circle.
				var turnCenter = mobileOffGrid.CenterPosition + turnCenterDir;
				if ((checkTarget.CenterPosition - turnCenter).HorizontalLengthSquared < turnRadius * turnRadius)
					desiredFacing = mobileOffGrid.Facing;
			}

			positionBuffer.Add(self.CenterPosition);
			if (positionBuffer.Count > 5)
				positionBuffer.RemoveAt(0);

			MoveOffGridTick(self, mobileOffGrid, desiredFacing, mobileOffGrid.Info.CruiseAltitude);

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

		public static bool HasNotCollided(Actor self, WVec move, Locomotor locomotor)
		{
			var destActorsWithinRange = self.World.FindActorsOnCircle(self.CenterPosition, new WDist(move.Length * 2)); // we double move length to widen search radius
			var selfCenterPos = self.CenterPosition.XYToInt2();
			var selfCenterPosWithMove = (self.CenterPosition + move * 2).XYToInt2();
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
					var cellWithMove = self.World.Map.CellContaining(corner + move * 2);
					if (locomotor.MovementCostToEnterCell(default, cell, BlockedByActor.None, self) == short.MaxValue &&
						locomotor.MovementCostToEnterCell(default, cellWithMove, BlockedByActor.None, self) == short.MaxValue)
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
							var hasCollisionWithMove = destShape.Info.Type.IntersectsWithHitShape(selfCenterPosWithMove, destActorCenterPos, selfShape);
							if (hasCollision && hasCollisionWithMove) // checking hasCollisionWithMove ensures we don't get stuck if moving out/away from the collision
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
