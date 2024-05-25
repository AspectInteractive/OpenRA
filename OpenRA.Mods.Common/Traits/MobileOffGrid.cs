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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

#pragma warning disable SA1513 // Closing brace should be followed by blank line

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Unit is able to move.")]
	public class MobileOffGridInfo : PausableConditionalTraitInfo, IMoveInfo, IPositionableInfo, IFacingInfo, IActorPreviewInitInfo,
		IEditorActorOptions
	{
		[LocomotorReference]
		[FieldLoader.Require]
		[Desc("Which Locomotor does this trait use. Must be defined on the World actor.")]
		public readonly string Locomotor = null;
		public readonly IdleBehaviorType IdleBehavior = IdleBehaviorType.None;

		/* --- Start of Aircraft Variables --- */
		public readonly WDist CruiseAltitude = new WDist(1280);

		[Desc("Whether the aircraft can be repulsed.")]
		public readonly bool Repulsable = true;

		[Desc("The distance it tries to maintain from other aircraft if repulsable.")]
		public readonly WDist IdealSeparation = new WDist(1706);

		[Desc("The speed at which the aircraft is repulsed from other aircraft. Specify -1 for normal movement speed.")]
		public readonly int RepulsionSpeed = -1;

		public readonly WAngle InitialFacing = WAngle.Zero;

		[Desc("Speed at which the actor turns.")]
		public readonly WAngle TurnSpeed = new WAngle(512);

		[Desc("Turn speed to apply when aircraft flies in circles while idle. Defaults to TurnSpeed if undefined.")]
		public readonly WAngle? IdleTurnSpeed = null;

		[Desc("Maximum flight speed when cruising.")]
		public int Speed = 1;

		[Desc("If non-negative, force the aircraft to move in circles at this speed when idle (a speed of 0 means don't move), ignoring CanHover.")]
		public readonly int IdleSpeed = -1;

		[Desc("Body pitch when flying forwards. Only relevant for voxel aircraft.")]
		public readonly WAngle Pitch = WAngle.Zero;

		[Desc("Pitch steps to apply each tick when starting/stopping.")]
		public readonly WAngle PitchSpeed = WAngle.Zero;

		[Desc("Body roll when turning. Only relevant for voxel aircraft.")]
		public readonly WAngle Roll = WAngle.Zero;

		[Desc("Body roll to apply when aircraft flies in circles while idle. Defaults to Roll if undefined. Only relevant for voxel aircraft.")]
		public readonly WAngle? IdleRoll = null;

		[Desc("Roll steps to apply each tick when turning.")]
		public readonly WAngle RollSpeed = WAngle.Zero;

		[Desc("Minimum altitude where this aircraft is considered airborne.")]
		public readonly int MinAirborneAltitude = 1;

		public readonly HashSet<string> LandableTerrainTypes = new HashSet<string>();

		[Desc("Can the actor be ordered to move in to shroud?")]
		public readonly bool MoveIntoShroud = true;

		[Desc("e.g. crate, wall, infantry")]
		public readonly BitSet<CrushClass> Crushes = default(BitSet<CrushClass>);

		[Desc("Types of damage that are caused while crushing. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> CrushDamageTypes = default(BitSet<DamageType>);

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line for regular move orders.")]
		public readonly Color TargetLineColor = Color.Green;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while airborne.")]
		public readonly string AirborneCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while at cruise altitude.")]
		public readonly string CruisingCondition = null;

		[Desc("Can the actor hover in place mid-air? If not, then the actor will have to remain in motion (circle around).")]
		public readonly bool CanHover = false;

		[Desc("Does the actor land and take off vertically?")]
		public readonly bool VTOL = false;

		[Desc("Does this VTOL actor need to turn before landing (on terrain)?")]
		public readonly bool TurnToLand = false;

		[Desc("Does this actor automatically take off after resupplying?")]
		public readonly bool TakeOffOnResupply = false;

		[Desc("Does this actor automatically take off after creation?")]
		public readonly bool TakeOffOnCreation = true;

		[Desc("Can this actor be given an explicit land order using the force-move modifier?")]
		public readonly bool CanForceLand = true;

		[Desc("Altitude at which the aircraft considers itself landed.")]
		public readonly WDist LandAltitude = WDist.Zero;

		[Desc("Range to search for an alternative landing location if the ordered cell is blocked.")]
		public readonly WDist LandRange = WDist.FromCells(5);

		[Desc("How fast this actor ascends or descends during horizontal movement.")]
		public readonly WAngle MaximumPitch = WAngle.FromDegrees(10);

		[Desc("How fast this actor ascends or descends when moving vertically only (vertical take off/landing or hovering towards CruiseAltitude).")]
		public readonly WDist AltitudeVelocity = new WDist(43);

		[Desc("Sounds to play when the actor is taking off.")]
		public readonly string[] TakeoffSounds = { };

		[Desc("Sounds to play when the actor is landing.")]
		public readonly string[] LandingSounds = { };

		[Desc("The number of ticks that a airplane will wait to make a new search for an available airport.")]
		public readonly int NumberOfTicksToVerifyAvailableAirport = 150;

		[Desc("Facing to use for actor previews (map editor, color picker, etc)")]
		public readonly WAngle PreviewFacing = new WAngle(384);

		[Desc("Display order for the facing slider in the map editor")]
		public readonly int EditorFacingDisplayOrder = 3;

		/* --- End of Aircraft Variables --- */

		[Desc("If set to true, this unit will always turn in place instead of following a curved trajectory (like infantry).")]
		public readonly bool AlwaysTurnInPlace = false;

		[CursorReference]
		[Desc("Cursor to display when a move order can be issued at target location.")]
		public readonly string Cursor = "move";

		[CursorReference(dictionaryReference: LintDictionaryReference.Values)]
		[Desc("Cursor overrides to display for specific terrain types.",
			"A dictionary of [terrain type]: [cursor name].")]
		public readonly Dictionary<string, string> TerrainCursors = new Dictionary<string, string>();

		[CursorReference]
		[Desc("Cursor to display when a move order cannot be issued at target location.")]
		public readonly string BlockedCursor = "move-blocked";

		[CursorReference]
		[Desc("Cursor to display when able to land at target building.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to land at target building.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition under which the regular (non-force) move cursor is disabled.")]
		public readonly BooleanExpression RequireForceMoveCondition = null;

		[ConsumedConditionReference]
		[Desc("Boolean expression defining the condition under which this actor cannot be nudged by other actors.")]
		public readonly BooleanExpression ImmovableCondition = null;

		IEnumerable<ActorInit> IActorPreviewInitInfo.ActorPreviewInits(ActorInfo ai, ActorPreviewType type)
		{
			yield return new FacingInit(PreviewFacing);
		}

		public Color GetTargetLineColor() { return TargetLineColor; }

		public override object Create(ActorInitializer init) { return new MobileOffGrid(init, this); }

		public LocomotorInfo LocomotorInfo { get; private set; }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var locomotorInfos = rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>();
			LocomotorInfo = locomotorInfos.FirstOrDefault(li => li.Name == Locomotor);
			if (LocomotorInfo == null)
				throw new YamlException($"A locomotor named '{Locomotor}' doesn't exist.");
			else if (locomotorInfos.Count(li => li.Name == Locomotor) > 1)
				throw new YamlException($"There is more than one locomotor named '{Locomotor}'.");

			// We need to reset the reference to the locomotor between each worlds, otherwise we are reference the previous state.
			locomotor = null;

			base.RulesetLoaded(rules, ai);
		}

		public WAngle GetInitialFacing() { return InitialFacing; }

		// initialized and used by CanEnterCell
		Locomotor locomotor;

		/// <summary>
		/// Note: If the target <paramref name="cell"/> has any free subcell, the value of <paramref name="subCell"/> is ignored.
		/// </summary>
		public bool CanEnterCell(World world, Actor self, CPos cell, SubCell subCell = SubCell.FullCell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			// PERF: Avoid repeated trait queries on the hot path
			if (locomotor == null)
				locomotor = world.WorldActor.TraitsImplementing<Locomotor>()
				   .SingleOrDefault(l => l.Info.Name == Locomotor);

			if (locomotor.MovementCostForCell(cell) == short.MaxValue)
				return false;

			return locomotor.CanMoveFreelyInto(self, cell, subCell, check, ignoreActor, true);
		}

		public bool CanStayInCell(World world, CPos cell)
		{
			// PERF: Avoid repeated trait queries on the hot path
			if (locomotor == null)
				locomotor = world.WorldActor.TraitsImplementing<Locomotor>()
				   .SingleOrDefault(l => l.Info.Name == Locomotor);

			if (cell.Layer == CustomMovementLayerType.Tunnel)
				return false;

			return locomotor.CanStayInCell(cell);
		}

		public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any)
		{
			return new Dictionary<CPos, SubCell>() { { location, subCell } };
		}

		bool IOccupySpaceInfo.SharesCell => LocomotorInfo.SharesCell;

		IEnumerable<EditorActorOption> IEditorActorOptions.ActorOptions(ActorInfo ai, World world)
		{
			yield return new EditorActorSlider("Facing", EditorFacingDisplayOrder, 0, 1023, 8,
				actor =>
				{
					var init = actor.GetInitOrDefault<FacingInit>(this);
					return (init?.Value ?? InitialFacing).Angle;
				},
				(actor, value) => actor.ReplaceInit(new FacingInit(new WAngle((int)value))));
		}
	}

	public class MobileOffGrid : PausableConditionalTrait<MobileOffGridInfo>, IOrderVoice, IPositionable, IMove, ITick, ICreationActivity,
		IFacing, IDeathActorInitModifier, IIssueDeployOrder, IIssueOrder, IResolveOrder, INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyBlockingMove, IActorPreviewInitModifier, INotifyBecomingIdle
	{
		public class MvVec
		{
			public WVec Vec;
			public int TickTimer = -1; // How long the movement vector should be applied for. 1 tick = 40 ms. -1 means indefinite.

			public MvVec(WVec newVec) { Vec = newVec; }
			public MvVec(WVec newVec, int tickTimer) { Vec = newVec; TickTimer = tickTimer; }
		}

		readonly Actor self;
		/*readonly Lazy<IEnumerable<int>> speedModifiers;*/

		readonly bool returnToCellOnCreation;
		readonly bool returnToCellOnCreationRecalculateSubCell = true;
		readonly int creationActivityDelay;
		bool notify = true;

		#region IMove CurrentMovementTypes
		MovementType movementTypes;
		public MovementType CurrentMovementTypes
		{
			get => movementTypes;

			set
			{
				var oldValue = movementTypes;
				movementTypes = value;
				if (value != oldValue)
				{
					self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
					foreach (var n in notifyMoving)
						n.MovementTypeChanged(self, value);
				}
			}
		}
		#endregion

		WRot orientation;
		WPos oldPos;
		CPos fromCell, toCell;
		public SubCell FromSubCell, ToSubCell;

		IEnumerable<int> speedModifiers;

		WPos cachedPosition;
		WAngle cachedFacing;

		public WPos CurrPathTarget;
		public WPos LastCompletedTarget;

		public WVec Delta => CurrPathTarget - CenterPosition;
		public List<WPos> PathComplete = new List<WPos>();
		public List<WPos> PositionBuffer = new List<WPos>();
		public int PositionBufferCapacity = 180;
		public int ChangedAngleOffsetsBufferCapacity = 5;
		public List<int> ChangedAngleOffsetsBuffer = new List<int>();
		public static void AddToBuffer<T>(ref List<T> buffer, int bufferCapacity, T item)
		{
			buffer.Add(item);
			if (buffer.Count > bufferCapacity)
				buffer.RemoveAt(0);
		}
		public void AddToPositionBuffer(WPos pos) { AddToBuffer(ref PositionBuffer, PositionBufferCapacity, pos); }
		public void AddToChangedAngleBuffer(int angle) { AddToBuffer(ref ChangedAngleOffsetsBuffer, ChangedAngleOffsetsBufferCapacity, angle); }

		public List<MvVec> SeekVectors = new List<MvVec>();
		public List<MvVec> FleeVectors = new List<MvVec>();

		Repairable repairable;
		Rearmable rearmable;
		IAircraftCenterPositionOffset[] positionOffsets;
		IDisposable reservation;
		INotifyCustomLayerChanged[] notifyCustomLayerChanged;
		INotifyCenterPositionChanged[] notifyCenterPositionChanged;
		INotifyMoving[] notifyMoving;
		INotifyFinishedMoving[] notifyFinishedMoving;
		IOverrideAircraftLanding overrideAircraftLanding;
		IWrapMove[] moveWrappers;
		bool requireForceMove;

		[Sync]
		public WAngle Facing
		{
			get => orientation.Yaw;
			set => orientation = orientation.WithYaw(value);
		}

		public WAngle DesiredFacing = WAngle.Zero;
		public WDist DesiredAltitude = WDist.Zero;
		public WVec DesiredMove = WVec.Zero;
		public bool IgnoreZVec = false;

		public WAngle Pitch
		{
			get => orientation.Pitch;
			set => orientation = orientation.WithPitch(value);
		}

		public WAngle Roll
		{
			get => orientation.Roll;
			set => orientation = orientation.WithRoll(value);
		}

		public WRot Orientation => orientation;

		[Sync]
		public WPos CenterPosition { get; private set; }

		public WDist UnitRadius;
		public WAngle TurnSpeed => IsTraitDisabled || IsTraitPaused ? WAngle.Zero : Info.TurnSpeed;
		public WAngle? IdleTurnSpeed => IsTraitDisabled || IsTraitPaused ? null : Info.IdleTurnSpeed;
		public WAngle GetTurnSpeed(bool isIdleTurn)
		{
			// A MovementSpeed of zero indicates either a speed modifier of zero percent or that the trait is paused or disabled.
			// Bail early in that case.
			if ((isIdleTurn && IdleMovementSpeed == 0) || MovementSpeed == 0)
				return WAngle.Zero;

			var turnSpeed = isIdleTurn ? IdleTurnSpeed ?? TurnSpeed : TurnSpeed;

			return new WAngle(Util.ApplyPercentageModifiers(turnSpeed.Angle, speedModifiers).Clamp(1, 1024));
		}

		public Actor ReservedActor { get; private set; }
		public bool MayYieldReservation { get; private set; }
		public bool ForceLanding { get; private set; }
		public bool AtLandAltitude => self.World.Map.DistanceAboveTerrain(GetPosition()) == LandAltitude;

		/*bool airborne;
		bool cruising;*/
		int airborneToken = Actor.InvalidConditionToken;
		int cruisingToken = Actor.InvalidConditionToken;
		public WDist LandAltitude
		{
			get
			{
				var alt = Info.LandAltitude;
				foreach (var offset in positionOffsets)
					alt -= new WDist(offset.PositionOffset.Z);

				return alt;
			}
		}

		public bool IsImmovable { get; private set; }
		public bool TurnToMove;
		public bool IsBlocking { get; private set; }

		public bool IsMovingBetweenCells => FromCell != ToCell;

		[Sync]
		public CPos FromCell => fromCell;

		[Sync]
		public CPos ToCell => toCell;

		public Locomotor Locomotor { get; private set; }

		public IPathFinder Pathfinder { get; private set; }

		#region IOccupySpace

		public WPos GetPosition()
		{
			var pos = self.CenterPosition;
			foreach (var offset in positionOffsets)
				pos += offset.PositionOffset;

			return pos;
		}

		public CPos TopLeft => ToCell;

		public (CPos, SubCell)[] OccupiedCells()
		{
			if (FromCell == ToCell)
				return new[] { (FromCell, FromSubCell) };

			// HACK: Should be fixed properly, see https://github.com/OpenRA/OpenRA/pull/17292 for an explanation
			if (Info.LocomotorInfo.SharesCell)
				return new[] { (ToCell, ToSubCell) };

			return new[] { (FromCell, FromSubCell), (ToCell, ToSubCell) };
		}
		#endregion

		public MobileOffGrid(ActorInitializer init, MobileOffGridInfo info)
			: base(info)
		{
			self = init.Self;

			ToSubCell = FromSubCell = info.LocomotorInfo.SharesCell ? init.World.Map.Grid.DefaultSubCell : SubCell.FullCell;

			var subCellInit = init.GetOrDefault<SubCellInit>();
			if (subCellInit != null)
			{
				FromSubCell = ToSubCell = subCellInit.Value;
				returnToCellOnCreationRecalculateSubCell = false;
			}

			var locationInit = init.GetOrDefault<LocationInit>();
			if (locationInit != null)
			{
				fromCell = toCell = locationInit.Value;
				SetCenterPosition(self, init.World.Map.CenterOfSubCell(FromCell, FromSubCell));
			}

			Facing = init.GetValue<FacingInit, WAngle>(Info.InitialFacing);

			// Sets the initial center position
			// Unit will move into the cell grid (defined by LocationInit) as its initial activity
			var centerPositionInit = init.GetOrDefault<CenterPositionInit>();
			if (centerPositionInit != null)
			{
				oldPos = centerPositionInit.Value;
				SetCenterPosition(self, oldPos);
				returnToCellOnCreation = true;
			}

			creationActivityDelay = init.GetValue<CreationActivityDelayInit, int>(0);
		}

		protected override void Created(Actor self)
		{
			UnitRadius = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled).FirstOrDefault().Info.Type.OuterRadius;
			notifyCustomLayerChanged = self.TraitsImplementing<INotifyCustomLayerChanged>().ToArray();
			notifyCenterPositionChanged = self.TraitsImplementing<INotifyCenterPositionChanged>().ToArray();
			notifyMoving = self.TraitsImplementing<INotifyMoving>().ToArray();
			notifyFinishedMoving = self.TraitsImplementing<INotifyFinishedMoving>().ToArray();
			moveWrappers = self.TraitsImplementing<IWrapMove>().ToArray();
			Pathfinder = self.World.WorldActor.Trait<IPathFinder>();
			Locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>()
				.Single(l => l.Info.Name == Info.Locomotor);

			// Aircraft Variables
			repairable = self.TraitOrDefault<Repairable>();
			rearmable = self.TraitOrDefault<Rearmable>();
			speedModifiers = self.TraitsImplementing<ISpeedModifier>().ToArray().Select(sm => sm.GetSpeedModifier());
			cachedPosition = self.CenterPosition;
			positionOffsets = self.TraitsImplementing<IAircraftCenterPositionOffset>().ToArray();
			overrideAircraftLanding = self.TraitOrDefault<IOverrideAircraftLanding>();

			base.Created(self);
		}

		void ITick.Tick(Actor self)
		{
			Tick(self);
		}

		public enum WVecTypes { Seek, Flee, All }
		public WVec GenFinalWVec(WVecTypes wvecTypes = WVecTypes.All)
		{
			var finalVec = WVec.Zero;
			if (wvecTypes == WVecTypes.Seek || wvecTypes == WVecTypes.All)
				foreach (var mvVec in SeekVectors)
					finalVec = mvVec.TickTimer != 0 ? finalVec + mvVec.Vec : finalVec;
			if (wvecTypes == WVecTypes.Flee || wvecTypes == WVecTypes.All)
				foreach (var mvVec in FleeVectors)
					finalVec = mvVec.TickTimer != 0 ? finalVec + mvVec.Vec : finalVec;
			return finalVec;
		}
		public WVec GenFinalWVec(List<MvVec> seekVectors, List<MvVec> fleeVectors, WVecTypes wvecTypes = WVecTypes.All)
		{
			var finalVec = WVec.Zero;
			if (wvecTypes == WVecTypes.Seek || wvecTypes == WVecTypes.All)
				foreach (var mvVec in seekVectors)
					finalVec = mvVec.TickTimer != 0 ? finalVec + mvVec.Vec : finalVec;
			if (wvecTypes == WVecTypes.Flee || wvecTypes == WVecTypes.All)
				foreach (var mvVec in fleeVectors)
					finalVec = mvVec.TickTimer != 0 ? finalVec + mvVec.Vec : finalVec;
			return finalVec;
		}

		public void DecrementMoveVectorSet(ref List<MvVec> mvVecList)
		{
			foreach (var mvVec in mvVecList.Where(v => v.TickTimer > 0))
				mvVec.TickTimer--;
			mvVecList.RemoveAll(v => v.TickTimer == 0);
		}

		public void DecrementTickFleeVectors() { DecrementMoveVectorSet(ref FleeVectors); }
		public void DecrementTickSeekVectors() { DecrementMoveVectorSet(ref SeekVectors); }
		public void SetDesiredFacing(WAngle desiredFacing) { DesiredFacing = desiredFacing; }
		public void SetDesiredFacingToFacing() { DesiredFacing = Facing; }
		public void SetDesiredMove(WVec desiredMove) { DesiredMove = desiredMove; }
		public void SetDesiredAltitude(WDist desiredAltitude) { DesiredAltitude = desiredAltitude; }
		public void SetIgnoreZVec(bool ignoreZVec) { IgnoreZVec = ignoreZVec; }
		public WVec RepulsionVecFunc(MobileOffGrid actorMobileOG, WPos selfPos, WPos actorPos, int moveSpeedScalar = 1)
		{
			var repulsionDelta = selfPos - actorPos;
			var distToMove = Math.Min(repulsionDelta.Length, actorMobileOG.MovementSpeed);
			return -new WVec(new WDist(distToMove), WRot.FromYaw(repulsionDelta.Yaw));
		}

		public bool ActorIsAiming(Actor actor)
		{
			if (!actor.IsDead)
			{
				var actorAttacks = actor.TraitsImplementing<AttackTurreted>().Where(Exts.IsTraitEnabled).ToArray();
				if (actorAttacks.Where(at => at.IsAiming).Any())
					return true;
			}
			return false;
		}

		public void RepelNearbyUnitsTick(Actor self)
		{
			var nearbyActorRange = UnitRadius * 2;
			var nearbyActors = self.World.FindActorsInCircle(CenterPosition, nearbyActorRange);

			foreach (var actor in nearbyActors)
			{
				if (!actor.IsDead && actor != self)
				{
					var actorMobileOGs = actor.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled);
					if (actorMobileOGs.Any() && !(actor.CurrentActivity is MobileOffGrid.ReturnToCellActivity))
					{
						var actorMobileOG = actorMobileOGs.FirstOrDefault();
						var repulsionMvVec = new MvVec(RepulsionVecFunc(actorMobileOG, CenterPosition, actorMobileOG.CenterPosition), 1);
						var proposedActorMove = GenFinalWVec(actorMobileOG.SeekVectors,
															 actorMobileOG.FleeVectors.Union(new List<MvVec>() { repulsionMvVec }).ToList());
						if (!CellsCollidingWithPosBool(actor, actor.CenterPosition, proposedActorMove, 3, Locomotor) &&
							!ActorIsAiming(actor)) // we do not repel Attacking actors, we repel against them
							actorMobileOG.FleeVectors.Add(repulsionMvVec);
						else // if collision will occur, we must apply reverse repulsion to ourselves
							FleeVectors.Add(new MvVec(-repulsionMvVec.Vec, 1));
					}
				}
			}
		}

		public void MobileOffGridMoveTick(Actor self)
		{
			var move = DesiredMove == WVec.Zero ? GenFinalWVec() : DesiredMove;
			if (move == WVec.Zero)
				return;

			DecrementTickFleeVectors();
			DecrementTickSeekVectors();

			var dat = self.World.Map.DistanceAboveTerrain(CenterPosition);

			if (IgnoreZVec)
				move = new WVec(move.X, move.Y, 0);

			var oldFacing = Facing;
			var turnSpeed = GetTurnSpeed(false);
			Facing = Util.TickFacing(Facing, (DesiredFacing == WAngle.Zero ? move.Yaw : DesiredFacing), turnSpeed);

			if (Info.Roll != WAngle.Zero)
			{
				var desiredRoll = Facing == DesiredFacing ? WAngle.Zero :
					new WAngle(Info.Roll.Angle * Util.GetTurnDirection(Facing, oldFacing));

				Roll = Util.TickFacing(Roll, desiredRoll, Info.RollSpeed);
			}

			if (Info.Pitch != WAngle.Zero)
				Pitch = Util.TickFacing(Pitch, Info.Pitch, Info.PitchSpeed);

			// Note: we assume that if move.Z is not zero, it's intentional and we want to move in that vertical direction instead of towards desiredAltitude.
			// If that is not desired, the place that calls this should make sure moveOverride.Z is zero.
			if ((DesiredAltitude != WDist.Zero && dat != DesiredAltitude) || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (DesiredAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			SetPosition(self, CenterPosition + move);

			// Reset temporary adjustments
			SetIgnoreZVec(false);
			SetDesiredMove(WVec.Zero);
			SetDesiredFacing(WAngle.Zero);
			SetDesiredAltitude(WDist.Zero);
		}
		private static bool CPosinMap(Actor self, CPos cPos)
		{
			return cPos.X >= 0 && cPos.X <= self.World.Map.MapSize.X - 1 &&
				   cPos.Y >= 0 && cPos.Y <= self.World.Map.MapSize.Y - 1;
		}

		public static bool CellIsBlocked(Actor self, Locomotor locomotor, CPos cell)
		{
			return locomotor.MovementCostToEnterCell(default, cell, BlockedByActor.None, self) == short.MaxValue ||
				CellBlockedByBuilding(self, cell) || !CPosinMap(self, cell);
		}

		public static bool CellBlockedByBuilding(Actor self, CPos cell)
		{
			foreach (var otherActor in self.World.ActorMap.GetActorsAt(cell))
				if (otherActor.OccupiesSpace is Building building && !building.TransitOnlyCells().Contains(cell))
					return true;
			return false;
		}

		public List<CPos> CellsCollidingWithActor(Actor self, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0)
		{ return CellsCollidingWithPos(self, self.CenterPosition, move, lookAhead, locomotor, incOrigin, skipLookAheadAmt); }
		public List<CPos> CellsCollidingWithPos(Actor self, WPos selfPos, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0)
		{
			var cellsColliding = new List<CPos>();
			var selfCenterPos = selfPos.XYToInt2();
			var selfCenterPosWithMoves = new List<int2>();
			var startI = skipLookAheadAmt == 0 ? 0 : skipLookAheadAmt - 1;
			// for each actor we are potentially colliding with
			var selfShapes = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
			foreach (var selfShape in selfShapes)
			{
				var hitShapeCorners = selfShape.Info.Type.GetCorners(selfCenterPos);
				foreach (var corner in hitShapeCorners)
				{
					var cell = self.World.Map.CellContaining(corner);
					for (var i = startI; i < lookAhead; i++)
					{
						var cellToTest = self.World.Map.CellContaining(corner + move * i);
						if (CellIsBlocked(self, locomotor, cellToTest) && !cellsColliding.Contains(cellToTest))
							cellsColliding.Add(cellToTest);
					}
					if (incOrigin && CellIsBlocked(self, locomotor, cell) && !cellsColliding.Contains(cell))
						cellsColliding.Add(cell);
				}
			}
			return cellsColliding;
		}
		public bool CellsCollidingWithPosBool(Actor self, WPos selfPos, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0)
		{
			var cellsColliding = new List<CPos>();
			var selfCenterPos = selfPos.XYToInt2();
			var selfCenterPosWithMoves = new List<int2>();
			var startI = skipLookAheadAmt == 0 ? 0 : skipLookAheadAmt - 1;
			// for each actor we are potentially colliding with
			var selfShapes = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
			foreach (var selfShape in selfShapes)
			{
				var hitShapeCorners = selfShape.Info.Type.GetCorners(selfCenterPos);
				foreach (var corner in hitShapeCorners)
				{
					for (var i = startI; i < lookAhead; i++)
						if (CellIsBlocked(self, locomotor, self.World.Map.CellContaining(corner + move * i)))
							return true;
					if (incOrigin && CellIsBlocked(self, locomotor, self.World.Map.CellContaining(corner)))
						return true;
				}
			}
			return false;
		}

		public static bool ValidCollisionActor(Actor actor)
		{
			return actor.TraitsImplementing<Building>().Any() ||
				   actor.TraitsImplementing<Mobile>().Any() ||
				   actor.TraitsImplementing<MobileOffGrid>().Any();
		}

		public bool HasNotCollided(Actor self, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0, bool attackingUnitsOnly = false)
		{
			if (self.CurrentActivity is ReturnToCellActivity) // we ignore collision if the unit is returning to a cell, e.g. coming out of a factory
				return true;

			var selfCenterPos = self.CenterPosition.XYToInt2();
			var startI = skipLookAheadAmt == 0 ? 0 : skipLookAheadAmt - 1;
			Func<CPos, bool> cellIsBlocked = c => CellIsBlocked(self, locomotor, c);
			Func<int2, HitShape, int2, HitShape, bool> posIsBlocked = (p, selfShape, destP, destShape) =>
				{ return destShape.Info.Type.IntersectsWithHitShape(p, destP, selfShape); };

			var selfShapes = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
			foreach (var selfShape in selfShapes)
			{
				foreach (var corner in selfShape.Info.Type.GetCorners(selfCenterPos))
				{
					/*System.Console.WriteLine($"checking corner {corner} of shape {selfShape.Info.Type}");*/
					var cell = self.World.Map.CellContaining(corner);
					for (var i = startI; i < lookAhead; i++)
					{
						var cornerWithOffset = corner + move * i;
						if ((!incOrigin || cellIsBlocked(cell)) && cellIsBlocked(self.World.Map.CellContaining(cornerWithOffset)))
							return false;

						foreach (var destActor in self.World.FindActorsInCircle(cornerWithOffset, UnitRadius)
															.Where(a => a != self && (!attackingUnitsOnly || ActorIsAiming(a))))
						{
							if (!destActor.IsDead && ValidCollisionActor(destActor))
							{
								//MoveOffGrid.RenderPoint(self, cornerWithOffset, Color.LightGreen);
								var destActorCenter = (destActor.CenterPosition +
														  destActor.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled)
															.FirstOrDefault().GenFinalWVec()); // adding move to use future pos
								if (destActorCenter != WPos.Zero)
								{
									MoveOffGrid.RenderPoint(self, destActorCenter, Color.LightGreen);
									foreach (var destShape in destActor.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled))
										if ((!incOrigin || posIsBlocked(selfCenterPos, selfShape, destActorCenter.XYToInt2(), destShape)) &&
											posIsBlocked(cornerWithOffset.XYToInt2(), selfShape, destActorCenter.XYToInt2(), destShape))
											return false;
								}
							}
						}
					}
				}
			}
			return true;
		}

		public WVec? GetCollisionVector(Actor self, WVec move, int lookAhead, Locomotor locomotor,
											bool incOrigin = false, int skipLookAheadAmt = 0, bool attackingUnitsOnly = false)
		{
			if (self.CurrentActivity is ReturnToCellActivity) // we ignore collision if the unit is returning to a cell, e.g. coming out of a factory
				return null;

			var selfCenterPos = self.CenterPosition.XYToInt2();
			var startI = skipLookAheadAmt == 0 ? 0 : skipLookAheadAmt - 1;
			Func<CPos, bool> cellIsBlocked = c => CellIsBlocked(self, locomotor, c);
			Func<int2, HitShape, int2, HitShape, bool> posIsBlocked = (p, selfShape, destP, destShape) =>
			{ return destShape.Info.Type.IntersectsWithHitShape(p, destP, selfShape); };

			var selfShapes = self.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
			foreach (var selfShape in selfShapes)
			{
				foreach (var corner in selfShape.Info.Type.GetCorners(selfCenterPos))
				{
					/*System.Console.WriteLine($"checking corner {corner} of shape {selfShape.Info.Type}");*/
					var cell = self.World.Map.CellContaining(corner);
					for (var i = startI; i < lookAhead; i++)
					{
						var cornerWithOffset = corner + move * i;
						var cellContainingCornerWithOffset = self.World.Map.CellContaining(cornerWithOffset);
						if ((!incOrigin || cellIsBlocked(cell)) && cellIsBlocked(cellContainingCornerWithOffset))
							return cornerWithOffset - self.World.Map.CenterOfCell(cellContainingCornerWithOffset);

						foreach (var destActor in self.World.FindActorsInCircle(cornerWithOffset, UnitRadius)
															.Where(a => a != self && (!attackingUnitsOnly || ActorIsAiming(a))))
						{
							if (!destActor.IsDead && ValidCollisionActor(destActor))
							{
								//MoveOffGrid.RenderPoint(self, cornerWithOffset, Color.LightGreen);
								var destActorCenter = (destActor.CenterPosition +
														  destActor.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled)
															.FirstOrDefault().GenFinalWVec()); // adding move to use future pos
								MoveOffGrid.RenderPoint(self, destActorCenter, Color.LightGreen);
								foreach (var destShape in destActor.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled))
									if ((!incOrigin || posIsBlocked(selfCenterPos, selfShape, destActorCenter.XYToInt2(), destShape)) &&
										posIsBlocked(cornerWithOffset.XYToInt2(), selfShape, destActorCenter.XYToInt2(), destShape))
										return destActorCenter - cornerWithOffset;
							}
						}
					}
				}
			}
			return null;
		}

		protected virtual void Tick(Actor self)
		{
			var oldCachedFacing = cachedFacing;
			cachedFacing = Facing;

			var oldCachedPosition = cachedPosition;
			cachedPosition = self.CenterPosition;

			var newMovementTypes = MovementType.None;
			if (oldCachedFacing != Facing)
				newMovementTypes |= MovementType.Turn;

			if ((oldCachedPosition - cachedPosition).HorizontalLengthSquared != 0)
				newMovementTypes |= MovementType.Horizontal;

			if ((oldCachedPosition - cachedPosition).VerticalLengthSquared != 0)
				newMovementTypes |= MovementType.Vertical;

			CurrentMovementTypes = newMovementTypes;

			if (!CurrentMovementTypes.HasMovementType(MovementType.Horizontal))
			{
				if (Info.Roll != WAngle.Zero && Roll != WAngle.Zero)
					Roll = Util.TickFacing(Roll, WAngle.Zero, Info.RollSpeed);

				if (Info.Pitch != WAngle.Zero && Pitch != WAngle.Zero)
					Pitch = Util.TickFacing(Pitch, WAngle.Zero, Info.PitchSpeed);
			}

			// Update unit position
			MobileOffGridMoveTick(self);
			RepelNearbyUnitsTick(self);

			// Update unit's cell as it moves
			var cell = self.World.Map.CellContaining(CenterPosition);
			SetLocation(cell, SubCell.FullCell, cell, SubCell.FullCell);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			self.World.AddToMaps(self, this);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.RemoveFromMaps(self, this);
		}

		protected override void TraitEnabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		protected override void TraitDisabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		protected override void TraitResumed(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		protected override void TraitPaused(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		#region Local misc stuff

		public void Nudge(Actor nudger)
		{
			if (IsTraitDisabled || IsTraitPaused || IsImmovable)
				return;

			var cell = GetAdjacentCell(nudger.Location);
			if (cell != null)
				self.QueueActivity(false, MoveTo(cell.Value, 0));
		}

		public CPos? GetAdjacentCell(CPos nextCell, Func<CPos, bool> preferToAvoid = null)
		{
			var availCells = new List<CPos>();
			var notStupidCells = new List<CPos>();
			foreach (CVec direction in CVec.Directions)
			{
				var p = ToCell + direction;
				if (CanEnterCell(p) && CanStayInCell(p) && (preferToAvoid == null || !preferToAvoid(p)))
					availCells.Add(p);
				else if (p != nextCell && p != ToCell)
					notStupidCells.Add(p);
			}

			CPos? newCell = null;
			if (availCells.Count > 0)
				newCell = availCells.Random(self.World.SharedRandom);
			else
			{
				var cellInfo = notStupidCells
					.SelectMany(c => self.World.ActorMap.GetActorsAt(c).Where(IsMovable),
						(c, a) => new { Cell = c, Actor = a })
					.RandomOrDefault(self.World.SharedRandom);
				if (cellInfo != null)
					newCell = cellInfo.Cell;
			}

			return newCell;
		}

		static bool IsMovable(Actor otherActor)
		{
			if (!otherActor.IsIdle)
				return false;

			var mobile = otherActor.TraitOrDefault<Mobile>();
			if (mobile == null || mobile.IsTraitDisabled || mobile.IsTraitPaused || mobile.IsImmovable)
				return false;

			return true;
		}

		public bool CanInteractWithGroundLayer(Actor self)
		{
			// TODO: Think about extending this to support arbitrary layer-layer checks
			// in a way that is compatible with the other IMove types.
			// This would then allow us to e.g. have units attack other units inside tunnels.
			if (ToCell.Layer == 0)
				return true;
			
			var layer = self.World.GetCustomMovementLayers()[ToCell.Layer];
			return layer == null || layer.InteractsWithDefaultLayer;
		}

		#endregion

		#region IPositionable

		// Returns a valid sub-cell
		public SubCell GetValidSubCell(SubCell preferred = SubCell.Any)
		{
			// Try same sub-cell
			if (preferred == SubCell.Any)
				preferred = FromSubCell;

			// Fix sub-cell assignment
			if (Info.LocomotorInfo.SharesCell)
			{
				if (preferred <= SubCell.FullCell)
					return self.World.Map.Grid.DefaultSubCell;
			}
			else
			{
				if (preferred != SubCell.FullCell)
					return SubCell.FullCell;
			}

			return preferred;
		}

		// Sets the location (fromCell, toCell, FromSubCell, ToSubCell) and CenterPosition
		public void SetPosition(Actor self, CPos cell, SubCell subCell = SubCell.Any)
		{
			subCell = GetValidSubCell(subCell);
			SetLocation(cell, subCell, cell, subCell);

			var position = cell.Layer == 0 ? self.World.Map.CenterOfCell(cell) :
				self.World.GetCustomMovementLayers()[cell.Layer].CenterOfCell(cell);

			var subcellOffset = self.World.Map.Grid.OffsetOfSubCell(subCell);
			SetCenterPosition(self, position + subcellOffset);
			FinishedMoving(self);
		}

		public void SetPosition(Actor self, WPos pos)
		{
			CenterPosition = pos;

			if (!self.IsInWorld)
				return;

			self.World.UpdateMaps(self, this);

			// NB: This can be called from the constructor before notifyCenterPositionChanged is assigned.
			if (notify && notifyCenterPositionChanged != null)
				foreach (var n in notifyCenterPositionChanged)
					n.CenterPositionChanged(self, 0, 0);

			FinishedMoving(self);
		}

		// Sets only the CenterPosition
		public void SetCenterPosition(Actor self, WPos pos)
		{
			CenterPosition = pos;
			self.World.UpdateMaps(self, this);

			// The first time SetCenterPosition is called is in the constructor before creation, so we need a null check here as well
			if (notifyCenterPositionChanged == null)
				return;

			foreach (var n in notifyCenterPositionChanged)
				n.CenterPositionChanged(self, fromCell.Layer, toCell.Layer);
		}

		public bool IsLeavingCell(CPos location, SubCell subCell = SubCell.Any)
		{
			return ToCell != location && fromCell == location
				&& (subCell == SubCell.Any || FromSubCell == subCell || subCell == SubCell.FullCell || FromSubCell == SubCell.FullCell);
		}

		public SubCell GetAvailableSubCell(CPos a, SubCell preferredSubCell = SubCell.Any, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			return Locomotor.GetAvailableSubCell(self, a, check, preferredSubCell, ignoreActor);
		}

		public bool CanExistInCell(CPos cell)
		{
			return Locomotor.MovementCostForCell(cell) != short.MaxValue;
		}

		public bool CanEnterCell(CPos cell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			return Info.CanEnterCell(self.World, self, cell, ToSubCell, ignoreActor, check);
		}

		public bool CanStayInCell(CPos cell)
		{
			return Info.CanStayInCell(self.World, cell);
		}

		#endregion

		#region Local IPositionable-related

		// Sets only the location (fromCell, toCell, FromSubCell, ToSubCell)
		public void SetLocation(CPos from, SubCell fromSub, CPos to, SubCell toSub)
		{
			if (FromCell == from && ToCell == to && FromSubCell == fromSub && ToSubCell == toSub)
				return;

			RemoveInfluence();
			fromCell = from;
			toCell = to;
			FromSubCell = fromSub;
			ToSubCell = toSub;
			AddInfluence();
			IsBlocking = false;

			// Most custom layer conditions are added/removed when starting the transition between layers.
			if (toCell.Layer != fromCell.Layer)
				foreach (var n in notifyCustomLayerChanged)
					n.CustomLayerChanged(self, fromCell.Layer, toCell.Layer);
		}

		public void FinishedMoving(Actor self)
		{
			// Need to check both fromCell and toCell because FinishedMoving is called multiple times during the move
			if (fromCell.Layer == toCell.Layer)
				foreach (var n in notifyFinishedMoving)
					n.FinishedMoving(self, fromCell.Layer, toCell.Layer);

			// Only make actor crush if it is on the ground
			if (!self.IsAtGroundLevel())
				return;

			var actors = self.World.ActorMap.GetActorsAt(ToCell, ToSubCell).Where(a => a != self).ToList();
			if (!AnyCrushables(actors))
				return;

			var notifiers = actors.SelectMany(a => a.TraitsImplementing<INotifyCrushed>().Select(t => new TraitPair<INotifyCrushed>(a, t)));
			foreach (var notifyCrushed in notifiers)
				notifyCrushed.Trait.OnCrush(notifyCrushed.Actor, self, Info.LocomotorInfo.Crushes);
		}

		bool AnyCrushables(List<Actor> actors)
		{
			var crushables = actors.SelectMany(a => a.TraitsImplementing<ICrushable>().Select(t => new TraitPair<ICrushable>(a, t))).ToList();
			if (crushables.Count == 0)
				return false;

			foreach (var crushes in crushables)
				if (crushes.Trait.CrushableBy(crushes.Actor, self, Info.LocomotorInfo.Crushes))
					return true;

			return false;
		}

		public void AddInfluence()
		{
			if (self.IsInWorld)
				self.World.ActorMap.AddInfluence(self, this);
		}

		public void RemoveInfluence()
		{
			if (self.IsInWorld)
				self.World.ActorMap.RemoveInfluence(self, this);
		}

		#endregion

		#region IMove

		Activity WrapMove(Activity inner)
		{
			var moveWrapper = moveWrappers.FirstOrDefault(Exts.IsTraitEnabled);
			if (moveWrapper != null)
				return moveWrapper.WrapMove(inner);

			return inner;
		}

		public Activity MoveTo(CPos cell, int nearEnough = 0, Actor ignoreActor = null,
			bool evaluateNearestMovableCell = false, Color? targetLineColor = null)
		{
			/*return WrapMove(new Move(self, cell, WDist.FromCells(nearEnough), ignoreActor, evaluateNearestMovableCell, targetLineColor));*/
			return new MoveOffGrid(self, Target.FromCell(self.World, cell), WDist.FromCells(nearEnough), targetLineColor: targetLineColor);
		}

		public Activity MoveWithinRange(in Target target, WDist range,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			var groupedActors = self.World.Selection.Actors
									.Where(a => !a.IsDead)
									.Where(a => a.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).Any()).ToList();
			return new MoveOffGrid(self, groupedActors, target, WDist.Zero, range, initialTargetPosition, targetLineColor);
		}

		public Activity MoveWithinRange(in Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			var groupedActors = self.World.Selection.Actors
									.Where(a => !a.IsDead)
									.Where(a => a.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).Any()).ToList();
			return new MoveOffGrid(self, groupedActors, target, minRange, maxRange, initialTargetPosition, targetLineColor);
		}

		public Activity MoveFollow(Actor self, in Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			return new MoveOffGridFollow(self, target, minRange, maxRange,
				initialTargetPosition, targetLineColor);
		}

		public Activity ReturnToCell(Actor self)
		{
			return new ReturnToCellActivity(self);
		}

		public class ReturnToCellActivity : Activity
		{
			readonly MobileOffGrid mobileOffGrid;
			readonly bool recalculateSubCell;

			CPos cell;
			SubCell subCell;
			WPos pos;
			int delay;

			public ReturnToCellActivity(Actor self, int delay = 0, bool recalculateSubCell = false)
			{
				mobileOffGrid = self.Trait<MobileOffGrid>();
				IsInterruptible = false;
				this.delay = delay;
				this.recalculateSubCell = recalculateSubCell;
			}

			protected override void OnFirstRun(Actor self)
			{
				System.Console.WriteLine("MobileOffGrid.OnFirstRun() issued at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
				pos = self.CenterPosition;
				if (self.World.Map.DistanceAboveTerrain(pos) > WDist.Zero && self.TraitOrDefault<Parachutable>() != null)
					QueueChild(new Parachute(self));
			}

			public override bool Tick(Actor self)
			{
				pos = self.CenterPosition;
				cell = mobileOffGrid.ToCell;
				subCell = mobileOffGrid.ToSubCell;

				if (recalculateSubCell)
					subCell = mobileOffGrid.Info.LocomotorInfo.SharesCell ? self.World.ActorMap.FreeSubCell(cell, subCell, a => a != self) : SubCell.FullCell;

				// TODO: solve/reduce cell is full problem
				if (subCell == SubCell.Invalid)
					subCell = self.World.Map.Grid.DefaultSubCell;

				// Reserve the exit cell
				mobileOffGrid.SetPosition(self, cell, subCell);
				mobileOffGrid.SetCenterPosition(self, pos);

				if (delay > 0)
					QueueChild(new Wait(delay));

				QueueChild(mobileOffGrid.LocalMove(self, pos, self.World.Map.CenterOfSubCell(cell, subCell)));
				return true;
			}
		}

		public Activity MoveToTarget(Actor self, in Target target,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			if (target.Type == TargetType.Invalid)
				return null;

			return new MoveOffGrid(self, target, initialTargetPosition, targetLineColor);
		}

		public Activity MoveIntoTarget(Actor self, in Target target)
		{
			if (target.Type == TargetType.Invalid)
				return null;

			// Activity cancels if the target moves by more than half a cell
			// to avoid problems with the cell grid
			return WrapMove(new LocalMoveIntoTarget(self, target, new WDist(512)));
		}

		public Activity LocalMove(Actor self, WPos fromPos, WPos toPos)
		{
			/*return WrapMove(LocalMove(self, fromPos, toPos, self.Location));*/
			var activities = new CallFunc(() => SetCenterPosition(self, fromPos));
			activities.Queue(new MoveOffGrid(self, Target.FromPos(toPos)));
			return activities;
		}

		public int EstimatedMoveDuration(Actor self, WPos fromPos, WPos toPos)
		{
			var speed = MovementSpeedForCell(self, self.Location);
			return speed > 0 ? (toPos - fromPos).Length / speed : 0;
		}

		public CPos NearestMoveableCell(CPos target)
		{
			// Limit search to a radius of 10 tiles
			return NearestMoveableCell(target, 1, 10);
		}

		public bool CanEnterTargetNow(Actor self, in Target target)
		{
			if (target.Type == TargetType.FrozenActor && !target.FrozenActor.IsValid)
				return false;

			return self.Location == self.World.Map.CellContaining(target.CenterPosition) || Util.AdjacentCells(self.World, target).Any(c => c == self.Location);
		}

		#endregion

		#region Local IMove-related

		public int MovementSpeedForCell(Actor self, CPos cell)
		{
			var terrainSpeed = Locomotor.MovementSpeedForCell(cell);
			var modifiers = speedModifiers.Append(terrainSpeed);

			return Util.ApplyPercentageModifiers(Info.Speed, modifiers);
		}

		public int MovementSpeedForWPos(Actor self, WPos pos)
		{
			// NOTE: Must add terrain modifiers without using cell
			var terrainSpeed = Locomotor.MovementSpeedForCell(self.World.Map.CellContaining(pos));
			return Info.Speed;
		}

		public int MovementSpeed => !IsTraitDisabled && !IsTraitPaused ? Util.ApplyPercentageModifiers(Info.Speed, speedModifiers) : 0;
		public int IdleMovementSpeed => Info.IdleSpeed < 0 ? MovementSpeed :
			!IsTraitDisabled && !IsTraitPaused ? Util.ApplyPercentageModifiers(Info.IdleSpeed, speedModifiers) : 0;

		public CPos NearestMoveableCell(CPos target, int minRange, int maxRange)
		{
			// HACK: This entire method is a hack, and needs to be replaced with
			// a proper path search that can account for movement layer transitions.
			// HACK: Work around code that blindly tries to move to cells in invalid movement layers.
			// This will need to change (by removing this method completely as above) before we can
			// properly support user-issued orders on to elevated bridges or other interactable custom layers
			if (target.Layer != 0)
				target = new CPos(target.X, target.Y);

			if (target == self.Location && CanStayInCell(target))
				return target;

			if (CanEnterCell(target, check: BlockedByActor.Immovable) && CanStayInCell(target))
				return target;

			foreach (var tile in self.World.Map.FindTilesInAnnulus(target, minRange, maxRange))
				if (CanEnterCell(tile, check: BlockedByActor.Immovable) && CanStayInCell(tile))
					return tile;

			// Couldn't find a cell
			return target;
		}

		public CPos NearestCell(CPos target, Func<CPos, bool> check, int minRange, int maxRange)
		{
			if (check(target))
				return target;

			foreach (var tile in self.World.Map.FindTilesInAnnulus(target, minRange, maxRange))
				if (check(tile))
					return tile;

			// Couldn't find a cell
			return target;
		}

		public void EnteringCell(Actor self)
		{
			// Only make actor crush if it is on the ground
			if (!self.IsAtGroundLevel())
				return;

			var actors = self.World.ActorMap.GetActorsAt(ToCell).Where(a => a != self).ToList();
			if (!AnyCrushables(actors))
				return;

			var notifiers = actors.SelectMany(a => a.TraitsImplementing<INotifyCrushed>().Select(t => new TraitPair<INotifyCrushed>(a, t)));
			foreach (var notifyCrushed in notifiers)
				notifyCrushed.Trait.WarnCrush(notifyCrushed.Actor, self, Info.LocomotorInfo.Crushes);
		}

		public Activity ScriptedMove(CPos cell) { return new Move(self, cell); }
		public Activity MoveTo(Func<BlockedByActor, List<CPos>> pathFunc) { return new Move(self, pathFunc); }

		Activity LocalMove(Actor self, WPos fromPos, WPos toPos, CPos cell)
		{
			var speed = MovementSpeedForCell(self, cell);
			var length = speed > 0 ? (toPos - fromPos).Length / speed : 0;

			var delta = toPos - fromPos;
			var facing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : Facing;

			return new Drag(self, fromPos, toPos, length, facing);
		}
		#endregion

		void IActorPreviewInitModifier.ModifyActorPreviewInit(Actor self, TypeDictionary inits)
		{
			if (!inits.Contains<DynamicFacingInit>() && !inits.Contains<FacingInit>())
				inits.Add(new DynamicFacingInit(() => Facing));
		}

		void IDeathActorInitModifier.ModifyDeathActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new FacingInit(Facing));

			// Allows the husk to drag to its final position
			if (CanEnterCell(self.Location, self, BlockedByActor.Stationary))
				init.Add(new HuskSpeedInit(MovementSpeedForCell(self, self.Location)));
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			OnBecomingIdle(self);
		}

		protected virtual void OnBecomingIdle(Actor self)
		{
		}

		void INotifyBlockingMove.OnNotifyBlockingMove(Actor self, Actor blocking)
		{
			if (!self.AppearsFriendlyTo(blocking))
				return;

			if (self.IsIdle)
			{
				Nudge(blocking);
				return;
			}

			IsBlocking = true;
		}

		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var observer in base.GetVariableObservers())
				yield return observer;

			if (Info.RequireForceMoveCondition != null)
				yield return new VariableObserver(RequireForceMoveConditionChanged, Info.RequireForceMoveCondition.Variables);

			if (Info.ImmovableCondition != null)
				yield return new VariableObserver(ImmovableConditionChanged, Info.ImmovableCondition.Variables);
		}

		void RequireForceMoveConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			requireForceMove = Info.RequireForceMoveCondition.Evaluate(conditions);
		}

		void ImmovableConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			var wasImmovable = IsImmovable;
			IsImmovable = Info.ImmovableCondition.Evaluate(conditions);
			if (wasImmovable != IsImmovable)
				self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<BuildingInfo>(
					"ForceEnter",
					6,
					Info.EnterCursor,
					Info.EnterBlockedCursor,
					(target, modifiers) => Info.CanForceLand && modifiers.HasModifier(TargetModifiers.ForceMove) && AircraftCanEnter(target),
					target => Reservable.IsAvailableFor(target, self) && AircraftCanResupplyAt(target, true));

				yield return new EnterAlliedActorTargeter<BuildingInfo>(
					"Enter",
					5,
					Info.EnterCursor,
					Info.EnterBlockedCursor,
					AircraftCanEnter,
					target => Reservable.IsAvailableFor(target, self) && AircraftCanResupplyAt(target, !Info.TakeOffOnResupply));

				yield return new MobileOffGridMoveOrderTargeter(this);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (!IsTraitDisabled &&
				(order.OrderID == "Enter" || order.OrderID == "Move" || order.OrderID == "Land" || order.OrderID == "ForceEnter"))
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (IsTraitDisabled)
				return;

			var orderString = order.OrderString;
			if (orderString == "Move")
			{
				if (!Info.MoveIntoShroud && !self.Owner.Shroud.IsExplored(order.Target.CenterPosition))
					return;

				var target = Target.FromPos(order.Target.CenterPosition);
				self.QueueActivity(order.Queued, new MoveOffGrid(self, order.GroupedActors.ToList(), target, targetLineColor: Info.TargetLineColor));
				#if DEBUG
				System.Console.WriteLine("ResolveOrder() with 'Move' to (" + order.Target.CenterPosition.X.ToString() + "," + order.Target.CenterPosition.Y.ToString() + ") called at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
				#endif
				self.ShowTargetLines();
			}
			else if (orderString == "Land")
			{
				var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
				if (!Info.MoveIntoShroud && !self.Owner.Shroud.IsExplored(cell))
					return;

				if (!order.Queued)
					UnReserve();

				var target = Target.FromCell(self.World, cell);

				self.QueueActivity(order.Queued, new Land(self, target, targetLineColor: Info.TargetLineColor));
				self.ShowTargetLines();
			}
			else if (orderString == "Enter" || orderString == "ForceEnter" || orderString == "Repair")
			{
				// Enter, ForceEnter and Repair orders are only valid for own/allied actors,
				// which are guaranteed to never be frozen.
				if (order.Target.Type != TargetType.Actor)
					return;

				var targetActor = order.Target.Actor;
				var isForceEnter = orderString == "ForceEnter";
				var canResupplyAt = AircraftCanResupplyAt(targetActor, isForceEnter || !Info.TakeOffOnResupply);

				// This is what the order targeter checks to display the correct cursor, so we need to make sure
				// the behavior matches the cursor if the player clicks despite a "blocked" cursor.
				if (!canResupplyAt || !Reservable.IsAvailableFor(targetActor, self))
					return;

				if (!order.Queued)
					UnReserve();

				// Aircraft with TakeOffOnResupply would immediately take off again, so there's no point in automatically forcing
				// them to land on a resupplier. For aircraft without it, it makes more sense to land than to idle above a
				// free resupplier.
				var forceLand = isForceEnter || !Info.TakeOffOnResupply;
				self.QueueActivity(order.Queued, new ReturnToBase(self, targetActor, forceLand));
				self.ShowTargetLines();
			}
			else if (orderString == "Stop")
			{
				// We don't want the Stop order to cancel a running Resupply activity.
				// Resupply is always either the main activity or a child of ReturnToBase.
				if (self.CurrentActivity is Resupply ||
					(self.CurrentActivity is ReturnToBase))
					return;

				self.CancelActivity();
				UnReserve();
				#if DEBUG
				System.Console.WriteLine("ResolveOrder() with 'Stop' called at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
				#endif
			}
			else if (orderString == "ReturnToBase")
			{
				// Do nothing if not rearmable and don't restart activity every time deploy hotkey is triggered
				if (rearmable == null || !rearmable.Info.RearmActors.Any() || self.CurrentActivity is ReturnToBase)
					return;

				if (!order.Queued)
					UnReserve();

				// Aircraft with TakeOffOnResupply would immediately take off again, so there's no point in forcing them to land
				// on a resupplier. For aircraft without it, it makes more sense to land than to idle above a free resupplier.
				self.QueueActivity(order.Queued, new ReturnToBase(self, null, !Info.TakeOffOnResupply));
				self.ShowTargetLines();
			}
			else if (orderString == "Scatter")
				Nudge(self);
		}

		public void UnReserve()
		{
			if (reservation == null)
				return;

			reservation.Dispose();
			reservation = null;
			ReservedActor = null;
			MayYieldReservation = false;
		}

		bool AircraftCanEnter(Actor a, TargetModifiers modifiers)
		{
			if (requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
				return false;

			return AircraftCanEnter(a);
		}

		bool AircraftCanEnter(Actor a)
		{
			if (self.AppearsHostileTo(a))
				return false;

			var canRearmAtActor = rearmable != null && rearmable.Info.RearmActors.Contains(a.Info.Name);
			var canRepairAtActor = repairable != null && repairable.Info.RepairActors.Contains(a.Info.Name);

			return canRearmAtActor || canRepairAtActor;
		}

		bool AircraftCanResupplyAt(Actor a, bool allowedToForceEnter = false)
		{
			if (self.AppearsHostileTo(a))
				return false;

			var canRearmAtActor = rearmable != null && rearmable.Info.RearmActors.Contains(a.Info.Name);
			var canRepairAtActor = repairable != null && repairable.Info.RepairActors.Contains(a.Info.Name);

			var allowedToEnterRearmer = canRearmAtActor && (allowedToForceEnter || rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo));
			var allowedToEnterRepairer = canRepairAtActor && (allowedToForceEnter || self.GetDamageState() != DamageState.Undamaged);

			return allowedToEnterRearmer || allowedToEnterRepairer;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			if (IsTraitDisabled || rearmable == null || !rearmable.Info.RearmActors.Any())
				return null;

			return new Order("ReturnToBase", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return rearmable != null && rearmable.Info.RearmActors.Any(); }

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (IsTraitDisabled)
				return null;

			switch (order.OrderString)
			{
				case "Move":
					if (!Info.LocomotorInfo.MoveIntoShroud && order.Target.Type != TargetType.Invalid)
					{
						var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
						if (!self.Owner.Shroud.IsExplored(cell))
							return null;
					}

					return Info.Voice;
				case "Scatter":
				case "Stop":
					return Info.Voice;
				default:
					return null;
			}
		}

		Activity ICreationActivity.GetCreationActivity()
		{
			return returnToCellOnCreation ? new ReturnToCellActivity(self, creationActivityDelay, returnToCellOnCreationRecalculateSubCell) : null;
		}

		public Activity MoveOntoTarget(Actor self, in Target target, in WVec offset, WAngle? facing, Color? targetLineColor = null)
		{
			throw new NotImplementedException();
		}

		public class MobileOffGridMoveOrderTargeter : IOrderTargeter
		{
			readonly MobileOffGrid mobileOffGrid;

			public string OrderID { get; protected set; }
			public int OrderPriority => 4;
			public bool IsQueued { get; protected set; }

			public MobileOffGridMoveOrderTargeter(MobileOffGrid mobileOffGrid)
			{
				this.mobileOffGrid = mobileOffGrid;
				OrderID = "Move";
			}

			public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers)
			{
				// Always prioritise orders over selecting other peoples actors or own actors that are already selected
				if (target.Type == TargetType.Actor && (target.Actor.Owner != self.Owner || self.World.Selection.Contains(target.Actor)))
					return true;

				return modifiers.HasModifier(TargetModifiers.ForceMove);
			}

			public virtual bool CanTarget(Actor self, in Target target, ref TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain || (mobileOffGrid.requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove)))
					return false;

				var location = self.World.Map.CellContaining(target.CenterPosition);

				// Aircraft can be force-landed by issuing a force-move order on a clear terrain cell
				// Cells that contain a blocking building are treated as regular force move orders, overriding
				// selection for left-mouse orders
				if (modifiers.HasModifier(TargetModifiers.ForceMove) && mobileOffGrid.Info.CanForceLand)
				{
					var buildingAtLocation = self.World.ActorMap.GetActorsAt(location)
						.Any(a => a.TraitOrDefault<Building>() != null && a.TraitOrDefault<Selectable>() != null);
				}

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				var explored = self.Owner.Shroud.IsExplored(location);
				cursor = !mobileOffGrid.IsTraitPaused && (explored || mobileOffGrid.Info.MoveIntoShroud) && self.World.Map.Contains(location) ?
					mobileOffGrid.Info.Cursor : mobileOffGrid.Info.BlockedCursor;

				return true;
			}
		}
	}
}
