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
using System.Diagnostics.Metrics;
using System.Linq;
using Linguini.Syntax.Ast;
using Microsoft.VisualBasic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Pathfinder;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using TagLib.Riff;
using OpenRA.Mods.Common.HitShapes;
using static OpenRA.Mods.Common.Traits.MobileOffGrid;
using RVO;



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
		readonly bool notify = true;

		public enum MovementState { Undefined, Blocked, Seeking, Starting, Stopped, Repathing, FinishedTarget, Ending, FailedStuck, FailedStuckButNotLastTarget }

		public MovementState CurrMovementState = MovementState.Undefined;

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

		public Color DebugColor = Color.RandomColor();
		WPos cachedPosition;
		WAngle cachedFacing;

		public AgentPreset DefaultAgentPreset = new
			(
			neighborDist: 15.0f * 150,
			maxNeighbors: 10 * 150,
			timeHorizon: 5.0f * 150,
			timeHorizonObst: 5.0f * 150,
			radius: 2.0f * 150,
			maxSpeed: 2.0f * 150,
			velocity: new Vector2(0.0f, 0.0f));

		public WPos CurrPathTarget;
		public WPos LastPathTarget;
		public WPos LastCompletedTarget;
		public ThetaStarPathSearch CurrThetaSearch;
		public bool SearchingForNextTarget = false;
		public bool IsBlocked = false;
		bool useLocalAvoidance = true; // determines whether the local avoidance algorithm should be used to avoid nearby units
		public List<WPos> PathComplete = new();
		public List<WPos> PositionBuffer = new();
		public int PositionBufferCapacity = 20;
		public int TraversedCirclesBufferCapacity = 10;
		public WDist LocalAvoidanceDist => UnitRadius * 2;
		public List<WPos> TraversedCirclesBuffer = new();

		public List<BlockedByCell> BlockedByCells = new();

		public enum BlockedByCell { TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }
		public static void AddToBuffer<T>(ref List<T> buffer, int bufferCapacity, T item)
		{
			buffer.Add(item);
			if (buffer.Count > bufferCapacity)
				buffer.RemoveAt(0);
		}
		public void AddToPositionBuffer(WPos pos) { AddToBuffer(ref PositionBuffer, PositionBufferCapacity, pos); }
		public void AddToTraversedCirclesBuffer(WPos pos)
		{
			// We divide UnitRadius by 2 because we allow some overlap, to ensure the space between circles is not skipped.
			if (!TraversedCirclesBuffer.Any(c => CircleShape.PosIsInsideCircle(c, UnitRadius.Length / 2, pos)))
				AddToBuffer(ref TraversedCirclesBuffer, TraversedCirclesBufferCapacity, pos);
		}

		public List<MvVec> SeekVectors = new();
		public List<MvVec> FleeVectors = new();

		public MobileOffGridOverlay Overlay;
		WVec Delta => CurrPathTarget - CenterPosition;
		WVec pastMoveVec;
		int currLocalAvoidanceAngleOffset = 0;
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
		public WDist ForcedAltitude = WDist.Zero;
		public WVec ForcedMove = WVec.Zero;
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

		public IHitShape UnitHitShape;

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
			UnitHitShape = self.TraitsImplementing<HitShape>().FirstOrDefault(Exts.IsTraitEnabled).Info.Type;
			UnitRadius = UnitHitShape.OuterRadius;
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

		public void AddUnique(WVecTypes wvecTypes, MvVec newMvVec) => AddUnique(SeekVectors, FleeVectors, wvecTypes, newMvVec);

		public void AddUnique(List<MvVec> seekVectors, List<MvVec> fleeVectors, WVecTypes wvecTypes, MvVec newMvVec)
		{
			if (wvecTypes == WVecTypes.Seek && !seekVectors.Any(v => v.Vec == newMvVec.Vec))
				seekVectors.Add(newMvVec);
			if (wvecTypes == WVecTypes.Flee && !fleeVectors.Any(v => v.Vec == newMvVec.Vec))
				fleeVectors.Add(newMvVec);
		}

		public enum WVecTypes { Seek, Flee, All }
		public WVec GenFinalWVec(WVecTypes wvecTypes = WVecTypes.All, bool ignoreTimer = false, bool capAtMoveSpeed = true)
			=> GenFinalWVec(SeekVectors, FleeVectors, new List<WVec>(), wvecTypes, ignoreTimer, capAtMoveSpeed);

		public WVec GenFinalWVec(WVec testVec, WVecTypes wvecTypes = WVecTypes.All, bool ignoreTimer = false, bool capAtMoveSpeed = true)
			=> GenFinalWVec(SeekVectors, FleeVectors, new List<WVec> { testVec }, wvecTypes, ignoreTimer, capAtMoveSpeed);

		public WVec GenFinalWVec(List<WVec> testVecs, WVecTypes wvecTypes = WVecTypes.All, bool ignoreTimer = false, bool capAtMoveSpeed = true)
			=> GenFinalWVec(SeekVectors, FleeVectors, testVecs, wvecTypes, ignoreTimer, capAtMoveSpeed);

		public WVec GenFinalWVec(List<MvVec> seekVectors, List<MvVec> fleeVectors, WVecTypes wvecTypes = WVecTypes.All,
			bool ignoreTimer = false, bool capAtMoveSpeed = true)
			=> GenFinalWVec(SeekVectors, FleeVectors, new List<WVec>(), wvecTypes, ignoreTimer, capAtMoveSpeed);

		public WVec GenFinalWVec(List<MvVec> seekVectors, List<MvVec> fleeVectors, List<WVec> testVecs, WVecTypes wvecTypes = WVecTypes.All,
			bool ignoreTimer = false, bool capAtMoveSpeed = true)
		{
			var finalVec = WVec.Zero;

			if (wvecTypes == WVecTypes.Seek || wvecTypes == WVecTypes.All)
				foreach (var mvVec in seekVectors)
					finalVec = mvVec.TickTimer != 0 || ignoreTimer ? finalVec + mvVec.Vec : finalVec;
			if (wvecTypes == WVecTypes.Flee || wvecTypes == WVecTypes.All)
				foreach (var mvVec in fleeVectors)
					finalVec = mvVec.TickTimer != 0 || ignoreTimer ? finalVec + mvVec.Vec : finalVec;

			foreach (var vec in testVecs)
				finalVec += vec;

			if (capAtMoveSpeed)
				finalVec = new WVec(new WDist(Math.Min(MovementSpeed, finalVec.Length)), WRot.FromYaw(finalVec.Yaw));

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
		public void SetForcedFacing(WAngle desiredFacing) { DesiredFacing = desiredFacing; }
		public void SetDesiredFacingToFacing() { DesiredFacing = Facing; }
		public void SetForcedMove(WVec desiredMove) { ForcedMove = desiredMove; }
		public void SetForcedAltitude(WDist desiredAltitude) { ForcedAltitude = desiredAltitude; }
		public void SetIgnoreZVec(bool ignoreZVec) { IgnoreZVec = ignoreZVec; }
		public WVec RepulsionVecFunc(int repellingMovementSpeed, WPos selfPos, WPos nearbyActorPos, int moveSpeedScalar = 1)
		{
			var repulsionDelta = nearbyActorPos - selfPos;
			var distToMove = Math.Min(repulsionDelta.Length, repellingMovementSpeed * moveSpeedScalar);
			return -new WVec(new WDist(distToMove), WRot.FromYaw(repulsionDelta.Yaw));
		}

		public bool ActorIsAiming(Actor actor)
		{
			if (!actor.IsDead)
				return actor.TraitsImplementing<AttackTurreted>().Where(Exts.IsTraitEnabled).Any(at => at.IsAiming);
			return false;
		}

		public void CreateRepelNearbyUnitsVectorsTick(Actor self)
		{
			var nearbyActorRange = UnitRadius * 2;
			var nearbyActors = self.World.FindActorsInCircle(CenterPosition, nearbyActorRange);

			if (!ActorIsAiming(self)) // Attacking actors are not repelled
			{
				foreach (var nearbyActor in nearbyActors)
				{
					if (!nearbyActor.IsDead && nearbyActor != self) // && !ActorIsAiming(nearbyActor))
					{
						var nearbyActorMobileOGs = nearbyActor.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled);
						if (nearbyActorMobileOGs.Any())
						{
							var nearbyActorMobileOG = nearbyActorMobileOGs.FirstOrDefault();
							var repulsionMvVec = new MvVec(RepulsionVecFunc(MovementSpeed, CenterPosition, nearbyActorMobileOG.CenterPosition), 1);
							FleeVectors.Add(repulsionMvVec);
						}
					}
				}
			}
		}

		public WVec RemoveVecOfIntersectingEdge(Map.CellEdge edge, WVec move)
		{
			var newX = move.X;
			var newY = move.Y;

			if (edge.EdgeName == Map.CellEdgeName.Top || edge.EdgeName == Map.CellEdgeName.Bottom)
				newY = 0; // if vec is coming from bottom or top, we remove the Y component to allow only horizontal movement
			if (edge.EdgeName == Map.CellEdgeName.Left || edge.EdgeName == Map.CellEdgeName.Right)
				newX = 0; // if vec is coming from the right or left, we remove the X component to allow only vertical movement

			return new WVec(newX, newY, move.Z);
		}

		public static WVec RemoveBlockedVectors(WVec move, List<BlockedByCell> blockedByCells)
		{
			var newMoveX = move.X;
			var newMoveY = move.Y;

			if ((blockedByCells.Contains(BlockedByCell.TopLeft) || blockedByCells.Contains(BlockedByCell.Left) ||
				blockedByCells.Contains(BlockedByCell.BottomLeft)) && newMoveX < 0)
				newMoveX = 0;
			else if ((blockedByCells.Contains(BlockedByCell.TopRight) || blockedByCells.Contains(BlockedByCell.Right) ||
					blockedByCells.Contains(BlockedByCell.BottomRight)) && newMoveX > 0)
				newMoveX = 0;

			if ((blockedByCells.Contains(BlockedByCell.TopLeft) || blockedByCells.Contains(BlockedByCell.Top) ||
				blockedByCells.Contains(BlockedByCell.TopRight)) && newMoveY < 0)
				newMoveY = 0;
			else if ((blockedByCells.Contains(BlockedByCell.BottomLeft) || blockedByCells.Contains(BlockedByCell.Bottom) ||
					blockedByCells.Contains(BlockedByCell.BottomRight)) && newMoveY > 0)
				newMoveY = 0;

			return new WVec(newMoveX, newMoveY, move.Z);
		}

		public void GetBestNonCollidingMovement()
		{
		}

		// This is the default movement to the current path target
		// Delta = currPathTarget - CenterPosition
		// It needs to be scaled by MovementSpeed before being used
		public WVec GetDeltaMovement() => new(new WDist(MovementSpeed), WRot.FromYaw(Delta.Yaw));

		bool UnitHasCollidedWithUnits(WVec mv) => ActorsCollidingWithActorBool(CenterPosition, mv,
			LocalAvoidanceDist, Locomotor, attackingUnitsOnly: true);

		public void UpdateSeekVecWithLocalAvoidance()
		{

#pragma warning disable SA1137 // Elements should have the same indentation
			List<int> localAvoidanceAngleOffsetsLeft = new()
			{
				 0, -64, -128, -192,
				-256, -320, -384,
				-448, -512, -576,
				-640, -704, -768,
				-832, -896, -960,
				-1024
			};

			List<int> localAvoidanceAngleOffsetsRight = new()
			{
				 0, 64,  128,  192,
				 256,  320,  384,
				 448,  512,  576,
				 640,  704,  768,
				 832,  896,  960,
				 1024
			};
#pragma warning restore SA1137 // Elements should have the same indentation*/

			if (SeekVectors.Count == 0)
				return;

			// moveVec is equal to the Seekector
			var moveVec = SeekVectors[0].Vec;

			static bool PosIsToTheLeft(WPos p1, WPos p2, WPos checkPos)
				=> (p2.X - p1.X) * (checkPos.Y - p1.Y) - (p2.Y - p1.Y) * (checkPos.X - p1.X) > 0;

			// Only change the SeekVector if either we are not searching for the next target, or we are colliding with an object, otherwise continue
			// Revert to deltaMoveVec if we are no longer searching for the next target
			//if (!(useLocalAvoidance && UnitHasCollidedWithUnits(moveVec)) && !mobileOffGrid.SearchingForNextTarget)
			//{
			//	mobileOffGrid.SeekVectors = new List<MvVec>() { new(deltaMoveVec) };
			//	moveVec = deltaMoveVec;
			//}
			// Since the pathfinder avoids map obstacles, this must be a unit obstacle, so we employ our local avoidance strategy
			if (useLocalAvoidance && UnitHasCollidedWithUnits(moveVec))
			{
				var avoidanceVec = WVec.Zero;
				var revisedMoveVec = moveVec;
				var localAvoidanceAngleOffset = 0;
				var i = 0;
				do
				{
					var actorUnitIsCollidingWith = ActorsCollidingWithActor(CenterPosition, moveVec,
						LocalAvoidanceDist, Locomotor, attackingUnitsOnly: true).FirstOrDefault();
					MobileOffGrid collidingMobileOG;

					if (actorUnitIsCollidingWith != null)
					{
						var localAvoidanceMove = new WVec(LocalAvoidanceDist, WRot.FromYaw(moveVec.Yaw));
						collidingMobileOG = actorUnitIsCollidingWith.TraitsImplementing<MobileOffGrid>().FirstOrDefault(Exts.IsTraitEnabled);
						Overlay.AddCircle(collidingMobileOG.CenterPosition, collidingMobileOG.UnitRadius,
							(int)PersistConst.Never, 1, OverlayKeyStrings.LocalAvoidance);

						// We take the angle from the unit's current _movement destination_ NOT from the unit's current center position.
						// This gives us the offset that we need
						var angleDistToCollidingActor = collidingMobileOG.CenterPosition - (CenterPosition + localAvoidanceMove);
						var checkLeft = PosIsToTheLeft(CenterPosition,
										 CenterPosition + new WVec(LocalAvoidanceDist, WRot.FromYaw(moveVec.Yaw)),
										 LastPathTarget);

						if (checkLeft)
							localAvoidanceAngleOffset = localAvoidanceAngleOffsetsLeft[i];
						else
							localAvoidanceAngleOffset = localAvoidanceAngleOffsetsRight[i];

						//var p1 = CenterPosition;
						//var p2 = CenterPosition + moveVec * 3;
						//Overlay.AddLine(p1, p2, Color.Orange, 16, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);

						//p1 = CenterPosition + moveVec * 3;
						//p2 = p1 + angleDistToCollidingActor;
						//Overlay.AddLine(p1, p2, Color.BlueViolet, 16, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);

						//p1 = CenterPosition + moveVec * 3;
						//p2 = p1 + new WVec(new WDist((moveVec * 3).Length), WRot.FromYaw(moveVec.Yaw + angleDistToCollidingActor.Yaw));
						//Overlay.AddLine(p1, p2, Color.Green, 16, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);

						// NOTE: localAvoidanceAngleOffset is initially 0, ensuring that the normal collision is tested first.
						// TO DO: Identify why going up does not cause change, most likely has something to do with Yaw being very small

						avoidanceVec = new WVec(new WDist(moveVec.Length),
													  WRot.FromYaw(angleDistToCollidingActor.Yaw +
														new WAngle(checkLeft ? 256 : -256) +
														new WAngle(localAvoidanceAngleOffset)));
						//var newMoveVec = new WVec(new WDist(moveVec.Length),
						//						  WRot.FromYaw(moveVec.Yaw
						//						  + angleDistToCollidingActor.Yaw
						//						  //+ angleDistToCollidingActor.Yaw
						//						  //+ new WAngle(localAvoidanceAngleOffset)
						//						  ));

						var p1 = CenterPosition;
						var p2 = CenterPosition + avoidanceVec * 3;
						Overlay.AddLine(p1, p2, Color.Green, 16, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);

						//Overlay.AddText(CenterPosition, checkLeft.ToString(), Color.LightCyan, (int)PersistConst.Never, key: OverlayKeyStrings.LocalAvoidance);
						//revisedMoveVec = newMoveVec;
					}

					i++;
				}
				while (UnitHasCollidedWithUnits(revisedMoveVec) && i < localAvoidanceAngleOffsetsLeft.Count);

				if (!UnitHasCollidedWithUnits(revisedMoveVec))
				{
#if DEBUGWITHOVERLAY
					//Console.WriteLine($"move.Yaw {moveVec.Yaw}, revisedMove.Yaw: {revisedMoveVec.Yaw}");
					//RenderLine(self, CenterPosition, CenterPosition + revisedMoveVec);
					//RenderPoint(self, CenterPosition + revisedMoveVec, Color.LightGreen);
#endif
					AddToTraversedCirclesBuffer(self.CenterPosition + revisedMoveVec);
					//MoveOffGrid.RenderCircleColorCollDebug(self, CenterPosition + revisedMoveVec, UnitRadius, Color.LightGreen, 3);
					currLocalAvoidanceAngleOffset = localAvoidanceAngleOffset;
					//RenderLine(CenterPosition, CenterPosition + revisedMoveVec * 4, LineType.LocalAvoidanceDirection);
					pastMoveVec = moveVec;
					SeekVectors = new List<MvVec>() { new(revisedMoveVec, 6) };
					SearchingForNextTarget = true;
					IsBlocked = false;
				}
				//else // since we cannot move without colliding, we stop trying
				//{
				//	EndingActions();
				//	return Complete();
				//}
			}
			else if (useLocalAvoidance && currLocalAvoidanceAngleOffset != 0 && SearchingForNextTarget && !UnitHasCollidedWithUnits(pastMoveVec))
			{
				SetForcedFacing(-WAngle.ArcTan(CenterPosition.Y - CurrPathTarget.Y, CenterPosition.X - CurrPathTarget.X) + new WAngle(256));
				pastMoveVec = new WVec(0, 0, 0);
				currLocalAvoidanceAngleOffset = 0;
				SearchingForNextTarget = false;
			}
		}

		public void AddCellCollisionFleeVectors()
		{
			if (SeekVectors.Count == 0)
				return;

			WVec RepulsionVecFunc(WPos selfPos, WPos cellPos)
			{
				var repulsionDelta = cellPos - selfPos;
				var distToMove = Math.Min(repulsionDelta.Length, MovementSpeed);
				return -new WVec(new WDist(distToMove), WRot.FromYaw(repulsionDelta.Yaw));
			}

			//// Check collision with walls
			var cellsCollidingSet = new List<CPos>();
			cellsCollidingSet.AddRange(CellsCollidingWithActor(self, SeekVectors[0].Vec, 3, Locomotor));
			cellsCollidingSet.AddRange(CellsCollidingWithActor(self, SeekVectors[0].Vec, 2, Locomotor));
			cellsCollidingSet.AddRange(CellsCollidingWithActor(self, SeekVectors[0].Vec, 1, Locomotor));

			// Used by MobileOffGrid to suppress movement in the direction that the unit is being blocked
			BlockedByCells = DirectionOfCellsBlockingPos(self, CenterPosition, cellsCollidingSet);

			var fleeVecToUse = cellsCollidingSet.Distinct().Select(c => self.World.Map.CenterOfCell(c))
														   .Select(wp => RepulsionVecFunc(CenterPosition, wp)).ToList();

			FleeVectors.AddRange(fleeVecToUse.ConvertAll(v => new MvVec(v, 1)));
		}

		public void MobileOffGridMoveTick(Actor self)
		{
			CreateRepelNearbyUnitsVectorsTick(self);

			var move = ForcedMove == WVec.Zero ? GenFinalWVec() : ForcedMove;

			if (!SearchingForNextTarget && CurrPathTarget != WPos.Zero)
			{
				SeekVectors = new List<MvVec>() { new(GetDeltaMovement()) };
				CurrMovementState = MovementState.Seeking;
			}

			RenderPathingStats();
			//RenderCurrPathTarget();

			UpdateSeekVecWithLocalAvoidance();
			AddCellCollisionFleeVectors();

			// Remove vectors if unit is blocked
			//if (self.CurrentActivity is not ReturnToCellActivity)
			//	move = RemoveBlockedVectors(move, BlockedByCells);

			if (ActorsCollidingWithActorBool(CenterPosition, move, UnitRadius * 2, Locomotor, attackingUnitsOnly: true))
				GetBestNonCollidingMovement();

			if (move == WVec.Zero &&
			   (GenFinalWVec(WVecTypes.Seek, false) != -GenFinalWVec(WVecTypes.Flee, false) ||
				(GenFinalWVec(WVecTypes.Seek, false) == WVec.Zero && GenFinalWVec(WVecTypes.Seek, false) == WVec.Zero))
			   )
				return;

			if (SeekVectors.Count > 0)
				RenderLine(CenterPosition, CenterPosition + GenFinalWVec(WVecTypes.Seek, true) * 5, LineType.SeekVector);
			if (FleeVectors.Count > 0)
				RenderLine(CenterPosition, CenterPosition + GenFinalWVec(WVecTypes.Flee, true) * 5, LineType.FleeVector);
			if (SeekVectors.Count > 0 && FleeVectors.Count > 0)
				RenderLine(CenterPosition, CenterPosition + GenFinalWVec(WVecTypes.All, true) * 5, LineType.AllVector);

			DecrementTickFleeVectors();
			DecrementTickSeekVectors();

			var dat = self.World.Map.DistanceAboveTerrain(CenterPosition);

			if (IgnoreZVec)
				move = new WVec(move.X, move.Y, 0);

			var oldFacing = Facing;
			var turnSpeed = GetTurnSpeed(false);
			//DesiredFacing = -WAngle.ArcTan(CenterPosition.Y - CurrPathTarget.Y, CenterPosition.X - CurrPathTarget.X) + new WAngle(256);
			Facing = Util.TickFacing(Facing, DesiredFacing == WAngle.Zero ? move.Yaw : DesiredFacing, turnSpeed);
			//Console.WriteLine($"DesiredFacing: {DesiredFacing}, Facing: {Facing}, move.Yaw: {move.Yaw}");

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
			if ((ForcedAltitude != WDist.Zero && dat != ForcedAltitude) || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (ForcedAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			SetPosition(self, CenterPosition + move);

			// Reset temporary adjustments
			SetIgnoreZVec(false);
			SetForcedMove(WVec.Zero);
			SetForcedFacing(WAngle.Zero);
			SetForcedAltitude(WDist.Zero);
		}

		static bool CPosinMap(Actor self, CPos cPos)
		{
			return cPos.X >= 0 && cPos.X <= self.World.Map.MapSize.X - 1 &&
				   cPos.Y >= 0 && cPos.Y <= self.World.Map.MapSize.Y - 1;
		}

		static bool CPosinMap(World world, CPos cPos)
		{
			return cPos.X >= 0 && cPos.X <= world.Map.MapSize.X - 1 &&
				   cPos.Y >= 0 && cPos.Y <= world.Map.MapSize.Y - 1;
		}


		public static List<(WPos Source, WPos Dest)> GenSDPairs(WPos selfCenter, WVec move, IHitShape unitHitShape)
		{
			return unitHitShape.GetCorners(selfCenter.XYToInt2())
					.Zip(unitHitShape.GetCorners((selfCenter + move).XYToInt2())).ToList();
		}

		public static bool CellIsBlocked(Actor self, Locomotor locomotor, CPos cell, BlockedByActor check = BlockedByActor.Immovable)
		{
			return locomotor.MovementCostToEnterCell(self, cell, check, self, true) == short.MaxValue ||
				CellBlockedByBuilding(self, cell) || !CPosinMap(self, cell);
		}

		public static bool CellIsBlocked(World world, Locomotor locomotor, CPos cell, BlockedByActor check = BlockedByActor.Immovable)
		{
			return locomotor.MovementCostToEnterCell(null, cell, check, null, true) == short.MaxValue ||
				CellBlockedByBuilding(world, cell) || !CPosinMap(world, cell);
		}

		public static bool CellBlockedByBuilding(Actor self, CPos cell)
		{
			if (self != null)
				foreach (var otherActor in self.World.ActorMap.GetActorsAt(cell))
					if (otherActor.OccupiesSpace is Building building && !building.TransitOnlyCells().Contains(cell))
						return true;
			return false;
		}

		public static bool CellBlockedByBuilding(World world, CPos cell)
		{
			if (world != null)
				foreach (var otherActor in world.ActorMap.GetActorsAt(cell))
					if (otherActor.OccupiesSpace is Building building && !building.TransitOnlyCells().Contains(cell))
						return true;
			return false;
		}

		public static List<BlockedByCell> DirectionOfCellsBlockingPos(Actor self, WPos pos, CPos cell) =>
			DirectionOfCellsBlockingPos(self, pos, new List<CPos>() { cell });

		public static List<BlockedByCell> DirectionOfCellsBlockingPos(Actor self, WPos pos, List<CPos> cells)
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

		// This is a look ahead repel from cells that the unit is about to move into - hence the 'lookAhead' parameter
		// It returns the list of cells that collide with the actor, for use with repulsion
		public List<CPos> CellsCollidingWithActor(Actor self, WVec move, int lookAhead, Locomotor locomotor)
			=> CellsCollidingWithPos(self, self.CenterPosition, move, lookAhead, locomotor);

		public List<CPos> CellsCollidingWithPos(Actor self, WPos selfPos, WVec move, int lookAhead, Locomotor locomotor)
			=> GetCollidingCellsAfterUnitMovement(selfPos, move, locomotor, lookAhead, BlockedByActor.Immovable).ToList();

		// Main cell collision method, do not touch!
		public IEnumerable<CPos> GetCollidingCellsAfterUnitMovement(WPos selfPos, WVec move, Locomotor locomotor, int lookAhead = 1,
			BlockedByActor check = BlockedByActor.Immovable, int neighboursToCount = 0)
		{
			var cellsColliding = new List<CPos>();
			foreach (var (source, dest) in GenSDPairs(selfPos, move * lookAhead, UnitHitShape))
			{
				var cellsToCheck = ThetaStarPathSearch.GetAllCellsUnderneathALine(self.World, source, dest, neighboursToCount);
				foreach (var cell in cellsToCheck)
				{
					if (CellIsBlocked(self, locomotor, cell, check) && !cellsColliding.Contains(cell))
					{
						cellsColliding.Add(cell);
						yield return cell;
					}
				}
			}
		}

		// This is used for detecting if any cell has collided
		// Unlike CellsCollidingWithPos, it does not need to keep a list of cells and can exit early
		public bool CellsCollidingWithPosBool(Actor self, WPos selfPos, WVec move, int lookAhead, Locomotor locomotor)
			=> GetCollidingCellsAfterUnitMovement(selfPos, move, locomotor, lookAhead, BlockedByActor.Immovable).Any();

		public static bool ValidCollisionActor(Actor actor)
		{
			return actor.TraitsImplementing<Building>().Any() ||
				   actor.TraitsImplementing<Mobile>().Any() ||
				   actor.TraitsImplementing<MobileOffGrid>().Any();
		}

		// Only used for detecting collisions for the purpose of slices in the ThetaPF Exec Manager
		public List<Map.CellEdge> GetIntersectingEdges(WPos checkPos, WVec move, int lookAhead, Locomotor locomotor)
		{
			var intersectingEdges = new List<Map.CellEdge>();

			// Ray cast to cell collisions
			foreach (var (source, dest) in GenSDPairs(checkPos, move * lookAhead, UnitHitShape))
			{
				var cellsToCheck = ThetaStarPathSearch.GetAllCellsUnderneathALine(self.World, source, dest);
				foreach (var cell in cellsToCheck)
					if (CellIsBlocked(self, locomotor, cell))
						intersectingEdges = intersectingEdges.Union(self.World.Map.CellEdgesThatIntersectWithLine(cell, source, dest))
														.ToList();
			}

			return intersectingEdges;
		}

		// Only used for detecting collisions for the purpose of slices in the ThetaPF Exec Manager
		public bool HasCollidedWithCell(WPos sourcePos, WVec move, WDist lookAheadDist, Locomotor locomotor)
		{
			var newMoveVec = new WVec(UnitRadius * 2, WRot.FromYaw(move.Yaw));
			return DoesUnitMoveCollideWithCell(sourcePos, newMoveVec, locomotor);
		}

		// Only used for detecting collisions for the purpose of slices in the ThetaPF Exec Manager
		public bool HasCollidedWithCell(WPos sourcePos, WPos destPos, Locomotor locomotor)
		{
			return DoesUnitMoveCollideWithCell(sourcePos, destPos - sourcePos, locomotor);
		}

		// Only used for detecting collisions for the purpose of slices in the ThetaPF Exec Manager
		public bool DoesUnitMoveCollideWithCell(WPos sourcePos, WVec move, Locomotor locomotor)
		{
			if (self.CurrentActivity is ReturnToCellActivity)
				return false;

			var neighboursToCount = (int)Fix64.Ceiling((Fix64)UnitRadius.Length / (Fix64)1024);
			return GetCollidingCellsAfterUnitMovement(sourcePos, move, locomotor, 1, BlockedByActor.Immovable, neighboursToCount).Any();
		}

		public List<Actor> ActorsCollidingWithActor(WPos selfPos, WVec move, WDist lookAheadDist, Locomotor locomotor, bool attackingUnitsOnly = true)
			=> GetCollidingActorsAfterUnitMovement(selfPos, move, lookAheadDist, locomotor, attackingUnitsOnly).ToList();

		public bool ActorsCollidingWithActorBool(WPos selfPos, WVec move, WDist lookAheadDist, Locomotor locomotor, bool attackingUnitsOnly = true)
			=> GetCollidingActorsAfterUnitMovement(selfPos, move, lookAheadDist, locomotor, attackingUnitsOnly).Any();

		public IEnumerable<Actor> GetCollidingActorsAfterUnitMovement(WPos selfPos, WVec move, WDist lookAheadDist, Locomotor locomotor, bool attackingUnitsOnly = true)
		{
			var actorsColliding = new List<Actor>();

			// Ray cast to actor collisions
			// First get all actors surrounding the unit by the appropriate movement amount
			foreach (var destActor in self.World.FindActorsInCircle(selfPos, UnitRadius + new WDist(move.Length) + lookAheadDist)
												.Where(a => a != self && (!attackingUnitsOnly || ActorIsAiming(a))))
			{
				var destActorMobileOffGrid = destActor.TraitsImplementing<MobileOffGrid>().FirstOrDefault(Exts.IsTraitEnabled);
				if (!destActor.IsDead && ValidCollisionActor(destActor) && destActorMobileOffGrid != null)
				{
					// adding move to use future pos
					var destActorCenter = destActor.CenterPosition + destActorMobileOffGrid.GenFinalWVec();
					if (destActorCenter != WPos.Zero)
					{
#if DEBUGWITHOVERLAY
						//MoveOffGrid.RenderPointCollDebug(self, destActorCenter, Color.LightGreen);
#endif
						foreach (var destShape in destActor.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled))
							if (destShape.Info.Type is CircleShape shape)
							{
								var newMoveVec = new WVec(lookAheadDist, WRot.FromYaw(move.Yaw));

								foreach (var (source, dest) in GenSDPairs(selfPos, newMoveVec, shape))
								{
									var collision = destShape.Info.Type.LineIntersectsOrIsInside(destActor.CenterPosition, source, dest);
									if (collision && !actorsColliding.Contains(destActor))
									{
#if DEBUGWITHOVERLAY
										//Overlay.AddLine(source, dest, Color.LightSalmon, persist: 32, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);
#endif
										actorsColliding.Add(destActor);
										yield return destActor;
									}
									else
									{
#if DEBUGWITHOVERLAY
										//Overlay.AddLine(source, dest, Color.LightGreen, persist: 32, LineEndPoint.EndArrow, key: OverlayKeyStrings.LocalAvoidance);
#endif
									}
								}
							}
					}
				}
			}
		}

		public void RenderPathingStats()
		{
			const int Persist = (int)PersistConst.Never;
			const string Key = OverlayKeyStrings.PathingText;

			static string VecToXYstring(WVec v) => $"{v.X},{v.Y}";
			var seek = GenFinalWVec(WVecTypes.Seek, ignoreTimer: false);
			var flee = GenFinalWVec(WVecTypes.Flee, ignoreTimer: false);

			Overlay.AddText(CenterPosition,
				$"cpt:{CurrPathTarget.X},{CurrPathTarget.Y},\nMS:{Enum.GetName(typeof(MovementState), CurrMovementState)}\n" +
				$"S:{VecToXYstring(seek)}, F:{VecToXYstring(flee)}", Color.Orange, Persist, key: Key);
			if (CurrPathTarget != WPos.Zero)
				Overlay.AddPoint(CurrPathTarget, Color.Purple, Persist, key: Key);
		}

		public void RenderCurrPathTarget()
		{
			if (CurrPathTarget != WPos.Zero)
				Overlay.AddLine(CenterPosition, CurrPathTarget, Color.Orange, 8, LineEndPoint.EndArrow,
					thickness: 3, key: OverlayKeyStrings.Pathing);
		}

		public enum LineType
		{
			LocalAvoidanceHit,
			LocalAvoidanceMiss,
			LocalAvoidanceDirection,
			SeekVector,
			FleeVector,
			AllVector,
		}

		public void RenderLine(WPos source, WPos dest, LineType colType)
		{
			const int Persist = (int)PersistConst.Never;

			//if (colType == LineType.LocalAvoidanceHit)
			//	Overlay.AddLine(source, dest, Color.DarkRed, Persist, endPoints: LineEndPoint.EndArrow, thickness: 2, key: OverlayKeyStrings.LocalAvoidance);
			//else if (colType == LineType.LocalAvoidanceMiss)
			//	Overlay.AddLine(source, dest, Color.LightBlue, Persist, endPoints: LineEndPoint.EndArrow, thickness: 2, key: OverlayKeyStrings.LocalAvoidance);
			if (colType == LineType.LocalAvoidanceDirection)
				Overlay.AddLine(source, dest, Color.Yellow, 10, endPoints: LineEndPoint.EndArrow, thickness: 3, key: OverlayKeyStrings.LocalAvoidance);
			else if (colType == LineType.SeekVector)
				Overlay.AddLine(source, dest, Color.Cyan, Persist, endPoints: LineEndPoint.EndArrow, thickness: 2, key: OverlayKeyStrings.SeekVectors);
			else if (colType == LineType.FleeVector)
				Overlay.AddLine(source, dest, Color.Orange, Persist, endPoints: LineEndPoint.EndArrow, thickness: 2, key: OverlayKeyStrings.FleeVectors);
			else if (colType == LineType.AllVector)
				Overlay.AddLine(source, dest, Color.HotPink, Persist, endPoints: LineEndPoint.EndArrow, thickness: 3, key: OverlayKeyStrings.AllVectors);
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

			MobileOffGridMoveTick(self); // Update unit position

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
									.Where(a => !a.IsDead && a.TraitsImplementing<MobileOffGrid>().Any(Exts.IsTraitEnabled)).ToList();
			return new MoveOffGrid(self, groupedActors, target, WDist.Zero, range, initialTargetPosition, targetLineColor);
		}

		public Activity MoveWithinRange(in Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			var groupedActors = self.World.Selection.Actors
									.Where(a => !a.IsDead && a.TraitsImplementing<MobileOffGrid>().Any(Exts.IsTraitEnabled)).ToList();
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

		public static List<TraitPair<MobileOffGrid>> GetGroupedActorsWithMobileOGs(List<Actor> groupedActors)
		{
			return groupedActors.Where(a => a != null && !a.IsDead && a.TraitsImplementing<MobileOffGrid>().Any())
				.Select(a => new TraitPair<MobileOffGrid>(a, a.TraitsImplementing<MobileOffGrid>().FirstOrDefault(Exts.IsTraitEnabled)))
				.ToList();
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

				var target = Target.FromPos(order.Target.TerrainCenterPosition);
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
				if ((target.Type != TargetType.Terrain && target.Type != TargetType.Actor) || (mobileOffGrid.requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove)))
					return false;

				var location = self.World.Map.CellContaining(target.CenterPosition);

				// Aircraft can be force-landed by issuing a force-move order on a clear terrain cell
				// CellNodesDict that contain a blocking building are treated as regular force move orders, overriding
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
