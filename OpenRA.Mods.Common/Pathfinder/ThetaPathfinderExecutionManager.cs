using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;
using OpenRA.Traits;
using RVO;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;


#pragma warning disable SA1108 // Block statements should not contain embedded comments

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Manages the queuing and prioritisation of Theta Pathfinder calculations, to ensure the computer is not overloded.")]

	public class ThetaPathfinderExecutionManagerInfo : TraitInfo<ThetaPathfinderExecutionManager> { }
	public class ThetaPathfinderExecutionManager : ITick, IResolveGroupedOrder, IWorldLoaded, NotBefore<ICustomMovementLayerInfo>
	{
		RVO.Circle rvoCircle;
		RVO.Blocks rvoBlocks;
		RVO.Roadmap rvoRoadmap;
		bool RVOtest = false;

		public class ThetaCircle
		{
			public struct SliceGroup
			{
				public int StartIndex;
				public int EndIndex;
				public SliceGroup(int startIndex, int endIndex)
				{
					StartIndex = startIndex;
					EndIndex = endIndex;
				}
			}

			public WPos CircleCenter;
			public WDist CircleRadius;
			public List<SliceGroup> SliceGroups = new();
			public bool SliceGroupsAreSet;

			public ThetaCircle(WPos circleCenter, WDist circleRadius, bool sliceGroupsSet = false)
			{
				CircleCenter = circleCenter;
				CircleRadius = circleRadius;
				SliceGroupsAreSet = sliceGroupsSet;
			}
		}

		public struct CircleSliceIndex
		{
			public PlayerCircleGroupIndex PlayerCI;
			public int CircleIndex;
			public int SliceIndex;

			public CircleSliceIndex(PlayerCircleGroupIndex playerCircleIndex, int circleIndex, int sliceIndex)
			{
				PlayerCI = playerCircleIndex;
				CircleIndex = circleIndex;
				SliceIndex = sliceIndex;
			}
		}
		public struct PlayerCircleGroupIndex
		{
			public Player PlayerOwner;
			public WPos PFTarget;

			public PlayerCircleGroupIndex(Player playerOwner, WPos pfTarget)
			{
				PlayerOwner = playerOwner;
				PFTarget = pfTarget;
			}
		}

		public struct ActorWithOrder
		{
			public Actor Actor;
			public WPos TargetPos;

			public ActorWithOrder(Actor actor, WPos targetPos)
			{
				Actor = actor;
				TargetPos = targetPos;
			}
		}

		// The number of expansions allowed across all Theta pathfinders
		World world;
		Locomotor locomotor;
		int currBcdId = 0;
		bool bcdSet = false;
		bool actorRemoved = false;
		public Dictionary<CPos, LinkedListNode<CPos>> AllCellNodes;
		public Dictionary<CPos, BasicCellDomain> AllCellBCDs;
		readonly int maxCurrExpansions = 500;
		readonly int radiusForSharedThetas = 1024 * 10;
		readonly int minDistanceForCircles = 0; // used to be 1024 * 28
		readonly int sliceAngle = 10;
		readonly int maxCircleSlices = 36;
		readonly Dictionary<PlayerCircleGroupIndex, List<ThetaCircle>> playerCircleGroups = new();
		public Dictionary<CircleSliceIndex, List<ActorWithOrder>> ActorOrdersInCircleSlices = new();
		public List<ThetaStarPathSearch> ThetaPFsToRun = new();
		List<(ThetaStarPathSearch, ThetaPFAction)> thetaPFActions = new();
		public bool PlayerCirclesLocked = false;

		public class LinkedListNode<T>
		{
			public LinkedListNode<T> Parent;
			public List<LinkedListNode<T>> Children = new();
			public Func<LinkedListNode<T>, LinkedListNode<T>, bool> IsValidParent;
			public T Value;

			public LinkedListNode(LinkedListNode<T> parent, T value, Func<T, T, bool> parentValidator)
			{
				Parent = parent;
				Value = value;
				IsValidParent = (LinkedListNode<T> parent, LinkedListNode<T> child) => parentValidator(parent.Value, child.Value);
			}

			public LinkedListNode(T value, Func<T, T, bool> parentValidator)
			{
				Value = value;
				IsValidParent = (LinkedListNode<T> parent, LinkedListNode<T> child) => parentValidator(parent.Value, child.Value);
			}

			public void AddChild(LinkedListNode<T> child) => Children.Add(child);
			public void AddChildren(List<LinkedListNode<T>> children) => Children.AddRange(children);
			public void SetChildren(List<LinkedListNode<T>> children) => Children = children;
			public void SetParent(LinkedListNode<T> parent) => Parent = parent;
			public void SetValue(T value) => Value = value;

			// Removes the parent and sets new parents for the children
			public LinkedListNode<T> RemoveParentAndReturnNewParent(List<LinkedListNode<T>> neighboursOfChildren)
			{
				Parent = null;
				if (Children.Count == 0)
					return null;

				// For each child, we run IsValidParent from that child to all other children to identify if they are
				// a valid parent and only keep them if they are valid. Then we sort the original list of children
				// by the highest number first (most valid children), to get the best candidates for parents.
				var bestCandidateParentWithChildren
					= Children.Select(parent => (parent, Children.Where(cx => cx != parent).Where(c2 => IsValidParent(parent, c2)).ToList()))
						.OrderByDescending(c => c.Item2.Count).FirstOrDefault();

				// First we set the best parent and assign all nearby children to it
				var childrenToReparent = Children;
				var childrenOfBestCandidate = bestCandidateParentWithChildren.Item2;
				for (var i = childrenOfBestCandidate.Count - 1; i >= 0; i--)
				{
					childrenToReparent.Remove(childrenOfBestCandidate[i]);
					childrenOfBestCandidate[i].Parent = bestCandidateParentWithChildren.parent;
				}

				// Next we check every single neighbour of every remaining child, to find a valid parent for all of them
				for (var i = childrenToReparent.Count - 1; i >= 0; i--)
				{
					foreach (var neighbour in neighboursOfChildren)
					{
						if (IsValidParent(neighbour, childrenToReparent[i]))
						{
							childrenToReparent[i].Parent = neighbour;
							childrenToReparent.RemoveAt(i);
							break;
						}
					}
				}

				// All children must point to a parent otherwise we cannot continue
				if (childrenToReparent.Count > 0)
					throw new InvalidOperationException($"Cannot find valid parent for children: " +
						$"{string.Join(",", childrenToReparent.ConvertAll(c => c.Value))}.");

				return bestCandidateParentWithChildren.parent;
			}
		}

		public class BasicCellDomain
		{
			public bool DomainIsBlocked;
			public Dictionary<CPos, LinkedListNode<CPos>> CellNodesDict = new();
			public LinkedListNode<CPos> Parent;
			public List<List<WPos>> CellEdges = new();
			public int ID;

			public BasicCellDomain() => ID = -1;
			public BasicCellDomain(int id) => ID = id;
			static bool ValidParent(CPos c1, CPos c2) => (c2 - c1).Length == 1;

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

			// Removes a cellNode edge from the list of edges. The order of positions does not matter
			public void RemoveEdge(List<WPos> edge) => RemoveEdge(edge[0], edge[1]);
			public void RemoveEdge(WPos p1, WPos p2)
			{
				CellEdges.RemoveAll(e => (e[0] == p1 && e[1] == p2) ||
										 (e[0] == p2 && e[1] == p1));
			}

			public void AddEdge(List<WPos> edge) => CellEdges.Add(edge);

			public List<CPos> CellNeighboursInBCD(Map map, CPos cell)
			{
				var neighbours = new List<CPos>();
				for (var x = -1; x <= 1; x++)
					for (var y = -1; y <= 1; y++)
						if (!(x == 0 && y == 0) && CellNodesDict.ContainsKey(new CPos(cell.X + x, cell.Y + y)))
							neighbours.Add(new CPos(cell.X + x, cell.Y + y));
				return neighbours;
			}

			public void RemoveEdgeIfNeighbourExists(Map map, CPos cell)
			{
				if (CellNodesDict.ContainsKey(new CPos(cell.X - 1, cell.Y)))
					RemoveEdge(map.LeftEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X + 1, cell.Y)))
					RemoveEdge(map.RightEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X, cell.Y - 1)))
					RemoveEdge(map.TopEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X, cell.Y + 1)))
					RemoveEdge(map.BottomEdgeOfCell(cell));
			}

			public void AddEdgeIfNeighbourExists(Map map, CPos cell)
			{
				if (CellNodesDict.ContainsKey(new CPos(cell.X - 1, cell.Y)))
					AddEdge(map.LeftEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X + 1, cell.Y)))
					AddEdge(map.RightEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X, cell.Y - 1)))
					AddEdge(map.TopEdgeOfCell(cell));
				if (CellNodesDict.ContainsKey(new CPos(cell.X, cell.Y + 1)))
					AddEdge(map.BottomEdgeOfCell(cell));
			}

			public void RemoveCell(Map map, CPos cell)
			{
				CellNodesDict.Remove(cell);
				AddEdgeIfNeighbourExists(map, cell);
			}

			public void RemoveParent(World world, Locomotor locomotor, LinkedListNode<CPos> parent,
				List<LinkedListNode<CPos>> neighboursOfChildren, HashSet<CPos> borderCells, ref int currBcdId,
				ref Dictionary<CPos, BasicCellDomain> allCellBCDs, BlockedByActor check = BlockedByActor.Immovable)
			{
				// We set the new parent and remove it from the BCD
				Parent = parent.RemoveParentAndReturnNewParent(neighboursOfChildren); // how do we know if the new parents are still part of the index?
				RemoveCell(world.Map, parent.Value);
				allCellBCDs[parent.Value] = null;

				// If there are no cells, there is no further action needed
				if (CellNodesDict.Count == 0)
					return;

				var origCellNodesDict = CellNodesDict;

				// We try to find all cells again from the new parent
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
						var candidateCellNodes = FindNodesFromStartNode(world, locomotor, remainingCellNodes.Values.FirstOrDefault(),
							remainingCellNodes.Values.ToList(), allCellBCDs, new HashSet<CPos>(), check).ToDictionary(c => c.Value, c => c);

						// Add this new set of cell nodes to a new domain, then increment the currBcdId
						var newBCD = CreateBasicCellDomainFromCellNodes(currBcdId, world, locomotor, candidateCellNodes.Values.ToList(), check);
						foreach (var cell in candidateCellNodes.Keys)
							allCellBCDs[cell] = newBCD;
						currBcdId++;

						// Remove the nodes that have recently been added to a new domain, and repeat the process with the remaining nodes
						remainingCellNodes = remainingCellNodes.Where(c => !candidateCellNodes.ContainsKey(c.Key)).ToDictionary(k => k.Key, k => k.Value);
					}
				}

				foreach (var cn in CellNodesDict.Values)
				{
					if (cn.Parent != null)
						MoveOffGrid.RenderLineWithColorCollDebug(world.WorldActor,
								world.Map.CenterOfCell(cn.Value), world.Map.CenterOfCell(cn.Parent.Value), colorToUse, 3, LineEndPoint.EndArrow);
					MoveOffGrid.RenderTextCollDebug(world.WorldActor, world.Map.CenterOfCell(cn.Value), $"ID:{allCellBCDs[cn.Value].ID}", colorToUse, "MediumBold");
				}
			}

			public void AddCellAndEdges(Map map, LinkedListNode<CPos> cellNode)
			{
				CellNodesDict.Add(cellNode.Value, cellNode);
				CellEdges.AddRange(Map.CellEdgeSet.FromCell(map, cellNode.Value).GetAllEdgesAsPosList());
			}

			// NOTE: Does NOT iterate through children of nodes, uses CPos instead and works off of the assumption that
			// any adjacent node sharing the same domain is traversable
			public static List<LinkedListNode<CPos>> FindNodesFromStartNode(World world, Locomotor locomotor, LinkedListNode<CPos> startNode,
				List<LinkedListNode<CPos>> targetNodes, Dictionary<CPos, BasicCellDomain> cellBCDs, HashSet<CPos> borderCells,
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
					if (cellBCDs[c] != null && cellBCDs[startNode.Value] != null)
						return cellBCDs[c].ID == cellBCDs[startNode.Value].ID;
					return false; // cannot match domain ID if one of the domain IDs is missing
				}

				while (nodesToExpand.Count > 0 && targetNodeCells.Count > 0)
				{
					var cn = nodesToExpand.Dequeue();
					if (targetNodeCells.Contains(cn.Value))
					{
						foundTargetNodes.Add(cn);
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

				var highestParent = cellNodeList.FirstOrDefault();
				while (highestParent.Parent != null)
					highestParent = highestParent.Parent;
				bcd.Parent = highestParent;

				foreach (var cellNode in cellNodeList)
				{
					bcd.AddCellAndEdges(map, cellNode);
					bcd.RemoveEdgeIfNeighbourExists(map, cellNode.Value);
				}

				return bcd;
			}

			// Creates a basic cellNode domain by traversing all cells that match its blocked status, in the N E S and W destinations
			// NOTE: This deliberately does not populate CellNodesDict or AllCellBCDs, to keep the logic distinct.
			// TO DO: Isolate further by not having this create the BasicCellDomain, but just the linked nodes, or something similar
			public static BasicCellDomain CreateBasicCellDomain(int currBcdId, World world, Locomotor locomotor, CPos cell,
				ref HashSet<CPos> visited, BlockedByActor check = BlockedByActor.Immovable)
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
				bcd.AddCellAndEdges(map, cellNode);

				while (cellsToExpandWithParent.Count > 0)
				{
					// NOTE: There is no need to add the Parent/Child since all children branch from the first parent
					var child = cellsToExpandWithParent.Dequeue();
					bcd.AddCellAndEdges(map, child);
					bcd.RemoveEdgeIfNeighbourExists(map, child.Value);

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

		enum ThetaPFAction { Add, Remove }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			locomotor = w.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
		}

		public bool GreaterThanMinDistanceForCircles(Actor actor, WPos targetPos)
		{ return (targetPos - actor.CenterPosition).LengthSquared > minDistanceForCircles * minDistanceForCircles; }

		public int GetOppositeSlice(int slice) { return (int)(((Fix64)slice + (Fix64)maxCircleSlices / (Fix64)2) % (Fix64)maxCircleSlices); }

		// Checks from the center of the circle outward to the end of the slice to see if any cellNode blocks it
		public bool SliceIsBlockedByCell(Actor self, WPos circleCenter, int sliceIndex)
		{
			var sliceAbsoluteAngle = (int)(sliceAngle * (sliceIndex - 0.5)); // We subtract 0.5 as we want the middle of the slice
			var move = new WVec(new WDist(radiusForSharedThetas), WRot.FromYaw(WAngle.FromDegrees(sliceAbsoluteAngle)));
			return self.Trait<MobileOffGrid>().HasCollidedWithCell(circleCenter, move, WDist.Zero, locomotor);
		}

		void ITick.Tick(Actor self) { Tick(self.World); }

		// Add all actors to their corresponding circles before performing any actions
		void IResolveGroupedOrder.ResolveGroupedOrder(Actor self, Order order)
		{
			foreach (var actor in order.GroupedActors)
				AddMoveOrder(actor, order.Target.CenterPosition, MobileOffGrid.GetGroupedActorsWithMobileOGs(order.GroupedActors.ToList()));
			PlayerCirclesLocked = false;
		}

		WPos GetUnblockedWPos(Actor self, World world, WPos checkPos)
		{
			var checkCCPos = ThetaStarPathSearch.GetNearestCCPos(world, checkPos);
			if (!ThetaStarPathSearch.CcinMap(checkCCPos, world) ||
				ThetaStarPathSearch.IsCellBlocked(self, locomotor, world.Map.CellContaining(checkPos), BlockedByActor.Immovable))
				checkCCPos = ThetaStarPathSearch.GetBestCandidateCCPos(self, world, locomotor, checkPos);
			else
				return checkPos;

			return world.Map.WPosFromCCPos(checkCCPos);
		}

		public void RemovePF(Actor actor) { RemovePF(actor, WPos.Zero); }

		public void RemovePF(Actor actor, WPos targetPos)
		{
			foreach (var thetaPF in ThetaPFsToRun)
				if (thetaPF.self == actor && (targetPos == WPos.Zero || thetaPF.Dest == targetPos))
					thetaPFActions.Add((thetaPF, ThetaPFAction.Remove));
		}

		public void AddMoveOrder(Actor actor, WPos targetPos, List<TraitPair<MobileOffGrid>> sharedMoveActors = null, bool secondThetaRun = false)
		{
			PlayerCirclesLocked = true;
			var world = actor.World;
			// Bypass circle logic if distance to target is small enough
			if (!GreaterThanMinDistanceForCircles(actor, targetPos) || sharedMoveActors == null)
			{
				var rawThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
																 targetPos)
				{
					running = true,
					ActorsSharingPF = new List<Actor>() { actor }
				};

				actor.Trait<MobileOffGrid>().CurrThetaSearch = rawThetaStarSearch;
				AddPF(rawThetaStarSearch);
			}
			else if (secondThetaRun || actor.CurrentActivity is MobileOffGrid.ReturnToCellActivity)
			{
				var rawThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
																 targetPos, 0)
				{
					running = true,
					ActorsSharingPF = new List<Actor>() { actor }
				};

				actor.Trait<MobileOffGrid>().CurrThetaSearch = rawThetaStarSearch;
				AddPF(rawThetaStarSearch);
			}
			else
			{
				// If no circle group exists for the player, generate one
				var playerCircleGroupIndex = new PlayerCircleGroupIndex(actor.Owner, targetPos);
				if (!playerCircleGroups.ContainsKey(playerCircleGroupIndex))
					playerCircleGroups[playerCircleGroupIndex] = new List<ThetaCircle>();
				if (playerCircleGroups[playerCircleGroupIndex].Count == 0)
					playerCircleGroups[playerCircleGroupIndex].Add(new ThetaCircle(MoveOffGrid.GetCenterOfUnits(sharedMoveActors), new WDist(radiusForSharedThetas)));

				// For the found Player Circle group, loop through each circle, attempting to find one that contains the actor
				var circleFound = false;
				for (var circleIndex = 0; circleIndex < playerCircleGroups[playerCircleGroupIndex].Count; circleIndex++)
				{
					var circle = playerCircleGroups[playerCircleGroupIndex].ElementAt(circleIndex);
					// Create a slice in the circle at the position of the actor and return the index of this slice
					var sliceIndex = CircleShape.CalcCircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																		actor.CenterPosition, sliceAngle);
					if (CircleShape.PosIsInsideCircle(circle.CircleCenter, circle.CircleRadius.Length, actor.CenterPosition) &&
						!SliceIsBlockedByCell(actor, circle.CircleCenter, sliceIndex) &&
						!actor.Trait<MobileOffGrid>().HasCollidedWithCell(actor.CenterPosition, circle.CircleCenter, locomotor))
					{
#if DEBUGWITHOVERLAY
						MoveOffGrid.RenderCircle(actor, circle.CircleCenter, circle.CircleRadius, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
						//Slice Line is the standard sliceAngle * index to get the slice
						var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
						MoveOffGrid.RenderLineWithColor(actor, sliceLine[0], sliceLine[1],
														Color.DarkBlue, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif

						var circleSliceIndex = new CircleSliceIndex(playerCircleGroupIndex, circleIndex, sliceIndex);
						if (!ActorOrdersInCircleSlices.ContainsKey(circleSliceIndex))
							ActorOrdersInCircleSlices[circleSliceIndex] = new List<ActorWithOrder>();
						ActorOrdersInCircleSlices[circleSliceIndex].Add(new ActorWithOrder(actor, targetPos));
						circleFound = true;
					}
				}

				// If no valid circle found, create one
				if (!circleFound)
				{
					// Create the circle
					playerCircleGroups[playerCircleGroupIndex].Add(new ThetaCircle(actor.CenterPosition, new WDist(radiusForSharedThetas)));
					var circle = playerCircleGroups[playerCircleGroupIndex].Last();
#if DEBUGWITHOVERLAY
					MoveOffGrid.RenderCircle(actor, circle.CircleCenter, circle.CircleRadius, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif
					var circleIndex = playerCircleGroups[playerCircleGroupIndex].Count - 1;
					var sliceIndex = CircleShape.CalcCircleSliceIndex(circle.CircleCenter, circle.CircleRadius.Length,
																		actor.CenterPosition, sliceAngle);
#if DEBUGWITHOVERLAY
					//Slice Line is the standard sliceAngle * index to get the slice
					var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
					MoveOffGrid.RenderLineWithColor(actor, sliceLine[0], sliceLine[1],
													Color.DarkBlue, ThetaStarPathfinderOverlay.OverlayKeyStrings.Circles);
#endif
					var circleSliceIndex = new CircleSliceIndex(playerCircleGroupIndex, circleIndex, sliceIndex);
					if (!ActorOrdersInCircleSlices.ContainsKey(circleSliceIndex))
						ActorOrdersInCircleSlices[circleSliceIndex] = new List<ActorWithOrder>();
					ActorOrdersInCircleSlices[circleSliceIndex].Add(new ActorWithOrder(actor, targetPos));
				}
			}
		}

		public void AddPF(ThetaStarPathSearch thetaPF) { thetaPFActions.Add((thetaPF, ThetaPFAction.Add)); }

		public List<WPos> GetSliceLine(WPos circleCenter, WDist circleRadius, int sliceAngle, int sliceIndex)
		{
			return new List<WPos>()
				{
					circleCenter,
					circleCenter + new WVec(circleRadius, WRot.FromYaw(WAngle.FromDegrees(sliceIndex * sliceAngle)))
				};
		}

		public void GenSharedMoveThetaPFs(World world)
		{
			// Do not generate shared moves until player circles have been unlocked (i.e. all player circle generation is complete)
			if (PlayerCirclesLocked)
				return;

			foreach (var (playerCircleGroupIndex, playerCircleGroup) in playerCircleGroups)
			{
				var player = playerCircleGroupIndex.PlayerOwner;
				foreach (var (circle, circleIndex) in playerCircleGroup.Select((value, index) => (value, index)))
				{
					// Disable this if you want to see the circles persist
					// MoveOffGrid.RemoveCircle(world, circle.CircleCenter, circle.CircleRadius);

					// The below actor is only used for SliceIsBlocked, and is guaranteed to exist since playerCircle
					// only exists
					var blockTestActor = ActorOrdersInCircleSlices.FirstOrDefault().Value.FirstOrDefault().Actor;

					// Generate slice groups (split at each collision)
					if (!circle.SliceGroupsAreSet)
					{
						var firstSliceIndex = 0;
						for (var sliceIndex = 0; sliceIndex < 360 / sliceAngle; sliceIndex++)
						{
							// var sliceLine = GetSliceLine(circle.CircleCenter, circle.CircleRadius, sliceAngle, sliceIndex);
							// MoveOffGrid.RenderLineWithColor(blockTestActor, sliceLine.ElementAt(0), sliceLine.ElementAt(1),
							//							 	Color.DarkGreen);
							// If the slice is blocked by a cellNode in either direction, then we create a separate group of actors for this ThetaPF, since
							// they are not going to be able to path through the cellNode.
							if (SliceIsBlockedByCell(blockTestActor, circle.CircleCenter, sliceIndex) ||
								SliceIsBlockedByCell(blockTestActor, circle.CircleCenter, GetOppositeSlice(sliceIndex)) ||
								sliceIndex == 360 / sliceAngle - 1)
							{
								circle.SliceGroups.Add(new ThetaCircle.SliceGroup(firstSliceIndex, sliceIndex));
								firstSliceIndex = sliceIndex + 1;
							}
						}
					}

					// Slice Groups are sets of one or more slices that are not blocked. Because they are not blocked,
					// they can share the same pathfinder, since we can guarantee that the units are able to follow
					// the same path
					foreach (var sliceGroup in circle.SliceGroups)
					{
						// Get Actors Within Slice Group
						var actorOrdersInSliceGroup = new List<ActorWithOrder>();
						for (var sliceIndex = sliceGroup.StartIndex; sliceIndex <= sliceGroup.EndIndex; sliceIndex++)
						{
							if (ActorOrdersInCircleSlices.ContainsKey(new CircleSliceIndex(playerCircleGroupIndex,
																					   circleIndex, sliceIndex)))
								actorOrdersInSliceGroup = actorOrdersInSliceGroup
															.Union(ActorOrdersInCircleSlices[new CircleSliceIndex(playerCircleGroupIndex,
																										  circleIndex, sliceIndex)]).ToList();
						}

						// Generate Average Theta PF for Slice Group if at least one actor order exists within it
						if (actorOrdersInSliceGroup.Count > 0)
						{
							var firstActorOrder = actorOrdersInSliceGroup[0];
							var avgSourcePosOfGroup = IEnumerableExtensions.Average(actorOrdersInSliceGroup
																					   .Select(ao => ao.Actor.CenterPosition));
							var thetaSourcePos = GetUnblockedWPos(firstActorOrder.Actor, world, avgSourcePosOfGroup);
							var newAvgThetaStarSearch = new ThetaStarPathSearch(firstActorOrder.Actor.World,
																			 firstActorOrder.Actor, thetaSourcePos,
																			 firstActorOrder.TargetPos);

							// Add Averaged Theta PF back to Actors, and to the GroupedThetaPF list
							var individualPFUsed = 0;
							foreach (var (actor, targetPos) in actorOrdersInSliceGroup.Select(ao => (ao.Actor, ao.TargetPos)))
							{
								var actorMobileOffGrid = actor.Trait<MobileOffGrid>();
								if (ThetaStarPathSearch.IsPathObservable(world, actor, locomotor, actor.CenterPosition, thetaSourcePos,
									actorMobileOffGrid.UnitHitShape, true, 1))
								{
									if (newAvgThetaStarSearch.ActorsSharingPF != null)
										newAvgThetaStarSearch.ActorsSharingPF.Add(actor);
									else
										newAvgThetaStarSearch.ActorsSharingPF = new List<Actor> { actor };
									actorMobileOffGrid.CurrThetaSearch = newAvgThetaStarSearch;
								}
								else
								{
									individualPFUsed++;
									var individualAvgThetaStarSearch = new ThetaStarPathSearch(actor.World, actor, actor.CenterPosition,
										targetPos)
									{
										running = true,
										ActorsSharingPF = new List<Actor> { actor }
									};

									actorMobileOffGrid.CurrThetaSearch = individualAvgThetaStarSearch;
									AddPF(individualAvgThetaStarSearch);
								}
							}

							// If all actors are using an individual pathfinder then we do not use the averaged pathfinder
							if (individualPFUsed < actorOrdersInSliceGroup.Count)
							{
								newAvgThetaStarSearch.running = true;
								AddPF(newAvgThetaStarSearch);
							}
						}
					}
				}
			}

			// IMPORTANT: Need to clear circles once done to avoid re-using pathfinders.
			// Since we have added all PFs to ThetaPFsToRun, the circle logic is no longer necessary.
			playerCircleGroups.Clear();
			ActorOrdersInCircleSlices.Clear();
		}

		public void RenderBCD(List<BasicCellDomain> domainList)
		{
			foreach (var bcd in domainList)
			{
				foreach (var cellNode in bcd.CellNodesDict.Values)
				{
					//MoveOffGrid.RenderCircleCollDebug(world.WorldActor, world.Map.CenterOfCell(cellNode), new WDist(512));
					Color colorToUse;
					if (bcd.DomainIsBlocked)
						colorToUse = Color.Red;
					else
						colorToUse = Color.Green;
					MoveOffGrid.RenderTextCollDebug(world.WorldActor, world.Map.CenterOfCell(cellNode.Value), $"ID:{bcd.ID}", colorToUse, "MediumBold");

					var parent = CPos.Zero;
					if (cellNode.Parent != null)
						parent = cellNode.Parent.Value;
					var child = cellNode.Value;
					//MoveOffGrid.RenderCircleCollDebug(world.WorldActor, world.Map.CenterOfCell(cellNode), new WDist(512));

					var arrowDist = WVec.Zero;
					if (parent != CPos.Zero)
						arrowDist = (world.Map.CenterOfCell(parent) - world.Map.CenterOfCell(child)) / 10 * 8;

					if (arrowDist != WVec.Zero)
						MoveOffGrid.RenderLineWithColorCollDebug(world.WorldActor,
							world.Map.CenterOfCell(child), world.Map.CenterOfCell(child) + arrowDist, colorToUse, 3, LineEndPoint.EndArrow);
				}

				foreach (var edge in bcd.CellEdges)
					MoveOffGrid.RenderLineCollDebug(world.WorldActor, edge[0], edge[1], 3);
			}
		}

		void Tick(World world)
		{
			var domainList = new List<BasicCellDomain>();
			var collDebugOverlay = world.WorldActor.TraitsImplementing<CollisionDebugOverlay>().FirstEnabledTraitOrDefault();

			if (!bcdSet && collDebugOverlay.Enabled)
			{
				world.ActorRemoved += RemovedFromWorld;
				var visited = new HashSet<CPos>();
				for (var x = 0; x < world.Map.MapSize.X; x++)
					for (var y = 0; y < world.Map.MapSize.Y; y++)
						if (!visited.Contains(new CPos(x, y)))
						{
							domainList.Add(BasicCellDomain.CreateBasicCellDomain(currBcdId, world, locomotor, new CPos(x, y), ref visited));
							currBcdId++;
						}

				AllCellNodes = domainList.SelectMany(domain => domain.CellNodesDict).ToDictionary(k => k.Key, k => k.Value);
				AllCellBCDs = domainList.SelectMany(domain => domain.CellNodesDict
									.Select(cell => new KeyValuePair<CPos, BasicCellDomain>(cell.Key, domain)))
								  .ToDictionary(k => k.Key, k => k.Value);

				RenderBCD(domainList);

				bcdSet = true;
			}

			//if (actorRemoved)
			//{
			//	collDebugOverlay.ClearTexts();
			//	collDebugOverlay.ClearLines();
			//	domainList = AllCellBCDs.Select(kv => kv.Value).Distinct().ToList();
			//	RenderBCD(domainList);
			//	actorRemoved = false;
			//}

			var rvoObject = rvoBlocks;

			// Call RVO Tick
			if (RVOtest && collDebugOverlay.Enabled && rvoObject == null)
			{
				RVO.Simulator.Instance.Clear();
				//rvoObject = new RVO.Circle();
				//rvoCircle = rvoObject;
				rvoObject = new RVO.Blocks();
				rvoBlocks = rvoObject;
			}
			else if (RVOtest && collDebugOverlay.Enabled)
			{
				collDebugOverlay.ClearCircles();
				collDebugOverlay.ClearLines();

				var agentPositions = rvoObject.getAgentPositions();
				var agentSpawnLocation = new WPos(world.Map.MapSize.X * 1024 / 2, world.Map.MapSize.Y * 1024 / 2, 0);

				var obstacleLines = RVO.Simulator.Instance.getObstacles().Select(o => (o.point_, o.next_.point_));
				foreach (var ol in obstacleLines)
					collDebugOverlay.AddLine(new WPos((int)ol.Item1.x(), (int)ol.Item1.y(), 0) + (WVec)agentSpawnLocation,
											 new WPos((int)ol.Item2.x(), (int)ol.Item2.y(), 0) + (WVec)agentSpawnLocation, 3);

				foreach (var agentPos in agentPositions)
				{
					var agentPosSpawn = new WPos((int)agentPos.x(), (int)agentPos.y(), 0) + (WVec)agentSpawnLocation;
					MoveOffGrid.RenderCircleColorCollDebug(world.WorldActor, agentPosSpawn, new WDist(300), Color.Purple, 3);
				}

				rvoObject.Tick();
			}

			// We only add or remove Theta PFs during tick cycle to ensure integrity is maintained
			foreach (var (thetaPF, action) in thetaPFActions)
				if (action == ThetaPFAction.Remove)
					ThetaPFsToRun.Remove(thetaPF);
				else if (action == ThetaPFAction.Add)
					ThetaPFsToRun.Add(thetaPF);
			thetaPFActions.Clear();

			// If there are new playerCircles to resolve, we resolve these first to populate ThetaPFsToRun.
			if (playerCircleGroups.Count > 0)
				GenSharedMoveThetaPFs(world);

			for (var i = ThetaPFsToRun.Count; i > 0; i--) // Iterate backwards since we may remove PFs that are no longer expanding
			{
				var thetaPF = ThetaPFsToRun[i - 1];
				foreach (var actor in thetaPF.ActorsSharingPF)
				{
					var actorMobileOG = actor.TraitsImplementing<MobileOffGrid>().FirstOrDefault(Exts.IsTraitEnabled);
					actorMobileOG.Overlay.AddText(actorMobileOG.CenterPosition, i.ToString(), Color.Yellow, (int)PersistConst.Never,
						key: OverlayKeyStrings.PFNumber);
				}

				if (thetaPF.running && !thetaPF.pathFound)
				{
					if (thetaPF.currDelayToRun == 0)
						thetaPF.Expand((int)Fix64.Ceiling((Fix64)maxCurrExpansions / (Fix64)ThetaPFsToRun.Count));
					else if (thetaPF.currDelayToRun > 0)
						thetaPF.currDelayToRun--; // keep subtracting the delay each tick until 0 is reached
				}
				else
				{
					thetaPF.currDelayToRun = -1;
					ThetaPFsToRun.RemoveAt(i - 1); // Remove if no longer expanding
				}
			}
		}

		public void RemovedFromWorld(Actor self)
		{
			// TO BE COMPLETED:
			if (self.OccupiesSpace != null)
			{
				var cellNode = AllCellNodes[self.Location];
				var cellBCD = AllCellBCDs[self.Location];
				var neighboursOfChildren = cellNode.Children.SelectMany(c =>
					cellBCD.CellNeighboursInBCD(self.World.Map, c.Value).ConvertAll(c2 => AllCellNodes[c2])).ToList();
				AllCellBCDs[self.Location].RemoveParent(self.World, locomotor, cellNode, neighboursOfChildren, new HashSet<CPos>(),
					ref currBcdId, ref AllCellBCDs);
			}
		}
	}
}
