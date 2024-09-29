using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;

namespace OpenRA.Mods.Common.Traits
{
	public class CellEdges
	{
		public List<List<WPos>> AllCellEdges = new();
		public List<List<WPos>> ConnectedCellEdges => GenerateConnectedCellEdges(AllCellEdges);

		// NOTE: This requires all cells to be loaded, if any cell is loaded afterwards this becomes invalid
		public void LoadEdges(Map map, LinkedListNode<CPos>[][] allCellNodes, bool clearFirst = true)
		{
			if (clearFirst)
				AllCellEdges.Clear();

			foreach (var col in allCellNodes)
				foreach (var rowCol in col)
					AddEdgeIfNoNeighbourExists(map, allCellNodes, rowCol.Value);
		}

		public void AddEdge(List<WPos> edge)
		{
			// Do not add an edge if it already exists
			if (!AllCellEdges.Any(e => (e[0] == edge[0] && e[1] == edge[1]) ||
									(e[0] == edge[1] && e[1] == edge[0])))
				AllCellEdges.Add(edge);
		}

		// Removes a cellNode edge from the list of edges. The order of positions does not matter
		public void RemoveEdge(List<WPos> edge) => RemoveEdge(edge[0], edge[1]);
		public void RemoveEdge(WPos p1, WPos p2)
		{
			AllCellEdges.RemoveAll(e => (e[0] == p1 && e[1] == p2) ||
									 (e[0] == p2 && e[1] == p1));
		}

		public void AddEdgeIfNoNeighbourExists(Map map, LinkedListNode<CPos>[][] allCellNodes, CPos cell)
		{
			var t = cell + new CVec(0, -1);
			var b = cell + new CVec(0, 1);
			var l = cell + new CVec(-1, 0);
			var r = cell + new CVec(1, 0);

			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(map.BottomEdgeOfCell(cell));
			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID != allCellNodes[cell.X][cell.Y].ID)
				AddEdge(map.RightEdgeOfCell(cell));
		}

		public void RemoveEdgeIfNeighbourExists(Map map, CPos cell, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellID = allCellNodes[cell.X][cell.Y].ID;

			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID == cellID)
				RemoveEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID == cellID)
				RemoveEdge(map.RightEdgeOfCell(cell));
			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID == cellID)
				RemoveEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID == cellID)
				RemoveEdge(map.BottomEdgeOfCell(cell));
		}

		public void AddEdgeIfNeighbourExists(Map map, CPos cell, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellID = allCellNodes[cell.X][cell.Y].ID;

			if (map.Contains(l) && allCellNodes[l.X][l.Y] != null && allCellNodes[l.X][l.Y].ID == cellID)
				AddEdge(map.LeftEdgeOfCell(cell));
			if (map.Contains(r) && allCellNodes[r.X][r.Y] != null && allCellNodes[r.X][r.Y].ID == cellID)
				AddEdge(map.RightEdgeOfCell(cell));
			if (map.Contains(t) && allCellNodes[t.X][t.Y] != null && allCellNodes[t.X][t.Y].ID == cellID)
				AddEdge(map.TopEdgeOfCell(cell));
			if (map.Contains(b) && allCellNodes[b.X][b.Y] != null && allCellNodes[b.X][b.Y].ID == cellID)
				AddEdge(map.BottomEdgeOfCell(cell));
		}

		public void AddCellEdges(Map map, LinkedListNode<CPos> cellNode, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			foreach (var edge in Map.CellEdgeSet.FromCell(map, cellNode.Value).GetAllEdgesAsPosList())
				AddEdge(edge);

			RemoveEdgeIfNeighbourExists(map, cellNode.Value, ref allCellNodes);
		}

		public void RemoveCellEdges(Map map, LinkedListNode<CPos> cellNode, ref LinkedListNode<CPos>[][] allCellNodes)
			=> AddEdgeIfNeighbourExists(map, cellNode.Value, ref allCellNodes);

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
		CollisionDebugOverlay collDebugOverlay;
		public bool DomainIsBlocked;
		HashSet<CPos> blockStatusUpdatedCells = new();
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
			Init();
		}

		public void Init()
		{
			cellEdges = new CellEdges();
			PopulateAllCellNodesAndEdges();
			RenderAllCells();
		}

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

		public static void AddHeadRemoveExisting(LinkedListNode<CPos> head, ref List<LinkedListNode<CPos>> heads)
		{
			heads.RemoveAll(h => h.Value == head.Value);
			heads.Add(head);
		}

		bool CellIsBlocked(CPos c) => MobileOffGrid.CellIsBlocked(world, locomotor, c, BlockedByActor.Immovable);

		// Removes the parent and sets new parents for the children
		public LinkedListNode<CPos> GetNewHeadAndUpdateChildren(LinkedListNode<CPos> parent, ref LinkedListNode<CPos>[][] allCellNodes)
		{
			if (parent.Children.Count == 0)
				return null;

			// For each child, we run IsValidParent from that child to all other children to identify if they are
			// a valid parent and only keep them if they are valid. Then we sort the original list of children
			// by the highest number first (most valid children), to get the best candidates for parents.
			var bestCandidateWithChildren
				= parent.Children.Select(c1 => (c1, parent.Children.Where(cx => !cx.ValueEquals(c1) && ValidParent(c1.Value, cx.Value)).ToList()))
					.OrderByDescending(c => c.Item2.Count).FirstOrDefault();

			// Update all valid children with their new parent and head
			var newParent = bestCandidateWithChildren.c1;
			newParent.Parent = null;
			newParent.Blocked = CellIsBlocked(newParent.Value);
			var newParentNewChildren = bestCandidateWithChildren.Item2;
			for (var i = bestCandidateWithChildren.Item2.Count - 1; i >= 0; i--)
			{
				var child = newParentNewChildren[i];
				child.Parent = newParent;
				child.Head = newParent;
				allCellNodes[child.Value.X][child.Value.Y] = child;
				newParent.Children.Add(child);
			}

			return newParent;
		}

		public List<LinkedListNode<CPos>> GetCellNodeChildren(LinkedListNode<CPos> cn)
			=> AllCellNodes[cn.Value.X][cn.Value.Y].Children;

		public void UpdateCellNode(LinkedListNode<CPos> cn) => AllCellNodes[cn.Value.X][cn.Value.Y] = cn;

		public void UpdateAllCellNodesWithNode(LinkedListNode<CPos> cellNode, ref LinkedListNode<CPos>[][] allCellNodes)
			=> cellNode.ActionOnEveryNodeToParent(
				cn =>
				{
					var cnChildren = GetCellNodeChildren(cn);
					for (var i = cnChildren.Count - 1; i >= 0; i--)
					{
						var child = cnChildren[i];
						child.Parent = cn;
						child.Head = cn.Head;
						cn.Children.Add(child);
					}

					UpdateCellNode(cn);
				});

		// NOTE: AddEdges cannot be done the way I have done it, because removing edges before the cells have been generated is flawed.
		public HashSet<CPos> RemoveParent(World world, Locomotor locomotor, LinkedListNode<CPos> parent, ref int currBcdId,
			ref LinkedListNode<CPos>[][] allCellNodes, ref List<LinkedListNode<CPos>> heads, ObjectRemoved objectRemoved,
			BlockedByActor check = BlockedByActor.Immovable)
		{
			// Only used for RenderDomain
			var visited = new HashSet<CPos>() { parent.Value };

			using var pt = new PerfTimer("RemoveParent2");
			var headToUse = parent.Head;

			// We do not need to update children without a head, as they are already a head
			var childrenToReparent = parent.Children.Where(c => c.Head != null).ToList();

			// If the cell's Head is null then it is the head and must assign a new head
			// Additionally, assign any children that are adjacent to the new head
			if (parent.Head == null)
			{
				headToUse = GetNewHeadAndUpdateChildren(parent, ref allCellNodes);
				// Do not update parent of recently parented children
				childrenToReparent = childrenToReparent.Where(c => !headToUse.Children.Select(c => c.Value).Contains(c.Value)).ToList();
				foreach (var c in childrenToReparent.ConvertAll(c => c.Value))
					visited.Add(c);
			}

			if (headToUse != null && headToUse.Blocked == null)
				throw new DataMisalignedException($"headToUse at cell {headToUse.Value} does not have a Blocked status");

			var allCellNodesCopy = allCellNodes;
			var parentIsBlocked = CellIsBlocked(parent.Value);

			// To check if the new parent's blocked status (CellIsBlocked(parent.Value)) is matching the existing blocked status
			// of another cell
			bool MatchingParentsNewBlockedStatus(CPos c) => allCellNodesCopy[c.X][c.Y].IsBlocked == parentIsBlocked;
			bool ValidParentAndValidBlockedStatus(CPos candidate, CPos parent)
				=> ValidParent(parent, candidate) && MatchingParentsNewBlockedStatus(candidate) &&
				!allCellNodesCopy[candidate.X][candidate.Y].Children.Contains(allCellNodesCopy[parent.X][parent.Y]) &&
				allCellNodesCopy[candidate.X][candidate.Y].Parent != allCellNodesCopy[parent.X][parent.Y] &&
				!allCellNodesCopy[parent.X][parent.Y].Children.Contains(allCellNodesCopy[candidate.X][candidate.Y]) &&
				allCellNodesCopy[parent.X][parent.Y].Parent != allCellNodesCopy[candidate.X][candidate.Y];

			// The removed parent node needs to latch onto a neighbouring node with matching blocked status
			var oldParentValidNeighbours = CellNeighbours(world.Map, parent.Value, ValidParentAndValidBlockedStatus);
			if (oldParentValidNeighbours.Count > 0)
			{
				var oldParentFirstNeighbour = oldParentValidNeighbours[0];
				var oldParentFirstNeighbourNode = allCellNodes[oldParentFirstNeighbour.X][oldParentFirstNeighbour.Y];
				parent.Parent = oldParentFirstNeighbourNode;
				parent.Head = oldParentFirstNeighbourNode.GetHead();
				oldParentFirstNeighbourNode.Children.Add(parent);
				visited.Add(oldParentFirstNeighbourNode.Value);
			}
			else
			{
				// Create new head if no valid neighbour exists
				parent.Parent = null;
				parent.Head = null;
				parent.Blocked = CellIsBlocked(parent.Value);
				parent.OwnID = currBcdId;
				currBcdId++;
				parent.Children.Clear();
				AddHeadRemoveExisting(parent, ref heads);
			}

			if (parent.Parent == parent)
				throw new DataMisalignedException($"Circular loop: parent's Parent {parent.Parent.Value} is equal to parent");

			if (parent.Head != null && parent.Head.Head != null)
				throw new DataMisalignedException("Head node cannot have its own Head");

			// If there are no children and no children
			if (childrenToReparent.Count == 0)
				return visited;

			//var possibleHeadsWithDist = new List<(LinkedListNode<CPos> Node, int Dist)>() { (headToUse, int.MaxValue) };
			//for (var i = childrenToReparent.Count - 1; i >= 0; i--)
			//{
			//	var currChild = childrenToReparent[i];
			//	if (currChild != null)
			//	{
			//		// Search for all possible heads
			//		// Initially this will be the original head only, but if this is not found, then currChild will
			//		// create a new head, which will then be added to the list of possible heads for the remaining
			//		// children to find.
			//		var possibleHeads = possibleHeadsWithDist.ConvertAll(h => h.Node);
			//		var foundHeadsWithDist = new List<(LinkedListNode<CPos> Node, int Dist)>();

			//		foreach (var head in possibleHeads)
			//		{
			//			var maybeFoundCurrChildWithDist = FindNodeFromNode(world, locomotor, head, currChild, ref allCellNodes,
			//				check: check, debugName: "_fromNewParent");
			//			if (maybeFoundCurrChildWithDist != null)
			//			{
			//				// Note that since FindNodeFromNode returns the origin not the target,
			//				// we need to add the head rather than the found node here
			//				var foundHeadWithDist = (head, (((LinkedListNode<CPos> Node, int Dist))maybeFoundCurrChildWithDist).Dist);
			//				foundHeadsWithDist.Add(foundHeadWithDist);

			//				// This updates all previous children of these nodes to point to the new node
			//				// UpdateAllCellNodesWithNode(foundHeadWithDist.Node, ref allCellNodes);
			//			}
			//		}

			//		if (foundHeadsWithDist.Count > 0)
			//			foundHeadsWithDist = foundHeadsWithDist.OrderBy(h => h.Dist).ToList(); // Get the closest head by sorting by distance
			//		else
			//		{
			//			// If no head is found, we must assign a new one.
			//			var newHeadWithDist = currChild.FindHead(CellIsBlocked(currChild.Value));
			//			var newHead = newHeadWithDist.Node;
			//			currChild.Head = newHead;
			//			newHead.OwnID = currBcdId;
			//			currBcdId++;
			//			AddHeadRemoveExisting(newHead, ref heads);

			//			// We add the new head to the list of possible heads usable by other children, then sort by the shortest distance
			//			possibleHeadsWithDist.Add(newHeadWithDist);
			//			// The below has a maximum of four items so this is not expensive. Only recalculate distance if not already found earlier
			//			possibleHeadsWithDist = possibleHeadsWithDist
			//				.OrderBy(h => h.Dist != int.MaxValue ? h.Dist : DistBetweenNodes(h.Node, currChild)).ToList();
			//		}
			//	}
			//}

			return visited;
		}

		public static List<LinkedListNode<CPos>> ReverseLinkedListNodes(LinkedListNode<CPos> head, ref LinkedListNode<CPos>[][] allCellNodes,
			ref List<LinkedListNode<CPos>> heads, CPos? stopPos = null)
		{
			head = new LinkedListNode<CPos>(head.Value, head.IsValidParent, head.OwnID, (bool)head.Blocked);
			var reversedNodes = new List<LinkedListNode<CPos>>
			{
				head
			};
			AddHeadRemoveExisting(head, ref heads);

			// Create list from head to dest or end of list
			var i = 0;
			var lastNewNode = head;
			var currNode = head.Parent;
			while (currNode != null && (stopPos == null || currNode.Value != stopPos) && i < 9000)
			{
				var newCurrNode = new LinkedListNode<CPos>(lastNewNode, head, currNode.Value, currNode.IsValidParent)
				{
					// Because the parents are all still valid paths to the head, we can preserve all children of all parents
					// Also make sure not to retain the parent as a child!
					Children = currNode.Children.Where(c => c.Value != lastNewNode.Value).ToList()
				};
				reversedNodes.Add(newCurrNode);
				allCellNodes[newCurrNode.Value.X][newCurrNode.Value.Y] = newCurrNode;
				currNode = currNode.Parent;
				lastNewNode = newCurrNode;
				i++;
			}

			if (i >= 90000)
				throw new DataMisalignedException($"An infinite loop exists between {currNode.Value} and {currNode.Parent.Value}.");

			return reversedNodes;
		}

		// For finding a single target node rather than multiple target nodes
		public static (LinkedListNode<CPos> Node, int Dist)? FindNodeFromNode(World world, Locomotor locomotor, LinkedListNode<CPos> startNode,
			LinkedListNode<CPos> targetNode, ref LinkedListNode<CPos>[][] allCellNodes, HashSet<CPos> borderCells = null,
			BlockedByActor check = BlockedByActor.Immovable, string debugName = "")
		{
			var foundNodes = FindNodesFromNode(world, locomotor, startNode, new List<LinkedListNode<CPos>>() { targetNode }, ref allCellNodes,
				borderCells, check, debugName);

			if (foundNodes != null && foundNodes.Count > 0)
				return foundNodes[0];
			else
				return null;
		}

		public static int DistBetweenNodes(LinkedListNode<CPos> a, LinkedListNode<CPos> b) => DistBetweenCPos(a.Value, b.Value);
		public static int DistBetweenCPos(CPos a, CPos b) => (b - a).LengthSquared;

		// NOTE: Does NOT iterate through children of nodes, uses CPos instead and works off of the assumption that
		// any adjacent node sharing the same domain is traversable
		// NOTE 2: You can find the shortest identified path (not necessarily absolute shortest) by traversing the
		// parents from the targetNode(s) back to startNode
		public static List<(LinkedListNode<CPos> Node, int Dist)> FindNodesFromNode(World world, Locomotor locomotor, LinkedListNode<CPos> startNode,
			List<LinkedListNode<CPos>> targetNodes, ref LinkedListNode<CPos>[][] allCellNodes, HashSet<CPos> borderCells = null,
			BlockedByActor check = BlockedByActor.Immovable, string debugName = "")
		{
			// NOTE: We do not need to reset the parent of startNode since its parent no longer exists
			int DistToStartNode(CPos c) => DistBetweenCPos(c, startNode.Value);

			using var pt = new PerfTimer("FindNodesFromStartNode" + debugName);
			var visited = new HashSet<CPos>();
			var foundTargetNodesWithLength = new List<(LinkedListNode<CPos> Node, int Dist)>();
			var targetNodeCells = targetNodes.ConvertAll(cn => cn.Value);

			var nodesToExpand = new List<(LinkedListNode<CPos> Node, int DistToStartNode)>
				{
					(startNode, DistToStartNode(startNode.Value))
				};
			visited.Add(startNode.Value);

			var allCellNodesReadOnly = allCellNodes;
			bool MatchingDomainID(CPos c)
			{
				var cNode = allCellNodesReadOnly[c.X][c.Y];
				if (cNode != null && startNode != null)
					return startNode.MatchesDomain(cNode);
				return false; // cannot match domain ID if one of the domain IDs is missing
			}

			var dist = 0;
			while (nodesToExpand.Count > 0 && targetNodeCells.Count > 0)
			{
				dist++;
				var cn = nodesToExpand[0].Node;

				if (cn.Value == new CPos(124, 25) || cn.Value == new CPos(124, 24))
					Console.WriteLine("This is the culprit node.");

				nodesToExpand.RemoveAt(0);
				if (targetNodeCells.Contains(cn.Value))
				{
					foundTargetNodesWithLength.Add((cn, dist));
					//cn.Parent?.Children.Add(cn); // Does not make sense, the node's Parent's Children must by definition already have cn
					targetNodeCells.Remove(cn.Value);
				}

				foreach (var cnd in GetCellsWithinDomain(world, locomotor, ref cn, startNode.IsValidParent, startNode.GetHead(), check, visited,
					MatchingDomainID, borderCells))
				{
					// We make sure to add the node at an index that is sorted by how far it is from the StartNode, so that we are
					// always able to take the closest node (we do not search far away locations until closer locations have been exhausted)
					var cndDistToStartNode = DistToStartNode(cnd.Value);
					var insertionIndex = nodesToExpand.ConvertAll(nd => nd.DistToStartNode).BinarySearch(cndDistToStartNode);
					if (insertionIndex < 0)
						insertionIndex = ~insertionIndex;
					nodesToExpand.Insert(insertionIndex, (cnd, cndDistToStartNode));
					visited.Add(cnd.Value);
				}
			}

			return foundTargetNodesWithLength;
		}

		public static List<LinkedListNode<CPos>> GetCellsWithinDomain(World world, Locomotor locomotor,
			ref LinkedListNode<CPos> cellNode, Func<LinkedListNode<CPos>, LinkedListNode<CPos>, bool> parentValidator,
			LinkedListNode<CPos> head = null, BlockedByActor check = BlockedByActor.Immovable, HashSet<CPos> visited = null,
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

			var cellsWithinDomain = new List<LinkedListNode<CPos>>();
			foreach (var c in candidateCells)
				if (map.CPosInMap(c) && cellMatchingCriteria(c) == cellMatchingCriteria(cell))
					cellsWithinDomain.Add(new LinkedListNode<CPos>(cellNode, head, c, parentValidator));

			return cellsWithinDomain;
		}

		// Creates a basic cellNode domain by traversing all cells that match its blocked status, in the N E S and W destinations
		// NOTE: This deliberately does not populate CellNodesDict or AllCellBCDs, to keep the logic distinct.
		// TO DO: Isolate further by not having this create the BasicCellDomain, but just the linked nodes, or something similar
		public static LinkedListNode<CPos> CreateBasicCellDomain(int currBcdId, World world, Locomotor locomotor, CPos cell,
			ref LinkedListNode<CPos>[][] allCellNodes, ref CellEdges cellEdges, ref HashSet<CPos> visited,
			BlockedByActor check = BlockedByActor.Immovable, string debugName = "")
		{
			static bool IsValidParent(LinkedListNode<CPos> a, LinkedListNode<CPos> b)
				=> Math.Abs(b.Value.X - a.Value.X) + Math.Abs(b.Value.Y - a.Value.Y) == 1 &&
				   !(a.Parent == b && b.Parent == a);

			using var pt = new PerfTimer("CreateBasicCellDomain" + debugName);
			var map = world.Map;
			var head = new LinkedListNode<CPos>(cell, IsValidParent, currBcdId, MobileOffGrid.CellIsBlocked(world, locomotor, cell, check));

			var cellsToExpandWithParent = new LinkedList<LinkedListNode<CPos>>();
			cellsToExpandWithParent.AddLast(head);
			visited.Add(cell);

			while (cellsToExpandWithParent.Count > 0)
			{
				// NOTE: There is no need to add the Parent/Child since all children branch from the first parent
				var child = cellsToExpandWithParent.Last();
				cellsToExpandWithParent.RemoveLast();
				allCellNodes[child.Value.X][child.Value.Y] = child;

				if (world.Map == null)
					throw new NullReferenceException("map is null");
				if (child == null)
					throw new NullReferenceException("child is null");
				if (allCellNodes == null)
					throw new NullReferenceException("allCellNodes is null");
				if (child.Head != null && child.Head.Head != null)
					throw new DataMisalignedException("Head node cannot have its own Head");

				cellEdges.AddCellEdges(map, child, ref allCellNodes);

				// INVARIANT: bcd is independent of the below method that obtains neighbouring cells
				foreach (var cn in GetCellsWithinDomain(world, locomotor, ref child, child.IsValidParent, head, check, visited))
				{
					cellsToExpandWithParent.AddLast(cn);
					visited.Add(cn.Value);
				}
			}

			return head;
		}

		public void InitializeAllCellNodes()
		{
			AllCellNodes = new LinkedListNode<CPos>[world.Map.MapSize.X][];
			for (var x = 0; x < world.Map.MapSize.X; x++)
				AllCellNodes[x] = new LinkedListNode<CPos>[world.Map.MapSize.Y];
		}

		public void PopulateAllCellNodesAndEdges()
		{
			InitializeAllCellNodes();
			// === Temporarily disable RemoveParent ===
			world.ActorRemoved += AddedToOrRemovedFromWorld;
			world.ActorAdded += AddedToOrRemovedFromWorld;
			var visited = new HashSet<CPos>();
			for (var x = 0; x < world.Map.MapSize.X; x++)
				for (var y = 0; y < world.Map.MapSize.Y; y++)
					if (!visited.Contains(new CPos(x, y)))
					{
						Heads.Add(CreateBasicCellDomain(currBcdId, world, locomotor, new CPos(x, y),
							ref AllCellNodes, ref cellEdges, ref visited));
						currBcdId++;
					}
		}

		public void Tick(Actor self)
		{
			if (blockStatusUpdatedCells.Count > 0)
			{
				var updatedCellNodes = new List<LinkedListNode<CPos>>();
				foreach (var c in blockStatusUpdatedCells)
					updatedCellNodes.Add(AllCellNodes[c.X][c.Y]);
				RenderCells(updatedCellNodes);
				blockStatusUpdatedCells.Clear();
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

		public static List<LinkedListNode<T>> GetAllHeads<T>(List<LinkedListNode<T>> nodeList)
		{
			var headList = new List<LinkedListNode<T>>();

			foreach (var node in nodeList)
				if (!headList.Contains(node.GetHead()))
					headList.Add(node.GetHead());

			return headList;
		}

		public void RenderAllCells() => RenderCells(AllCellNodes.SelectMany(c => c).ToList());

		public void RenderCells(List<LinkedListNode<CPos>> cellNodes)
		{
			foreach (var cellNode in cellNodes)
			{
				collDebugOverlay.AddOrUpdateBCDNode(
					new CollisionDebugOverlay.BCDCellNode(world, cellNode.ID, cellNode.ParentValIfExists, cellNode.Value, cellNode.IsBlocked));
			}

			var edgesToUse = cellEdges.ConnectedCellEdges.ToList();
			//var lineColour = Color.RandomColor();
			var lineColour = Color.LightBlue;
			foreach (var edge in edgesToUse)
				MoveOffGrid.RenderLineWithColorCollDebug(world.WorldActor, edge[0], edge[1], lineColour, 3, LineEndPoint.Circle);
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

		public HashSet<CPos> RemoveParentOfCellNode(LinkedListNode<CPos> cellNode, ObjectRemoved objectRemoved)
			=> RemoveParent(world, locomotor, cellNode, ref currBcdId, ref AllCellNodes, ref Heads, objectRemoved);

		public void ActorBlockedStatusChange(Actor self, bool pastBlockedStatus)
		{
			if (MobileOffGrid.CellIsBlocked(world, locomotor, self.Location, BlockedByActor.Immovable) != pastBlockedStatus)
			{
				var cellNode = AllCellNodes[self.Location.X][self.Location.Y];
				var objectRemoved = new ObjectRemoved(self.GetType().ToString(), self.Info.Name, self.ActorID.ToString());
				var visited = RemoveParentOfCellNode(cellNode, objectRemoved);
				foreach (var c in visited)
					blockStatusUpdatedCells.Add(c);
			}
		}

		public void BuildingBlockedStatusChange(Building self, bool pastBlockedStatus)
		{
			var cellsWithChangedBlockStatus = self.OccupiedCells().Where(c =>
					MobileOffGrid.CellIsBlocked(world.WorldActor, locomotor, c.Item1, BlockedByActor.Immovable) != pastBlockedStatus)
					.Select(csc => csc.Item1).ToList();

			foreach (var cell in cellsWithChangedBlockStatus)
			{
				var cellNode = AllCellNodes[cell.X][cell.Y];
				var objectRemoved = new ObjectRemoved(self.GetType().ToString(), self.Info.InstanceName, "");
				var visited = RemoveParentOfCellNode(cellNode, objectRemoved);
				foreach (var c in visited)
					blockStatusUpdatedCells.Add(c);
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
