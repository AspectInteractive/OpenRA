using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.CollisionDebugOverlay;

namespace OpenRA.Mods.Common.Traits
{
	public class BasicCellDomain
	{
		public bool DomainIsBlocked;
		public Dictionary<CPos, DomainNode<CPos>> CellNodesDict = new();
		public DomainNode<CPos> Parent;
		public int ID;

		public BasicCellDomain(int id) => ID = id;
		static bool ValidParent(CPos c1, CPos c2) => Math.Abs(c2.X - c1.X) + Math.Abs(c2.Y - c1.Y) == 1;

		public bool CellIsInBCD(CPos cell) => CellNodesDict.ContainsKey(cell);

		public static List<CPos> CellNeighbours(Map map, CPos cell, Func<CPos, CPos, bool> checkCondition = null)
		{
			checkCondition ??= (c1, c2) => true; // use function that always returns true if no function is passed
			var neighbours = new List<CPos>();
			for (var x = -1; x <= 1; x++)
				for (var y = -1; y <= 1; y++)
					if (!(x == 0 && y == 0) && checkCondition(new CPos(cell.X + x, cell.Y + y), cell))
						neighbours.Add(new CPos(cell.X + x, cell.Y + y));
			return neighbours;
		}

		public void RemoveCell(CPos cell) => CellNodesDict.Remove(cell);

		public void AddCell(Map map, DomainNode<CPos> cellNode) => CellNodesDict.Add(cellNode.Value, cellNode);

		// NOTE: AddEdges cannot be done the way I have done it, because removing edges before the cells have been generated is flawed.
		public List<BasicCellDomain> RemoveParent(World world, Locomotor locomotor, DomainNode<CPos> parent, bool newBlockedStatus,
			ref int currBcdId, ref BasicCellDomain[,] allCellBCDs, ref CellEdges cellEdges, BlockedByActor check = BlockedByActor.Immovable)
		{
			var oldDomainID = allCellBCDs[parent.Value.X, parent.Value.Y].ID;

			using var pt = new PerfTimer("RemoveParent");
			var modifiedBCDs = new List<BasicCellDomain>() { this };

			RemoveCell(parent.Value);
			var allCellBCDsCopy = allCellBCDs;

			bool ValidDomain(CPos newNeighbour, CPos removedCell)
				=> ValidParent(removedCell, newNeighbour) && MatchingBlockedStatus(newNeighbour);
			bool MatchingBlockedStatus(CPos c) => allCellBCDsCopy[c.X, c.Y].DomainIsBlocked == newBlockedStatus;

			var oldParentValidNeighbours = CellNeighbours(world.Map, parent.Value, ValidDomain);

			// NOTE: None of this code affects the original domain since it is tied to the new domain
			BasicCellDomain oldParentNewBCD;

			if (oldParentValidNeighbours.Count > 0)
			{
				var oldParentLargestValidNeighbour = oldParentValidNeighbours
					.OrderByDescending(c => allCellBCDsCopy[c.X, c.Y].CellNodesDict.Count).FirstOrDefault();

				// Assign new parent and domain (largest domain with matching blocked status) to the old parent
				oldParentNewBCD = allCellBCDs[oldParentLargestValidNeighbour.X, oldParentLargestValidNeighbour.Y];
				oldParentNewBCD.AddCell(world.Map, parent);
			}
			else
			{
				oldParentNewBCD = CreateBasicCellDomainFromCellNodes(currBcdId, world, locomotor, new List<DomainNode<CPos>>() { parent }, check);
				currBcdId++;
			}

			if (oldParentNewBCD.ID == oldDomainID)
				throw new DataMisalignedException($"Parent has retained old domain with ID {oldDomainID} despite cell being removed.");

			allCellBCDs[parent.Value.X, parent.Value.Y] = oldParentNewBCD;
			cellEdges.AddCellEdges(world.Map, parent, ref allCellBCDs);
			return modifiedBCDs;
		}

		public static Queue<DomainNode<CPos>> GetCellsWithinDomain(World world, Locomotor locomotor,
			DomainNode<CPos> cellNode, BlockedByActor check = BlockedByActor.Immovable, HashSet<CPos> visited = null,
			Func<CPos, bool> cellMatchingCriteria = null, HashSet<CPos> limitedCellSet = null)
		{
			var map = world.Map;
			var candidateCells = new List<CPos>();
			var cell = cellNode.Value;

			if (!map.CPosInMap(cell))
				return default;

			candidateCells.Add(new CPos(cell.X, cell.Y - 1, cell.Layer)); // Top
			candidateCells.Add(new CPos(cell.X, cell.Y + 1, cell.Layer)); // Bottom
			candidateCells.Add(new CPos(cell.X - 1, cell.Y, cell.Layer)); // Left
			candidateCells.Add(new CPos(cell.X + 1, cell.Y, cell.Layer)); // Right

			cellMatchingCriteria ??= (CPos c) => MobileOffGrid.CellIsBlocked(world, locomotor, c, check);

			// If we are limited to cells within a specific set, we remove all candidates that are not in the set
			if (limitedCellSet != null && limitedCellSet.Count > 0)
				candidateCells.RemoveAll(c => !limitedCellSet.Contains(c));

			// We do not want to test cells that have already been included or excluded
			// A copy of visited is needed to bypass issue with using ref in functions
			if (visited != null && visited.Count > 0)
				candidateCells.RemoveAll(c => visited.Contains(c));

			var cellsWithinDomain = new Queue<DomainNode<CPos>>();
			foreach (var c in candidateCells)
				if (map.CPosInMap(c) && cellMatchingCriteria(c) == cellMatchingCriteria(cell))
					cellsWithinDomain.Enqueue(new DomainNode<CPos>(c));

			return cellsWithinDomain;
		}

		// REQUIREMENT: CellNodes must already have appropriate parent and children, as this method simply creates a domain
		// for a given list of cellNodes
		public static BasicCellDomain CreateBasicCellDomainFromCellNodes(int currBcdId, World world, Locomotor locomotor,
			List<DomainNode<CPos>> cellNodeList, BlockedByActor check = BlockedByActor.Immovable)
		{
			var map = world.Map;
			var bcd = new BasicCellDomain(currBcdId)
			{
				DomainIsBlocked = MobileOffGrid.CellIsBlocked(world, locomotor, cellNodeList.FirstOrDefault().Value, check)
			};

			foreach (var cellNode in cellNodeList)
				bcd.AddCell(map, cellNode);

			return bcd;
		}

		// Creates a basic cellNode domain by traversing all cells that match its blocked status, in the N E S and W destinations
		// NOTE: This deliberately does not populate CellNodesDict or AllCellBCDs, to keep the logic distinct.
		// TO DO: Isolate further by not having this create the BasicCellDomain, but just the linked nodes, or something similar
		public static BasicCellDomain CreateBasicCellDomain(int currBcdId, World world, Locomotor locomotor, CPos cell,
			ref HashSet<CPos> visited, ref BasicCellDomain[,] allCellBCDs, ref CellEdges cellEdges,
			BlockedByActor check = BlockedByActor.Immovable)
		{
			var map = world.Map;
			var cellNode = new DomainNode<CPos>(cell);
			var bcd = new BasicCellDomain(currBcdId)
			{
				DomainIsBlocked = MobileOffGrid.CellIsBlocked(world, locomotor, cell, check)
			};

			var cellsToExpandWithParent = GetCellsWithinDomain(world, locomotor, cellNode, check, visited);
			foreach (var child in cellsToExpandWithParent)
			{
				visited.Add(child.Value);
			}

			bcd.Parent = cellNode;
			visited.Add(cell);
			bcd.AddCell(map, cellNode);
			allCellBCDs[cellNode.Value.X, cellNode.Value.Y] = bcd;
			cellEdges.AddCellEdges(map, cellNode, ref allCellBCDs);

			while (cellsToExpandWithParent.Count > 0)
			{
				// NOTE: There is no need to add the Parent/Child since all children branch from the first parent
				var child = cellsToExpandWithParent.Dequeue();
				bcd.AddCell(map, child);
				allCellBCDs[child.Value.X, child.Value.Y] = bcd;
				cellEdges.AddCellEdges(map, child, ref allCellBCDs);

				// INVARIANT: bcd is independent of the below method that obtains neighbouring cells
				foreach (var cn in GetCellsWithinDomain(world, locomotor, child, check, visited))
				{
					cellsToExpandWithParent.Enqueue(cn);
					visited.Add(cn.Value);
				}
			}

			return bcd;
		}
	}

	public class BasicCellDomainManagerInfo : TraitInfo<BasicCellDomainManager>, NotBefore<LocomotorInfo> { }
	public class BasicCellDomainManager : IWorldLoaded
	{
		World world;
		Locomotor locomotor;
		public BasicCellDomain[,] AllCellBCDs;
		CollisionDebugOverlay collDebugOverlay;
		public bool DomainIsBlocked;
		int currBcdId = 0;
		CellEdges cellEdges;
		public List<DomainNode<CPos>> Heads = new();
		public Dictionary<CPos, DomainNode<CPos>> CellNodesDict = new();
		public DomainNode<CPos> Parent;
		public int ID;

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			locomotor = w.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			collDebugOverlay = world.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();
			Init(w);
		}

		public void Init(World world)
		{
			cellEdges = new CellEdges(world);
			PopulateAllCellNodesAndEdges();
			RenderAllCells();
		}

		public static List<CPos> CellNeighbours(Map map, CPos cell, Func<CPos, CPos, bool> checkCondition = null)
		{
			checkCondition ??= (c1, c2) => true; // use function that always returns true if no function is passed
			var neighbours = new List<CPos>();
			for (var x = -1; x <= 1; x++)
				for (var y = -1; y <= 1; y++)
					if (!(x == 0 && y == 0) && checkCondition(new CPos(cell.X + x, cell.Y + y), cell))
						neighbours.Add(new CPos(cell.X + x, cell.Y + y));
			return neighbours;
		}

		public void InitializeAllCellBCDs() => AllCellBCDs = new BasicCellDomain[world.Map.MapSize.X, world.Map.MapSize.Y];
		public void PopulateAllCellNodesAndEdges()
		{
			InitializeAllCellBCDs();
			var domainList = new List<BasicCellDomain>();

			world.ActorMap.CellUpdated += CellUpdated;

			var visited = new HashSet<CPos>();
			for (var x = 0; x < world.Map.MapSize.X; x++)
				for (var y = 0; y < world.Map.MapSize.Y; y++)
				{
					var cell = new CPos(x, y);
					if (!visited.Contains(cell))
					{
						var domain = BasicCellDomain.CreateBasicCellDomain(
							currBcdId, world, locomotor, cell, ref visited, ref AllCellBCDs, ref cellEdges);
						foreach (var cellNode in domain.CellNodesDict)
							AllCellBCDs[cellNode.Key.X, cellNode.Key.Y] = domain;
						domainList.Add(domain);
						currBcdId++;
					}
				}
		}

		public void RenderAllCells()
		{
			var allCells = new List<DomainNode<CPos>>();
			var allBCDs = new HashSet<BasicCellDomain>();

			foreach (var bcd in AllCellBCDs)
				if (!allBCDs.Contains(bcd))
				{
					allCells.AddRange(bcd.CellNodesDict.Values);
					allBCDs.Add(bcd);
				}

			RenderCells(allCells);
		}

		public void RenderCells(List<CPos> cells)
		{
			var cellNodes = new List<DomainNode<CPos>>();
			foreach (var c in cells)
				cellNodes.Add(AllCellBCDs[c.X, c.Y].CellNodesDict[c]);

			RenderCells(cellNodes);
		}

		public void RenderCells(List<DomainNode<CPos>> cellNodes)
		{
			foreach (var cellNode in cellNodes)
			{
				var cellBCD = AllCellBCDs[cellNode.Value.X, cellNode.Value.Y];
				collDebugOverlay.AddOrUpdateBCDNode(new BCDCellNode(world, cellBCD.ID, cellNode, cellBCD.DomainIsBlocked));
			}

			var edgesToUse = cellEdges.GenerateConnectedCellEdges(world.Map).ToList();
			var lineColour = Color.LightBlue;
			collDebugOverlay.ClearCellEdges();
			foreach (var edge in edgesToUse)
				collDebugOverlay.AddCellEdge(edge[0], edge[1], lineColour);
		}

		public struct ObjectRemoved
		{
			public string Type;
			public string Name;
			public string ID;

			public ObjectRemoved(string type, string name, string id)
			{
				Type = type;
				Name = name;
				ID = id;
			}
		}

		void CellUpdated(CPos cell)
		{
			if (world.Map.Contains(cell))
			{
				var cellBCD = AllCellBCDs[cell.X, cell.Y];
				var newBlockedStatus = MobileOffGrid.CellIsBlocked(world, locomotor, cell, BlockedByActor.Immovable);

				// Only remove parent if blocked status is changing
				if (cellBCD.DomainIsBlocked != newBlockedStatus)
				{
					var cellNode = cellBCD.CellNodesDict[cell];
					AllCellBCDs[cell.X, cell.Y].RemoveParent(world, locomotor, cellNode, newBlockedStatus, ref currBcdId, ref AllCellBCDs, ref cellEdges);
					var cellsToRender = new List<CPos>() { cell };
					cellsToRender.AddRange(CellNeighbours(world.Map, cell));
					RenderCells(cellsToRender);
				}
			}
		}
	}
}
