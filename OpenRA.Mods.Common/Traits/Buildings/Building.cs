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
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum FootprintCellType
	{
		Empty = '_',
		OccupiedPassable = '=',
		Occupied = 'x',
		OccupiedUntargetable = 'X',
		OccupiedPassableTransitOnly = '+'
	}

	public class BuildingInfo : TraitInfo, IOccupySpaceInfo, IPlaceBuildingDecorationInfo
	{
		[Desc("Where you are allowed to place the building (Water, Clear, ...)")]
		public readonly HashSet<string> TerrainTypes = new HashSet<string>();

		[Desc("x means cell is blocked, capital X means blocked but not counting as targetable, ",
			"= means part of the footprint but passable, _ means completely empty.")]
		[FieldLoader.LoadUsing(nameof(LoadFootprint))]
		public readonly Footprint BuildingFootprint;

		public readonly CVec Dimensions = new CVec(1, 1);

		[Desc("Shift center of the actor by this offset.")]
		public readonly WVec LocalCenterOffset = WVec.Zero;

		public readonly bool RequiresBaseProvider = false;

		public readonly bool AllowInvalidPlacement = false;

		public readonly bool AllowPlacementOnResources = false;

		[Desc("Clear smudges from underneath the building footprint.")]
		public readonly bool RemoveSmudgesOnBuild = true;

		[Desc("Clear smudges from underneath the building footprint on sell.")]
		public readonly bool RemoveSmudgesOnSell = true;

		[Desc("Clear smudges from underneath the building footprint on transform.")]
		public readonly bool RemoveSmudgesOnTransform = true;

		public readonly string[] BuildSounds = Array.Empty<string>();

		public readonly string[] UndeploySounds = Array.Empty<string>();

		public override object Create(ActorInitializer init) { return new Building(init, this); }

		protected static object LoadFootprint(MiniYaml yaml) { return new Footprint(yaml); }

		public IEnumerable<CPos> FootprintTiles(CPos location, FootprintCellType type) { return BuildingFootprint.FootprintTiles(location, type); }
		public IEnumerable<CPos> Tiles(CPos location) { return BuildingFootprint.Tiles(location); }
		public IEnumerable<CPos> FrozenUnderFogTiles(CPos location) { return BuildingFootprint.FrozenUnderFogTiles(location); }
		public IEnumerable<CPos> OccupiedTiles(CPos location) { return BuildingFootprint.OccupiedTiles(location); }
		public IEnumerable<CPos> PathableTiles(CPos location) { return BuildingFootprint.PathableTiles(location); }
		public IEnumerable<CPos> TransitOnlyTiles(CPos location) { return BuildingFootprint.TransitOnlyTiles(location); }

		public WVec CenterOffset(World w)
		{
			var off = (w.Map.CenterOfCell(new CPos(Dimensions.X, Dimensions.Y)) - w.Map.CenterOfCell(new CPos(1, 1))) / 2;
			return (off - new WVec(0, 0, off.Z)) + LocalCenterOffset;
		}

		public BaseProvider FindBaseProvider(World world, Player p, CPos topLeft)
		{
			var center = world.Map.CenterOfCell(topLeft) + CenterOffset(world);
			var mapBuildRadius = world.WorldActor.TraitOrDefault<MapBuildRadius>();
			var allyBuildEnabled = mapBuildRadius != null && mapBuildRadius.AllyBuildRadiusEnabled;

			if (mapBuildRadius == null || !mapBuildRadius.BuildRadiusEnabled)
				return null;

			foreach (var bp in world.ActorsWithTrait<BaseProvider>())
			{
				var validOwner = bp.Actor.Owner == p || (allyBuildEnabled && bp.Actor.Owner.RelationshipWith(p) == PlayerRelationship.Ally);
				if (!validOwner || !bp.Trait.Ready())
					continue;

				// Range is counted from the center of the actor, not from each cell.
				var target = Target.FromPos(bp.Actor.CenterPosition);
				if (target.IsInRange(center, bp.Trait.Info.Range))
					return bp.Trait;
			}

			return null;
		}

		static bool AnyGivesBuildableArea(IEnumerable<Actor> actors, Player p, bool allyBuildEnabled, RequiresBuildableAreaInfo rba)
		{
			foreach (var a in actors)
			{
				if (!a.IsInWorld)
					continue;

				if (a.Owner != p && (!allyBuildEnabled || a.Owner.RelationshipWith(p) != PlayerRelationship.Ally))
					continue;

				var overlaps = rba.AreaTypes.Overlaps(a.TraitsImplementing<GivesBuildableArea>()
					.SelectMany(gba => gba.AreaTypes));

				if (overlaps)
					return true;
			}

			return false;
		}

		public virtual bool IsCloseEnoughToBase(World world, Player p, ActorInfo ai, CPos topLeft)
		{
			var requiresBuildableArea = ai.TraitInfoOrDefault<RequiresBuildableAreaInfo>();
			var mapBuildRadius = world.WorldActor.TraitOrDefault<MapBuildRadius>();

			if (requiresBuildableArea == null || p.PlayerActor.Trait<DeveloperMode>().BuildAnywhere)
			{
				return true;
			}

			if (mapBuildRadius != null && mapBuildRadius.BuildRadiusEnabled && RequiresBaseProvider && FindBaseProvider(world, p, topLeft) == null)
			{
				/*System.Console.WriteLine($"---Returned at mapRadius stuff:\n" +
					$"mapBuildRadius != null: {mapBuildRadius != null}\n" +
					$"mapBuildRadius.BuildRadiusEnabled: {mapBuildRadius.BuildRadiusEnabled}\n" +
					$"RequiresBaseProvider: {RequiresBaseProvider}\n" +
					$"FindBaseProvider(world, p, topLeft) == null: {FindBaseProvider(world, p, topLeft) == null}\n" +
					"");*/
				return false;
			}

			var adjacent = requiresBuildableArea.Adjacent;
			var buildingMaxBounds = Dimensions;

			var scanStart = world.Map.Clamp(topLeft - new CVec(adjacent, adjacent));
			var scanEnd = world.Map.Clamp(topLeft + buildingMaxBounds + new CVec(adjacent, adjacent));

			var nearnessCandidates = new List<CPos>();
			var bi = world.WorldActor.Trait<BuildingInfluence>();
			var allyBuildEnabled = mapBuildRadius != null && mapBuildRadius.AllyBuildRadiusEnabled;

			for (var y = scanStart.Y; y < scanEnd.Y; y++)
			{
				for (var x = scanStart.X; x < scanEnd.X; x++)
				{
					var c = new CPos(x, y);
					if (AnyGivesBuildableArea(world.ActorMap.GetActorsAt(c), p, allyBuildEnabled, requiresBuildableArea))
					{
						nearnessCandidates.Add(c);
						continue;
					}

					// Building bibs and pathable footprint cells are not included in the ActorMap
					// TODO: Allow ActorMap to track these and finally remove the BuildingInfluence layer completely
					if (AnyGivesBuildableArea(bi.GetBuildingsAt(c), p, allyBuildEnabled, requiresBuildableArea))
						nearnessCandidates.Add(c);
				}
			}

			var buildingTiles = Tiles(topLeft).ToList();
			return nearnessCandidates
				.Any(a => buildingTiles
					.Any(b => Math.Abs(a.X - b.X) <= adjacent
						&& Math.Abs(a.Y - b.Y) <= adjacent));
		}

		public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos topLeft, SubCell subCell = SubCell.Any)
		{
			return OccupiedTiles(topLeft)
				.ToDictionary(c => c, c => SubCell.FullCell);
		}

		bool IOccupySpaceInfo.SharesCell => false;

		public IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w, ActorInfo ai, WPos centerPosition)
		{
			if (!RequiresBaseProvider)
				return SpriteRenderable.None;

			return w.ActorsWithTrait<BaseProvider>().SelectMany(a => a.Trait.RangeCircleRenderables(wr));
		}
	}

	public class Building : IOccupySpace, ITargetableCells, INotifySold, INotifyTransform, ISync,
		INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		public readonly BuildingInfo Info;

		[Sync]
		readonly CPos topLeft;

		readonly Actor self;
		readonly BuildingInfluence influence;

		readonly (CPos, SubCell)[] occupiedCells;
		readonly (CPos, SubCell)[] targetableCells;
		readonly CPos[] transitOnlyCells;

		public CPos TopLeft => topLeft;
		public WPos CenterPosition { get; private set; }

		public Building(ActorInitializer init, BuildingInfo info)
		{
			self = init.Self;
			topLeft = init.GetValue<LocationInit, CPos>();
			Info = info;
			influence = self.World.WorldActor.Trait<BuildingInfluence>();

			occupiedCells = Info.OccupiedTiles(TopLeft)
				.Select(c => (c, SubCell.FullCell)).ToArray();

			targetableCells = Info.FootprintTiles(TopLeft, FootprintCellType.Occupied)
				.Select(c => (c, SubCell.FullCell)).ToArray();

			transitOnlyCells = Info.TransitOnlyTiles(TopLeft).ToArray();

			CenterPosition = init.World.Map.CenterOfCell(topLeft) + Info.CenterOffset(init.World);
		}

		public (CPos, SubCell)[] OccupiedCells() { return occupiedCells; }

		public CPos[] TransitOnlyCells() { return transitOnlyCells; }

		(CPos, SubCell)[] ITargetableCells.TargetableCells() { return targetableCells; }

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			AddedToWorld(self);
		}

		protected virtual void AddedToWorld(Actor self)
		{
			if (Info.RemoveSmudgesOnBuild)
				RemoveSmudges();

			self.World.AddToMaps(self, this);
			influence.AddInfluence(self, Info.Tiles(self.Location));
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.RemoveFromMaps(self, this);
			influence.RemoveInfluence(self, Info.Tiles(self.Location));
		}

		void INotifySold.Selling(Actor self)
		{
			if (Info.RemoveSmudgesOnSell)
				RemoveSmudges();
		}

		void INotifySold.Sold(Actor self) { }

		void INotifyTransform.BeforeTransform(Actor self)
		{
			if (Info.RemoveSmudgesOnTransform)
				RemoveSmudges();

			foreach (var s in Info.UndeploySounds)
				Game.Sound.PlayToPlayer(SoundType.World, self.Owner, s, self.CenterPosition);
		}

		void INotifyTransform.OnTransform(Actor self) { }
		void INotifyTransform.AfterTransform(Actor self) { }

		public void RemoveSmudges()
		{
			var smudgeLayers = self.World.WorldActor.TraitsImplementing<SmudgeLayer>();

			foreach (var smudgeLayer in smudgeLayers)
				foreach (var footprintTile in Info.Tiles(self.Location))
					smudgeLayer.RemoveSmudge(footprintTile);
		}
	}
}
