#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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

namespace OpenRA.Traits
{
	public enum TargetType : byte { Invalid, Actor, Terrain, FrozenActor }
	public readonly struct Target : IEquatable<Target>
	{
		public static readonly Target[] None = Array.Empty<Target>();
		public static readonly Target Invalid = default;
		public Actor Actor { get; }
		public FrozenActor FrozenActor { get; }

		readonly TargetType type;
		public readonly WPos TerrainCenterPosition;
		readonly WPos[] terrainPositions;
		readonly CPos? cell;
		readonly SubCell? subCell;
		readonly int generation;

		Target(WPos terrainCenterPosition, WPos[] terrainPositions = null)
		{
			type = TargetType.Terrain;
			TerrainCenterPosition = terrainCenterPosition;
			this.terrainPositions = terrainPositions ?? new[] { terrainCenterPosition };

			Actor = null;
			FrozenActor = null;
			cell = null;
			subCell = null;
			generation = 0;
		}

		Target(World w, CPos c, SubCell subCell)
		{
			type = TargetType.Terrain;
			TerrainCenterPosition = w.Map.CenterOfSubCell(c, subCell);
			terrainPositions = new[] { TerrainCenterPosition };
			cell = c;
			this.subCell = subCell;

			Actor = null;
			FrozenActor = null;
			generation = 0;
		}

		Target(Actor a, int generation, WPos? centerPosition = null)
		{
			type = TargetType.Actor;
			Actor = a;
			this.generation = generation;

			TerrainCenterPosition = centerPosition ?? WPos.Zero;

			terrainPositions = new[] { TerrainCenterPosition };
			FrozenActor = null;
			cell = null;
			subCell = null;
		}

		Target(CPos c, SubCell subCell, WPos terrainCenterPosition)
		{
			type = TargetType.Terrain;
			TerrainCenterPosition = terrainCenterPosition;
			terrainPositions = new[] { terrainCenterPosition };
			cell = c;
			this.subCell = subCell;

			Actor = null;
			FrozenActor = null;
			generation = 0;
		}

		Target(Actor a, WPos terrainPos = default)
		{
			type = TargetType.Actor;
			Actor = a;
			generation = a.Generation;
			TerrainCenterPosition = terrainPos != WPos.Zero ? terrainPos : WPos.Zero;
			terrainPositions = new[] { TerrainCenterPosition };
			FrozenActor = null;
			cell = null;
			subCell = null;
		}

		Target(FrozenActor fa)
		{
			type = TargetType.FrozenActor;
			FrozenActor = fa;

			TerrainCenterPosition = WPos.Zero;
			terrainPositions = null;
			Actor = null;
			cell = null;
			subCell = null;
			generation = 0;
		}

		Target(FrozenActor fa, WPos terrainPos = default)
		{
			type = TargetType.FrozenActor;
			FrozenActor = fa;

			TerrainCenterPosition = terrainPos != WPos.Zero ? terrainPos : WPos.Zero;
			terrainPositions = null;
			Actor = null;
			cell = null;
			subCell = null;
			generation = 0;
		}

		public static Target FromPos(WPos p) { return new Target(p); }
		public static Target FromCellWithTerrainPos(CPos c, SubCell subCell = SubCell.FullCell, WPos terrainPos = default) { return new Target(c, subCell, terrainPos); }
		public static Target FromActorWithTerrainPos(Actor a, WPos terrainPos = default) { return a != null ? new Target(a, terrainPos) : Invalid; }

		public static Target FromTargetPositions(in Target t) { return new Target(t.CenterPosition, t.Positions.ToArray()); }
		public static Target FromCell(World w, CPos c, SubCell subCell = SubCell.FullCell) { return new Target(w, c, subCell); }
		public static Target FromActor(Actor a) { return a != null ? new Target(a, a.Generation) : Invalid; }
		public static Target FromFrozenActor(FrozenActor fa) { return new Target(fa); }
		public static Target FromFrozenActorWithTerrainPos(FrozenActor fa, WPos terrainPos = default) { return new Target(fa, terrainPos); }

		public TargetType Type
		{
			get
			{
				if (type == TargetType.Actor)
				{
					// Actor is no longer in the world
					if (!Actor.IsInWorld || Actor.IsDead)
						return TargetType.Invalid;

					// Actor generation has changed (teleported or captured)
					if (Actor.Generation != generation)
						return TargetType.Invalid;
				}

				return type;
			}
		}

		public bool IsValidFor(Actor targeter)
		{
			if (targeter == null)
				return false;

			switch (Type)
			{
				case TargetType.Actor:
					return Actor.IsTargetableBy(targeter);
				case TargetType.FrozenActor:
					return FrozenActor.IsValid && FrozenActor.Visible && !FrozenActor.Hidden;
				case TargetType.Invalid:
					return false;
				case TargetType.Terrain:
				default:
					return true;
			}
		}

		// Currently all or nothing.
		// TODO: either replace based on target type or put in singleton trait
		public bool RequiresForceFire
		{
			get
			{
				if (Actor == null)
					return false;

				// PERF: Avoid LINQ.
				var isTargetable = false;
				foreach (var targetable in Actor.Targetables)
				{
					if (!targetable.IsTraitEnabled())
						continue;

					isTargetable = true;
					if (!targetable.RequiresForceFire)
						return false;
				}

				return isTargetable;
			}
		}

		// Representative position - see Positions for the full set of targetable positions.
		public WPos CenterPosition
		{
			get
			{
				switch (Type)
				{
					case TargetType.Actor:
					case TargetType.FrozenActor:
					case TargetType.Terrain:
						return TerrainCenterPosition;
					case TargetType.Invalid:
					default:
						throw new InvalidOperationException("Attempting to query the position of an invalid Target");
				}
			}
		}

		// Positions available to target for range checks
		static readonly WPos[] NoPositions = Array.Empty<WPos>();
		public IEnumerable<WPos> Positions
		{
			get
			{
				switch (Type)
				{
					case TargetType.Actor:
						return Actor.GetTargetablePositions();
					case TargetType.FrozenActor:
						// TargetablePositions may be null if it is Invalid
						return FrozenActor.TargetablePositions ?? NoPositions;
					case TargetType.Terrain:
						return terrainPositions;
					case TargetType.Invalid:
					default:
						return NoPositions;
				}
			}
		}

		public bool IsInRange(WPos origin, WDist range)
		{
			if (Type == TargetType.Invalid)
				return false;

			// Target ranges are calculated in 2D, so ignore height differences
			return Positions.Any(t => (t - origin).HorizontalLengthSquared <= range.LengthSquared);
		}

		public override string ToString()
		{
			switch (Type)
			{
				case TargetType.Actor:
					return Actor.ToString();

				case TargetType.FrozenActor:
					return FrozenActor.ToString();

				case TargetType.Terrain:
					return TerrainCenterPosition.ToString();

				case TargetType.Invalid:
				default:
					return "Invalid";
			}
		}

		public static bool operator ==(in Target me, in Target other)
		{
			if (me.type != other.type)
				return false;

			switch (me.type)
			{
				case TargetType.Terrain:
					return me.cell == other.cell && me.subCell == other.subCell
						&& me.CenterPosition == other.CenterPosition
						&& me.terrainPositions == other.terrainPositions;

				case TargetType.Actor:
					return me.Actor == other.Actor && me.generation == other.generation;

				case TargetType.FrozenActor:
					return me.FrozenActor == other.FrozenActor;

				case TargetType.Invalid:
				default:
					return false;
			}
		}

		public static bool operator !=(in Target me, in Target other)
		{
			return !(me == other);
		}

		public override int GetHashCode()
		{
			switch (type)
			{
				case TargetType.Terrain:
					var hash = TerrainCenterPosition.GetHashCode() ^ terrainPositions.GetHashCode();
					if (cell != null)
						hash ^= cell.GetHashCode();

					if (subCell != null)
						hash ^= subCell.GetHashCode();

					return hash;

				case TargetType.Actor:
					return Actor.GetHashCode() ^ generation.GetHashCode();

				case TargetType.FrozenActor:
					return FrozenActor.GetHashCode();

				case TargetType.Invalid:
				default:
					return 0;
			}
		}

		public bool Equals(Target other)
		{
			return other == this;
		}

		public override bool Equals(object other)
		{
			return other is Target t && t == this;
		}

		// Expose internal state for serialization by the orders code *only*
		internal static Target FromSerializedActor(Actor a, int generation, WPos centerPosition) { return a != null ? new Target(a, generation, centerPosition) : Invalid; }
		internal static Target FromSerializedTerrainPosition(WPos centerPosition, WPos[] terrainPositions) { return new Target(centerPosition, terrainPositions); }
		internal (TargetType Type, Actor Actor, int Generation, CPos? Cell, SubCell? SubCell, WPos Pos, WPos[] TerrainPositions) SerializableState =>
			(type, Actor, generation, cell, subCell, TerrainCenterPosition, terrainPositions);
	}
}
