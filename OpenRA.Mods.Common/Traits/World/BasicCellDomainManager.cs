using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.CollisionDebugOverlay;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;

namespace OpenRA.Mods.Common.Traits
{
	public class BasicCellDomain
	{
		public bool DomainIsBlocked;
		public Dictionary<CPos, LinkedListNode<CPos>> CellNodesDict = new();
		public LinkedListNode<CPos> Parent;
		public int ID;

		public BasicCellDomain() => ID = -1;
		public BasicCellDomain(int id) => ID = id;
		static bool ValidParent(LinkedListNode<CPos> c1, LinkedListNode<CPos> c2)
			=> Math.Abs(c2.Value.X - c1.Value.X) + Math.Abs(c2.Value.Y - c1.Value.Y) == 1;
		static bool ValidParent(CPos c1, CPos c2) => Math.Abs(c2.X - c1.X) + Math.Abs(c2.Y - c1.Y) == 1;

		public void LoadCells()
		{
			CellNodesDict.Clear();
			CellNodesDict.Add(Parent.Value, Parent);
			var children = Parent.Children;
			while (children.Count > 0)
			{
				var child = children.FirstOrDefault();
				CellNodesDict.Add(child.Value, child);
				children.AddRange(child.Children); // expand child after adding to CellNodesDict
				children.Remove(child); // remove child after expanding
			}
		}

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

		public void AddCell(Map map, LinkedListNode<CPos> cellNode) => CellNodesDict.Add(cellNode.Value, cellNode);

		// NOTE: AddEdges cannot be done the way I have done it, because removing edges before the cells have been generated is flawed.
		public List<BasicCellDomain> RemoveParent(World world, Locomotor locomotor, LinkedListNode<CPos> parent,
			List<LinkedListNode<CPos>> neighboursOfChildren, HashSet<CPos> borderCells, ref int currBcdId,
			//ref Dictionary<CPos, BasicCellDomain> allCellBCDs,
			ref BasicCellDomain[,] allCellBCDs,
			BlockedByActor check = BlockedByActor.Immovable)
		{
			using var pt = new PerfTimer("RemoveParent");
			var modifiedBCDs = new List<BasicCellDomain>() { this };
			neighboursOfChildren.Remove(parent);

			// We set the new parent and remove it from the BCD
			Parent = parent.RemoveParentAndReturnNewParent(neighboursOfChildren);
			RemoveCell(parent.Value);
			var allCellBCDsCopy = allCellBCDs;

			var cellIsBlocked = MobileOffGrid.CellIsBlocked(world, locomotor, parent.Value, BlockedByActor.Immovable);
			bool ValidParentAndValidBlockedStatus(CPos candidate, CPos parent)
				=> ValidParent(parent, candidate) && MatchingBlockedStatus(candidate);
			bool MatchingBlockedStatus(CPos c) => allCellBCDsCopy[c.X, c.Y].DomainIsBlocked == cellIsBlocked;

			var oldParentValidNeighbours = CellNeighbours(world.Map, parent.Value, ValidParentAndValidBlockedStatus);

			// NOTE: None of this code affects the original domain since it is tied to the new domain
			BasicCellDomain oldParentNewBCD;

			if (oldParentValidNeighbours.Count > 0)
			{
				var oldParentLargestValidNeighbour = oldParentValidNeighbours
					.OrderByDescending(c => allCellBCDsCopy[c.X, c.Y].CellNodesDict.Count).FirstOrDefault();

				// Assign new parent and domain (largest domain with matching blocked status) to the old parent
				oldParentNewBCD = allCellBCDs[oldParentLargestValidNeighbour.X, oldParentLargestValidNeighbour.Y];
				parent.Parent = oldParentNewBCD.CellNodesDict[oldParentLargestValidNeighbour];
				oldParentNewBCD.CellNodesDict[oldParentLargestValidNeighbour].Children.Add(parent);
				oldParentNewBCD.AddCell(world.Map, parent);
			}
			else
			{
				oldParentNewBCD = CreateBasicCellDomainFromCellNodes(currBcdId, world, locomotor, new List<LinkedListNode<CPos>>() { parent }, check);
				parent.Parent = null;
				currBcdId++;
			}

			allCellBCDs[parent.Value.X, parent.Value.Y] = oldParentNewBCD;
			modifiedBCDs.Add(oldParentNewBCD);

			// If there are no cells to handle or no new Parent could be found (can only mean there are no children),
			// then there is no further action needed
			if (CellNodesDict.Count == 0 || Parent == null)
				return modifiedBCDs;

			Parent.Parent = null; // the new Parent must be detached from its old parent.

			var origCellNodesDict = CellNodesDict;

			// We try to find all cells again from the new parent and assign them to this domain
			CellNodesDict = FindNodesFromStartNode(world, locomotor, Parent, CellNodesDict.Values.ToList(), allCellBCDs, new HashSet<CPos>(), check)
								.ToDictionary(c => c.Value, c => c);

			var cellsNotFound = origCellNodesDict.Values.Select(cn => cn.Value).Except(CellNodesDict.Values.Select(cn => cn.Value)).ToList();

			var colorToUse = Color.Green;

			// Any cells that were not found from the new parent need to form new domains
			if (cellsNotFound.Count > 0)
			{
				// First set the existing domain to a new ID
				ID = currBcdId;
				currBcdId++;

				// Next get the set of all cells that could not be found from the new parent
				var remainingCellNodes = origCellNodesDict.Where(c => cellsNotFound.Contains(c.Key)).ToDictionary(k => k.Key, k => k.Value);

				// Keep looping through the remaining nodes until all of them have a domain
				while (remainingCellNodes.Count > 0)
				{
					// Use the first cell node in the remaining nodes list to search for all other remaining cell nodes
					// NOTE: This does not use parent/child logic, it is simply a BFS
					var firstNode = remainingCellNodes.Values.First();
					firstNode.Parent = null; // This ensures we don't set recursive node
					var candidateCellNodes = FindNodesFromStartNode(world, locomotor, firstNode,
						remainingCellNodes.Values.ToList(), allCellBCDs, new HashSet<CPos>(), check).ToDictionary(c => c.Value, c => c);

					// Add this new set of cell nodes to a new domain, then increment the currBcdId
					var newBCD = CreateBasicCellDomainFromCellNodes(currBcdId, world, locomotor, candidateCellNodes.Values.ToList(), check);
					foreach (var cell in candidateCellNodes.Keys)
						allCellBCDs[cell.X, cell.Y] = newBCD;
					modifiedBCDs.Add(newBCD);
					currBcdId++;

					// Remove the nodes that have recently been added to a new domain, and repeat the process with the remaining nodes
					remainingCellNodes = remainingCellNodes.Where(c => !candidateCellNodes.ContainsKey(c.Key)).ToDictionary(k => k.Key, k => k.Value);
				}
			}

			return modifiedBCDs;
		}

		// NOTE: Does NOT iterate through children of nodes, uses CPos instead and works off of the assumption that
		// any adjacent node sharing the same domain is traversable
		public static List<LinkedListNode<CPos>> FindNodesFromStartNode(World world, Locomotor locomotor, LinkedListNode<CPos> startNode,
			List<LinkedListNode<CPos>> targetNodes,
			//Dictionary<CPos, BasicCellDomain> cellBCDs,
			BasicCellDomain[,] cellBCDs,
			HashSet<CPos> borderCells,
			BlockedByActor check = BlockedByActor.Immovable)
		{
			var visited = new HashSet<CPos>();
			var foundTargetNodes = new List<LinkedListNode<CPos>>();
			var targetNodeCells = targetNodes.ConvertAll(cn => cn.Value);

			var nodesToExpand = new Queue<LinkedListNode<CPos>>();
			nodesToExpand.Enqueue(startNode);
			visited.Add(startNode.Value);

			bool MatchingDomainID(CPos c)
			{
				var snv = startNode.Value;
				if (cellBCDs[c.X, c.Y] != null && cellBCDs[snv.X, snv.Y] != null)
					return cellBCDs[c.X, c.Y].ID == cellBCDs[snv.X, snv.Y].ID;
				return false; // cannot match domain ID if one of the domain IDs is missing
			}

			while (nodesToExpand.Count > 0 && targetNodeCells.Count > 0)
			{
				var cn = nodesToExpand.Dequeue();
				if (targetNodeCells.Contains(cn.Value))
				{
					foundTargetNodes.Add(cn);
					cn.Parent?.Children.Add(cn);
					targetNodeCells.Remove(cn.Value);
				}

				foreach (var cnd in GetCellsWithinDomain(world, locomotor, cn, check, visited, MatchingDomainID, borderCells))
				{
					nodesToExpand.Enqueue(cnd);
					visited.Add(cnd.Value);
				}
			}

			return foundTargetNodes;
		}

		public static Queue<LinkedListNode<CPos>> GetCellsWithinDomain(World world, Locomotor locomotor,
			LinkedListNode<CPos> cellNode, BlockedByActor check = BlockedByActor.Immovable, HashSet<CPos> visited = null,
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

			var cellsWithinDomain = new Queue<LinkedListNode<CPos>>();
			foreach (var c in candidateCells)
				if (map.CPosInMap(c) && cellMatchingCriteria(c) == cellMatchingCriteria(cell))
					cellsWithinDomain.Enqueue(new LinkedListNode<CPos>(cellNode, c, ValidParent));

			return cellsWithinDomain;
		}

		// REQUIREMENT: CellNodes must already have appropriate parent and children, as this method simply creates a domain
		// for a given list of cellNodes
		public static BasicCellDomain CreateBasicCellDomainFromCellNodes(int currBcdId, World world, Locomotor locomotor,
			List<LinkedListNode<CPos>> cellNodeList, BlockedByActor check = BlockedByActor.Immovable)
		{
			var map = world.Map;
			var bcd = new BasicCellDomain(currBcdId)
			{
				DomainIsBlocked = MobileOffGrid.CellIsBlocked(world, locomotor, cellNodeList.FirstOrDefault().Value, check)
			};

			if (cellNodeList.Count > 1)
			{
				var highestParent = cellNodeList.FirstOrDefault();
				while (highestParent.Parent != null)
					highestParent = highestParent.Parent;
				bcd.Parent = highestParent;
			}
			else
				bcd.Parent = null;

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
			var cellNode = new LinkedListNode<CPos>(cell, ValidParent);
			var bcd = new BasicCellDomain(currBcdId)
			{
				DomainIsBlocked = MobileOffGrid.CellIsBlocked(world, locomotor, cell, check)
			};

			var cellsToExpandWithParent = GetCellsWithinDomain(world, locomotor, cellNode, check, visited);
			foreach (var child in cellsToExpandWithParent)
			{
				cellNode.AddChild(child);
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
					child.AddChild(cn);
					visited.Add(cn.Value);
				}
			}

			return bcd;
		}
	}

	public class CellEdges
	{
#pragma warning disable IDE1006 // Naming Styles
		const int le = 1;
		const int re = 2;
		const int te = 4;
		const int be = 8;
#pragma warning restore IDE1006 // Naming Styles

		public int[] AllCellEdges;
		public int CCPosCols;
		public int CCPosRows;

		public CellEdges(World world) => InitializeAllCellEdges(world);

		// col = x, row = y
		int Get(int col, int row) => AllCellEdges[row * CCPosCols + col];
		void Set(int col, int row, int val) => AllCellEdges[row * CCPosCols + col] = val;
		void Add(int col, int row, int val) => AllCellEdges[row * CCPosCols + col] |= val;
		void Remove(int col, int row, int val) => AllCellEdges[row * CCPosCols + col] &= ~val;

		public void InitializeAllCellEdges(World world)
		{
			CCPosCols = world.Map.MapSize.X;
			CCPosRows = world.Map.MapSize.Y;
			AllCellEdges = new int[CCPosCols * CCPosRows];
		}

		// NOTE: This requires all cells to be loaded, if any cell is loaded afterwards this becomes invalid
		public void LoadEdges(Map map, ref BasicCellDomain[,] allCellBCDs, List<LinkedListNode<CPos>> cellNodes, bool clearFirst = true)
		{
			if (clearFirst)
				InitializeAllCellEdges(world);

			foreach (var cell in cellNodes)
				AddEdgeIfNoNeighbourExists(map, allCellBCDs, cell.Value);
		}

		public void AddEdge(CPos cell, int edge) => Add(cell.X, cell.Y, edge);

		// Removes a cellNode edge from the list of edges. The order of positions does not matter
		public void RemoveEdge(CPos cell, int edge) => Remove(cell.X, cell.Y, edge);

		public void AddEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellID = allCellBCDs[cell.X, cell.Y].ID;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].ID == cellID)
				AddEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].ID == cellID)
				AddEdge(map.RightEdgeOfCell(cell));
			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].ID == cellID)
				AddEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].ID == cellID)
				AddEdge(map.BottomEdgeOfCell(cell));
		}

		public void AddEdgeIfNoNeighbourExists(Map map, BasicCellDomain[,] allCellBCDs, CPos cell)
		{
			var t = cell + new CVec(0, -1);
			var b = cell + new CVec(0, 1);
			var l = cell + new CVec(-1, 0);
			var r = cell + new CVec(1, 0);

			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].ID != allCellBCDs[cell.X, cell.Y].ID)
				AddEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].ID != allCellBCDs[cell.X, cell.Y].ID)
				AddEdge(map.BottomEdgeOfCell(cell));
			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].ID != allCellBCDs[cell.X, cell.Y].ID)
				AddEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].ID != allCellBCDs[cell.X, cell.Y].ID)
				AddEdge(map.RightEdgeOfCell(cell));
			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(cell, te);
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(cell, be);
			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(cell, le);
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(cell, re);
		}

		public void RemoveEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellID = allCellBCDs[cell.X, cell.Y].ID;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].ID == cellID)
				RemoveEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].ID == cellID)
				RemoveEdge(map.RightEdgeOfCell(cell));
			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].ID == cellID)
				RemoveEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].ID == cellID)
				RemoveEdge(map.BottomEdgeOfCell(cell));
		}

		public void AddCellEdges(Map map, LinkedListNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID == cellID)
				RemoveEdge(cell, le);
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID == cellID)
				RemoveEdge(cell, re);
			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID == cellID)
				RemoveEdge(cell, te);
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID == cellID)
				RemoveEdge(cell, be);
		}

		public void AddEdgeIfNeighbourExists(Map map, CPos cell, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellID = allCellNodes[cell.X][cell.Y].ID;

			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID == cellID)
				AddEdge(cell, le);
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID == cellID)
				AddEdge(cell, re);
			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID == cellID)
				AddEdge(cell, te);
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID == cellID)
				AddEdge(cell, be);
		}

		public void AddCellEdges(Map map, LinkedListNode<CPos> cellNode, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			AddEdge(cellNode.Value, le);
			AddEdge(cellNode.Value, re);
			AddEdge(cellNode.Value, te);
			AddEdge(cellNode.Value, be);

			RemoveEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);
		}

		public void RemoveCellEdges(Map map, LinkedListNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
			=> AddEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);

		public List<List<CCPos>> GenerateCellEdges()
		{
			var edges = new List<List<CCPos>>();

			for (var i = 0; i < CCPosRows * CCPosCols; i++)
			{
				var x = i % CCPosCols;
				var y = i / CCPosCols;
				if (Get(x, y))
					edges.Add(new List<CCPos>() { new(x, y), new(x, y) });
			}

			return edges;
		}

		public List<List<CCPos>> GenerateConnectedCellEdges()
		{
			var x = 0;
			var y = 0;

			var edges = new List<List<CCPos>>();

			// We subtract 1 from
			while (y < CCPosRows - 1)
			{
				while (x < CCPosCols - 1)
				{
					// If both the current and next point are occupied, then we extend the end point further
					var endX = x;
					if (Get(x, y) && endX + 1 < CCPosCols)
						while (endX + 1 < CCPosCols && Get(endX + 1, y))
						{
							endX++;
						}

					// We have either not started, reached the end of the map, or found an end point in the middle of the map
					// If an end point was found, we add the edge
					if (endX != x)
					{
						edges.Add(new List<CCPos>() { new(x, y), new(endX, y) });
						x = endX + 1; // We increment past the endpoint
					}
					else
						x++; // If we have not started, we simply increment x
				}

				y++;
				x = 0;
			}

			return edges;
		}

		public static List<List<WPos>> GenerateConnectedCellEdges(List<List<WPos>> cellEdges)
		{
			List<List<WPos>> newEdges = new();
			Dictionary<WPos, List<List<WPos>>> edgesByStart = new();
			HashSet<List<WPos>> traversedEdges = new();

			foreach (var edge in cellEdges)
			{
				if (edgesByStart.ContainsKey(edge[0]))
					edgesByStart[edge[0]].Add(edge);
				else
					edgesByStart[edge[0]] = new List<List<WPos>> { edge.ToList() };
			}

			for (var i = 0; i < cellEdges.Count; i++)
			{
				var edge = cellEdges[i].ToList();

				if (traversedEdges.Any(e => e[0] == edge[0] && e[1] == edge[1]))
					continue;

				traversedEdges.Add(new List<WPos>(edge));
				while (edgesByStart.ContainsKey(edge[1]) && // if a second edge starts where this edge ends
					   edgesByStart[edge[1]].Any(e => e[1].X == edge[0].X || // and the second edge ends in the same X
													  e[1].Y == edge[0].Y)) // or ends in the same Y, then it's a continous line
				{
					var matchingEdges = edgesByStart[edge[1]]
						.Where(e => e[1].X == edge[0].X ||
									e[1].Y == edge[0].Y).ToList();

					if (matchingEdges.Count > 1)
						throw new DataMisalignedException($"Cannot have more than one matching edge: {string.Join(',', matchingEdges)}");

					traversedEdges.Add(new List<WPos>(matchingEdges[0]));
					edge[1] = matchingEdges[0][1]; // make the original edge's end point equal to the second edge's end point
				}

				// Before adding the new edge, remove any existing edges that are overlapping and smaller than the currently found edge
				newEdges.RemoveAll(e => (e[0].X >= edge[0].X && e[1].X <= edge[1].X &&                      // X's of edge are fully contained
										 e[0].Y == edge[0].Y && e[1].Y == edge[1].Y && e[0].Y == e[1].Y) || // and all points are on the same Y plane
										(e[0].Y >= edge[0].Y && e[1].Y <= edge[1].Y &&                      // Y's of edge are fully contained
										 e[0].X == edge[0].X && e[1].X == edge[1].X && e[0].X == e[1].X));  // and all points are on the same  plane
				newEdges.Add(edge);
			}

			return newEdges;
		}
	}

	public class BasicCellDomainManagerInfo : TraitInfo<BasicCellDomainManager>, NotBefore<LocomotorInfo> { }
	public class BasicCellDomainManager : IWorldLoaded, ITick
	{
		World world;
		Locomotor locomotor;
		public BasicCellDomain[,] AllCellBCDs;
		CollisionDebugOverlay collDebugOverlay;
		public bool DomainIsBlocked;
		List<BasicCellDomain> modifiedBCDs = new();
		int currBcdId = 0;
		List<(Actor Actor, bool PastBlockedStatus)> actorsWithBlockStatusChange = new();
		List<(Building Building, Actor BuildingActor, bool PastBlockedStatus)> buildingsWithBlockStatusChange = new();
		public LinkedListNode<CPos>[][] AllCellNodes;
		CellEdges cellEdges;
		public List<LinkedListNode<CPos>> Heads = new();
		public Dictionary<CPos, LinkedListNode<CPos>> CellNodesDict = new();
		public LinkedListNode<CPos> Parent;
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

		static bool ValidParent(LinkedListNode<CPos> c1, LinkedListNode<CPos> c2)
			=> Math.Abs(c2.Value.X - c1.Value.X) + Math.Abs(c2.Value.Y - c1.Value.Y) == 1;

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

		public static void AddHeadRemoveExisting(LinkedListNode<CPos> head, ref List<LinkedListNode<CPos>> heads)
		{
			heads.RemoveAll(h => h.Value == head.Value);
			heads.Add(head);
		}

		bool CellIsBlocked(CPos c) => MobileOffGrid.CellIsBlocked(world, locomotor, c, BlockedByActor.Immovable);

		public static int DistBetweenNodes(LinkedListNode<CPos> a, LinkedListNode<CPos> b) => DistBetweenCPos(a.Value, b.Value);
		public static int DistBetweenCPos(CPos a, CPos b) => (b - a).LengthSquared;

		public void InitializeAllCellBCDs() => AllCellBCDs = new BasicCellDomain[world.Map.MapSize.X, world.Map.MapSize.Y];
		public void PopulateAllCellNodesAndEdges()
		{
			InitializeAllCellBCDs();
			var domainList = new List<BasicCellDomain>();

			// === Temporarily disable RemoveParent ===
			world.ActorRemoved += AddedToOrRemovedFromWorld;
			world.ActorAdded += AddedToOrRemovedFromWorld;

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

			RenderAllCells();
		}

		public void Tick(Actor self)
		{
			if (modifiedBCDs.Count > 0)
			{
				var updatedCellNodes = new List<LinkedListNode<CPos>>();
				foreach (var bcd in modifiedBCDs)
					updatedCellNodes.AddRange(bcd.CellNodesDict.Values.ToList());
				RenderCells(updatedCellNodes);
				//RenderAllCells();
				modifiedBCDs.Clear();
			}

			// If actor is in world, then blocked status must now be True, and PastBlockedStatus must have been False,
			// making !PastBlockedStatus == True == ab.Actor.IsInWorld
			// Otherwise if actor is no longer in the world, then blocked status must now be False, and PastBlockedStatus must have been True,
			// making !PastBlockedStatus == False == ab.Actor.IsInWorld
			if (actorsWithBlockStatusChange.Count > 0 &&
				actorsWithBlockStatusChange.Any(ab => !ab.PastBlockedStatus == ab.Actor.IsInWorld))
			{
				for (var i = actorsWithBlockStatusChange.Count - 1; i >= 0; i--)
				{
					var actorWithBlockStatus = actorsWithBlockStatusChange[i];
					if (!actorWithBlockStatus.PastBlockedStatus == actorWithBlockStatus.Actor.IsInWorld)
					{
						ActorBlockedStatusChange(actorWithBlockStatus.Actor, actorWithBlockStatus.PastBlockedStatus);
						actorsWithBlockStatusChange.RemoveAt(i);
					}
				}
			}

			// Same logic as actorsWithBlockStatusChange
			if (buildingsWithBlockStatusChange.Count > 0 &&
				buildingsWithBlockStatusChange.Any(bb => !bb.PastBlockedStatus == bb.BuildingActor.IsInWorld))
			{
				for (var i = buildingsWithBlockStatusChange.Count - 1; i >= 0; i--)
				{
					var buildingWithBlockStatus = buildingsWithBlockStatusChange[i];
					if (!buildingWithBlockStatus.PastBlockedStatus == buildingWithBlockStatus.BuildingActor.IsInWorld)
					{
						BuildingBlockedStatusChange(buildingWithBlockStatus.Building, buildingWithBlockStatus.PastBlockedStatus);
						buildingsWithBlockStatusChange.RemoveAt(i);
					}
				}
			}
		}

		public void RenderBCD(List<BasicCellDomain> domainList)
		{
			foreach (var bcd in domainList)
				RenderCells(bcd.CellNodesDict.Values.ToList());
		}

		public void RenderAllCells()
		{
			var allCells = new List<LinkedListNode<CPos>>();
			var allBCDs = new HashSet<BasicCellDomain>();

			foreach (var bcd in AllCellBCDs)
				if (!allBCDs.Contains(bcd))
				{
					allCells.AddRange(bcd.CellNodesDict.Values);
					allBCDs.Add(bcd);
				}

			RenderCells(allCells);
		}

		public void RenderCells(List<LinkedListNode<CPos>> cellNodes)
		{
			foreach (var cellNode in cellNodes)
			{
				var cellBCD = AllCellBCDs[cellNode.Value.X, cellNode.Value.Y];
				collDebugOverlay.AddOrUpdateBCDNode(new BCDCellNode(world, cellBCD.ID, cellNode, cellBCD.DomainIsBlocked));
			}

			var edgesToUse = cellEdges.GenerateConnectedCellEdges().ToList();
			//var edgesToUse = cellEdges.GenerateCellEdges().ToList();
			//var lineColour = Color.RandomColor();
			var lineColour = Color.LightBlue;
			foreach (var edge in edgesToUse)
				MoveOffGrid.RenderLineWithColorCollDebug(world.WorldActor,
					world.Map.WPosFromCCPos(edge[0]), world.Map.WPosFromCCPos(edge[1]), lineColour, 3, LineEndPoint.Circle);
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

		public void ActorBlockedStatusChange(Actor self, bool pastBlockedStatus)
		{
			if (MobileOffGrid.CellIsBlocked(world, locomotor, self.Location, BlockedByActor.Immovable) != pastBlockedStatus)
			{
				var cell = self.Location;
				var cellBCD = AllCellBCDs[cell.X, cell.Y];
				var cellNode = cellBCD.CellNodesDict[self.Location];
				var neighboursOfChildren = cellNode.Children.SelectMany(c =>
					BasicCellDomain.CellNeighbours(self.World.Map, c.Value, (candidate, parent) => cellBCD.CellIsInBCD(candidate))
						.ConvertAll(c2 => AllCellBCDs[c2.X, c2.Y].CellNodesDict[c2])).ToList();
				modifiedBCDs.AddRange(AllCellBCDs[cell.X, cell.Y].RemoveParent(self.World, locomotor, cellNode, neighboursOfChildren, new HashSet<CPos>(),
					ref currBcdId, ref AllCellBCDs));
				modifiedBCDs = modifiedBCDs.Distinct().ToList();
			}
		}

		public void BuildingBlockedStatusChange(Building self, bool pastBlockedStatus)
		{
			var cellsWithChangedBlockStatus = self.OccupiedCells().Where(c =>
					MobileOffGrid.CellIsBlocked(world.WorldActor, locomotor, c.Item1, BlockedByActor.Immovable) != pastBlockedStatus)
					.Select(csc => csc.Item1).ToList();

			foreach (var cell in cellsWithChangedBlockStatus)
			{
				var cellBCD = AllCellBCDs[cell.X, cell.Y];
				var cellNode = cellBCD.CellNodesDict[cell];
				var neighboursOfChildren = cellNode.Children.SelectMany(c =>
					BasicCellDomain.CellNeighbours(world.Map, c.Value, (candidate, parent) => cellBCD.CellIsInBCD(candidate))
						.ConvertAll(c2 => AllCellBCDs[c2.X, c2.Y].CellNodesDict[c2])).ToList();
				modifiedBCDs.AddRange(AllCellBCDs[cell.X, cell.Y].RemoveParent(world, locomotor, cellNode, neighboursOfChildren, new HashSet<CPos>(),
					ref currBcdId, ref AllCellBCDs));
				modifiedBCDs = modifiedBCDs.Distinct().ToList();
			}
		}

		public void AddedToOrRemovedFromWorld(Actor self)
		{
			var building = self.TraitsImplementing<Building>().FirstEnabledTraitOrDefault();
			if (building != null)
				AddBuildingWithBlockStatusChange(self, building);
			else
				AddActorWithBlockStatusChange(self);
		}

		public void AddActorWithBlockStatusChange(Actor self)
		{
			if (self.OccupiesSpace != null && self.World.Map.Contains(self.Location))
				actorsWithBlockStatusChange
					.Add((self, MobileOffGrid.CellIsBlocked(world, locomotor, self.Location, BlockedByActor.Immovable)));
		}

		public void AddBuildingWithBlockStatusChange(Actor buildingActor, Building building)
		{
			var buildingOccupiedCells = building.OccupiedCells().Select(csc => csc.Item1).ToList();
			if (buildingOccupiedCells.Count > 0 && buildingOccupiedCells.All(c => world.Map.Contains(c)))
				buildingsWithBlockStatusChange
					.Add((building, buildingActor, MobileOffGrid.CellIsBlocked(world, locomotor, building.TopLeft, BlockedByActor.Immovable)));
		}
	}
}
