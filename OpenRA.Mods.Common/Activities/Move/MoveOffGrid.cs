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
		bool usePathFinder = true;
		bool useLocalAvoidance = true;
		int rightAngleTurns = 0;
		int tickCount = 0;
		int maxTicksBeforeLOScheck = 3;
		int lineDefaultLength = 10240;
		readonly int rotateAmtIfBlocked = 5;
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
			usePathFinder = true;
			useLocalAvoidance = false;

			if (usePathFinder)
			{
				thetaStarSearch = new ThetaStarPathSearch(self.World, self);
				using (new Support.PerfTimer("ThetaStar"))
					pathRemaining = thetaStarSearch.ThetaStarFindPath(self.CenterPosition, t.CenterPosition);
			}
			else
				pathRemaining = new List<WPos>() { t.CenterPosition };

			GetNextTargetOrComplete();

			mobileOffGrid = self.Trait<MobileOffGrid>();
			target = t;

			this.targetLineColor = targetLineColor;
			locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				|| target.Type == TargetType.FrozenActor || target.SelfIsTerrainType())
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

		public static WVec MoveStep(int speed, WAngle facing)
		{
			var dir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(facing));
			return speed * dir / 1024;
		}

		public static void MoveOffGridTick(Actor self, MobileOffGrid mobileOffGrid, WAngle desiredFacing, WDist desiredAltitude, in WVec moveOverride, bool idleTurn = false)
		{
			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);
			var speed = mobileOffGrid.MovementSpeed;
			var move = mobileOffGrid.Info.CanSlide ? MoveStep(speed, desiredFacing) : MoveStep(speed, mobileOffGrid.Facing);
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
			//var delta = checkTarget.CenterPosition - pos;
			var delta = currPathTarget - pos;

			// Inside the target annulus, so we're done
			//var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
			//var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);
			var insideMaxRange = maxRange.Length > 0 && PosInRange(currPathTarget, pos, maxRange);
			var insideMinRange = minRange.Length > 0 && PosInRange(currPathTarget, pos, minRange);

			if (insideMaxRange && !insideMinRange)
				return true;

			var isSlider = mobileOffGrid.Info.CanSlide;
			var desiredFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : mobileOffGrid.Facing;
			var speed = mobileOffGrid.MovementSpeed;
			var move = isSlider ? MoveStep(speed, desiredFacing) : MoveStep(speed, mobileOffGrid.Facing);

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
				searchingForNextTarget = false;
				return GetNextTargetOrComplete();
			}

			// Since the pathfinder avoids map obstacles, this must be a unit obstacle, so we employ our local avoidance strategy
			if (useLocalAvoidance && !HasNotCollided(self, move, 3, locomotor) && !searchingForNextTarget)
			{
				// > 0 means the point is to the right of the current move angle, < 0 means the point is to the left.
				var leftOrRightInc = move.CrossProduct(currPathTarget) > 0 ? -1 : 1;
				WVec revisedMove;
				var i = 0;
				do
				{
					var newDegrees = move.Yaw.Angle + 256 * leftOrRightInc * i;
					System.Console.WriteLine($"existing deg: {(move.Yaw.Angle)}, new deg: {newDegrees}");
					var newAngle = new WAngle(Nearest90Deg(newDegrees));
					revisedMove = new WVec(new WDist(move.Length), WRot.FromYaw(newAngle));
					RenderLine(self, mobileOffGrid.CenterPosition, mobileOffGrid.CenterPosition + revisedMove);
					i++;
				}
				while (!HasNotCollided(self, revisedMove, 3, locomotor) && i < 4);

				if (HasNotCollided(self, revisedMove, 3, locomotor))
				{
					// TO DO: Need a way to reset units that get stuck. Maybe set a flag?
					rightAngleTurns += leftOrRightInc;
					var newTargDelta = revisedMove * lineDefaultLength / revisedMove.Length;

					#if DEBUGWITHOVERLAY
					System.Console.WriteLine($"move.Yaw {move.Yaw}" +
											 $",newTargDelta.Yaw: {newTargDelta.Yaw}" +
											 $",revisedMove.Yaw: {revisedMove.Yaw}");
					RenderLine(self, mobileOffGrid.CenterPosition, mobileOffGrid.CenterPosition + newTargDelta);
					RenderPoint(self, mobileOffGrid.CenterPosition + revisedMove, Color.LightGreen);
					#endif

					var newTarget = NearestPosInMap(self, mobileOffGrid.CenterPosition + newTargDelta);
					RequeueTargetAndSetCurrTo(newTarget); // Put existing target back in queue and set the curr target to the new one
					searchingForNextTarget = true;
				}
			}
			else if (useLocalAvoidance && !HasNotCollided(self, move, 1, locomotor)) // No more moves left yet still blocked, so return false
				return false;

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
