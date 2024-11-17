using RVO;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OpenRA.Mods.Common.Traits
{
	using Obstacle = List<List<(int, List<WPos>)>>;

	public class CellEdges
	{
		World world;
		public int[] AllCellEdges;
		public int CellEdgeCols;
		public int CellEdgeRows;
		public Dictionary<int, int> IndexList = new();
		public int MinCol = int.MaxValue;
		public int MaxCol = 0;
		public int MinRow = int.MaxValue;
		public int MaxRow = 0;
		public CellEdges(World world)
		{
			this.world = world;
			InitializeAllCellEdges(world);
		}

		public CellEdges(World world, int[] allCellEdges)
			: this(world) => AllCellEdges = allCellEdges.ToArray();

		public CellEdges(CellEdges cellEdges)
			: this(cellEdges.world, cellEdges.AllCellEdges) { }

		public int CellTopEdge(int x, int y) => y * 2 * CellEdgeCols + x;
		public int CellBotEdge(int x, int y) => (y + 1) * 2 * CellEdgeCols + x;
		public int CellLeftEdge(int x, int y) => (y * 2 + 1) * CellEdgeCols + x;
		public int CellRightEdge(int x, int y) => (y * 2 + 1) * CellEdgeCols + x + 1;

		public int GetTopEdge(int x, int y) => AllCellEdges[CellTopEdge(x, y)];
		public int GetBotEdge(int x, int y) => AllCellEdges[CellBotEdge(x, y)];
		public int GetLeftEdge(int x, int y) => AllCellEdges[CellLeftEdge(x, y)];
		public int GetRightEdge(int x, int y) => AllCellEdges[CellRightEdge(x, y)];

		public List<int> GetAllEdges(int x, int y)
		{
			return new()
			{
				CellTopEdge(x, y),
				CellBotEdge(x, y),
				CellLeftEdge(x, y),
				CellRightEdge(x, y),
			};
		}

		public int GetNumberOfEdges()
		{
			var edgesOccupied = 0;
			foreach (var edge in AllCellEdges)
				if (edge == 1)
					edgesOccupied++;
			return edgesOccupied;
		}

		public List<(int Index, List<WPos> Edge)> AllEdgesToIndexWPosList()
		{
			var edges = new List<(int Index, List<WPos> Edge)>();
			for (var i = 0; i < AllCellEdges.Length; i++)
				if (AllCellEdges[i] == 1)
					edges.Add((i, EdgeToWPosList(i)));
			return edges;
		}

		public void UpdateIndexList(int index, int val)
		{
			if (val == 1)
				IndexList[index] = val;
			else
				IndexList.Remove(index);
		}

		public void UpdateMinMaxVars(int x, int y)
		{
			MinCol = MinCol > x ? x : MinCol;
			MinRow = MinRow > y ? y : MinRow;
			MaxCol = MaxCol < x ? x : MaxCol;
			MaxRow = MaxRow < y ? y : MaxRow;
		}

		public void SetTopEdge(int x, int y, int val)
		{
			var index = CellTopEdge(x, y);
			AllCellEdges[index] = val;
			UpdateIndexList(index, val);

			if (val == 1)
				UpdateMinMaxVars(x, y);
		}

		public void SetBotEdge(int x, int y, int val)
		{
			var index = CellBotEdge(x, y);
			AllCellEdges[index] = val;
			UpdateIndexList(index, val);

			if (val == 1)
				UpdateMinMaxVars(x, y);
		}

		public void SetLeftEdge(int x, int y, int val)
		{
			var index = CellLeftEdge(x, y);
			AllCellEdges[index] = val;
			UpdateIndexList(index, val);

			if (val == 1)
				UpdateMinMaxVars(x, y);
		}

		public void SetRightEdge(int x, int y, int val)
		{
			var index = CellRightEdge(x, y);
			AllCellEdges[index] = val;
			UpdateIndexList(index, val);

			if (val == 1)
				UpdateMinMaxVars(x, y);
		}

		public void SetAllEdges(int x, int y, int val)
		{
			var topIndex = CellTopEdge(x, y);
			var botIndex = CellBotEdge(x, y);
			var leftIndex = CellLeftEdge(x, y);
			var rightIndex = CellRightEdge(x, y);

			AllCellEdges[topIndex] = val;
			AllCellEdges[botIndex] = val;
			AllCellEdges[leftIndex] = val;
			AllCellEdges[rightIndex] = val;

			UpdateIndexList(topIndex, val);
			UpdateIndexList(botIndex, val);
			UpdateIndexList(leftIndex, val);
			UpdateIndexList(rightIndex, val);

			if (val == 1)
				UpdateMinMaxVars(x, y);
		}

		public void InitializeAllCellEdges(World world)
		{
			CellEdgeCols = world.Map.MapSize.X + 1;
			CellEdgeRows = world.Map.MapSize.Y + 1;
			MinRow = CellEdgeRows;
			MinCol = CellEdgeCols;
			MaxRow = 0;
			MaxCol = 0;
			AllCellEdges = new int[CellEdgeCols * CellEdgeRows * 2];
		}

		public void AddTopEdge(CPos cell) => SetTopEdge(cell.X, cell.Y, 1);
		public void AddBotEdge(CPos cell) => SetBotEdge(cell.X, cell.Y, 1);
		public void AddLeftEdge(CPos cell) => SetLeftEdge(cell.X, cell.Y, 1);
		public void AddRightEdge(CPos cell) => SetRightEdge(cell.X, cell.Y, 1);
		public void AddAllEdges(CPos cell) => SetAllEdges(cell.X, cell.Y, 1);
		public void RemoveTopEdge(CPos cell) => SetTopEdge(cell.X, cell.Y, 0);
		public void RemoveBotEdge(CPos cell) => SetBotEdge(cell.X, cell.Y, 0);
		public void RemoveLeftEdge(CPos cell) => SetLeftEdge(cell.X, cell.Y, 0);
		public void RemoveRightEdge(CPos cell) => SetRightEdge(cell.X, cell.Y, 0);
		public void RemoveAllEdges(CPos cell) => SetAllEdges(cell.X, cell.Y, 0);

		public void RemoveEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellBlocked = allCellBCDs[cell.X, cell.Y].DomainIsBlocked;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].DomainIsBlocked == cellBlocked)
				RemoveLeftEdge(cell);

			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].DomainIsBlocked == cellBlocked)
				RemoveRightEdge(cell);

			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].DomainIsBlocked == cellBlocked)
				RemoveTopEdge(cell);

			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].DomainIsBlocked == cellBlocked)
				RemoveBotEdge(cell);
		}

		public void AddEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellBlocked = allCellBCDs[cell.X, cell.Y].DomainIsBlocked;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].DomainIsBlocked == cellBlocked)
				AddLeftEdge(cell);
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].DomainIsBlocked == cellBlocked)
				AddRightEdge(cell);
			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].DomainIsBlocked == cellBlocked)
				AddTopEdge(cell);
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].DomainIsBlocked == cellBlocked)
				AddBotEdge(cell);
		}

		public void AddCellEdges(Map map, DomainNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
		{
			AddLeftEdge(cellNode.Value);
			AddRightEdge(cellNode.Value);
			AddTopEdge(cellNode.Value);
			AddBotEdge(cellNode.Value);

			RemoveEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);
		}

		public void RemoveCellEdges(Map map, DomainNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
			=> AddEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);

		public List<List<WPos>> GenerateCellEdges(Map map)
		{
			var edges = new List<List<WPos>>();

			for (var i = 0; i < CellEdgeRows * CellEdgeCols; i++)
			{
				var x = i % CellEdgeCols;
				var y = i / CellEdgeCols;

				if (GetTopEdge(x, y) == 1)
					edges.Add(map.TopEdgeOfCell(new CPos(x, y)));
				if (GetBotEdge(x, y) == 1)
					edges.Add(map.BottomEdgeOfCell(new CPos(x, y)));
				if (GetLeftEdge(x, y) == 1)
					edges.Add(map.LeftEdgeOfCell(new CPos(x, y)));
				if (GetRightEdge(x, y) == 1)
					edges.Add(map.RightEdgeOfCell(new CPos(x, y)));
			}

			return edges;
		}

		public enum CellFromIndex { TopLeft, BotRight }

		public int GetXFromIndex(int index, CellFromIndex cellFromIndex)
			=> index % CellEdgeCols + (cellFromIndex == CellFromIndex.TopLeft ? 0 : 1);

		public int GetYFromIndex(int index, CellFromIndex cellFromIndex, bool useCellIndex = true)
			=> (index - GetXFromIndex(index, cellFromIndex)) / ((useCellIndex ? 2 : 1) * CellEdgeCols);

		public bool IsVerticalEdge(int index) => (index - index % CellEdgeCols) / CellEdgeCols % 2 == 1;

		public List<WPos> EdgeToWPosList(int index)
		{
			var (topLeft, botRight) = EdgeToWPos(index);
			return new List<WPos> { topLeft, botRight };
		}

		public (WPos TopLeft, WPos BotRight) EdgeToWPos(int index)
		{
			int x1;
			int x2;
			int y1;
			int y2;

			if (IsVerticalEdge(index))
			{
				x1 = GetXFromIndex(index, CellFromIndex.TopLeft) * 1024;
				x2 = x1;
				y1 = GetYFromIndex(index, CellFromIndex.TopLeft) * 1024;
				y2 = y1 + 1024;
			}
			else
			{
				x1 = GetXFromIndex(index, CellFromIndex.TopLeft) * 1024;
				x2 = x1 + 1024;
				y1 = GetYFromIndex(index, CellFromIndex.TopLeft) * 1024;
				y2 = y1;
			}

			return (new WPos(x1, y1, 0), new WPos(x2, y2, 0));
		}

		public readonly struct IndicesWithEndPoint
		{
			public readonly List<int> Indices;
			public readonly WPos EndPoint;

			public IndicesWithEndPoint(List<int> indices, WPos endPoint)
			{
				Indices = indices;
				EndPoint = endPoint;
			}
		}

		public List<IndicesWithEndPoint> GetNeighbourIndices(int index, WPos? limitToPos = null)
			=> GetNeighbourIndices(index, ref AllCellEdges, limitToPos);

		public List<IndicesWithEndPoint> GetNeighbourIndices(int index, ref int[] allCellEdges, WPos? limitToPos = null)
		{
			var visited = new HashSet<int>();
			return GetNeighbourIndices(index, ref visited, ref allCellEdges, limitToPos);
		}

		public List<IndicesWithEndPoint> GetNeighbourIndices(int index, ref HashSet<int> visited, ref int[] allCellEdges,
			WPos? limitToPos = null)
		{
			var indicesWithEndPoints = new List<IndicesWithEndPoint>();
			var edgeWPos = EdgeToWPos(index);

			// We need to exclude any neighbours that are starting from a position that has already been traversed
			if (limitToPos != null)
				limitToPos = (WPos)limitToPos;

			var x = GetXFromIndex(index, CellFromIndex.TopLeft);

			/* Vertical Edge
			 *	   N
			 *	NW   NE
			 *	   x
			 *	SW   SE
			 *	   S
			 *
			 *	Example:
			 *       1,1
			 *    0,2   1,2
			 *       1,3
			 *    0,4   1,4
			 *       1,5
			 *
			 */

			// NOTE: We will exclude either the Northern edges or Southern edges for a vertical edge,
			// or the Western edges or Eastern edges for a horizontal edge, depending on which side
			// has the starting point of the recently traversed edge. This is to prevent the traversal from
			// going backwards, since the starting point is at the rear of the edge, any edges extending
			// from this point are extending from the rear, not the front. We do this by limiting the edges
			// returned to this originating from a particular point in the current edge, either the top or left
			// point, or the bottom or right point.

			// INVARIANT: the new currEdgeStartPos will be the existing currEdgeEndPos
			if (IsVerticalEdge(index))
			{

				// Northern edges
				if (index > CellEdgeCols && (limitToPos == null || limitToPos == edgeWPos.TopLeft))
				{
					var neighbourIndices = new List<int>();

					var neHorEdge = index - CellEdgeCols;
					if (allCellEdges[neHorEdge] == 1 && !visited.Contains(neHorEdge))
						neighbourIndices.Add(neHorEdge); // add NE horizontal edge

					var nwHorEdge = index - CellEdgeCols - 1;
					if (x > 0 && allCellEdges[nwHorEdge] == 1 && !visited.Contains(nwHorEdge))
						neighbourIndices.Add(nwHorEdge); // add NW vertical edge

					var nVertEdge = index - 2 * CellEdgeCols;
					if (index > 2 * CellEdgeCols && allCellEdges[nVertEdge] == 1 && !visited.Contains(nVertEdge))
						neighbourIndices.Add(nVertEdge); // add N vertical edge

					// Add the origin position edgeWPos.TopLeft, so that these edges can be excluded from future searches
					if (neighbourIndices.Count > 0)
						indicesWithEndPoints.Add(new IndicesWithEndPoint(neighbourIndices, edgeWPos.TopLeft));
				}

				// Southern edges
				if (index < 2 * CellEdgeCols * (CellEdgeRows - 1)
					&& (limitToPos == null || limitToPos == edgeWPos.BotRight)) // at least 1 row available at the bottom
				{
					var neighbourIndices = new List<int>();

					var seHorEdge = index + CellEdgeCols;
					if (allCellEdges[seHorEdge] == 1 && !visited.Contains(seHorEdge))
						neighbourIndices.Add(seHorEdge); // add SE horizontal edge

					var swHorEdge = index + CellEdgeCols - 1;
					if (x > 0 && allCellEdges[swHorEdge] == 1 && !visited.Contains(swHorEdge))
						neighbourIndices.Add(swHorEdge); // add SW vertical edge

					var sVertEdge = index + 2 * CellEdgeCols;
					// below we check that at least 2 rows are available at the bottom
					if (index < 2 * CellEdgeCols * (CellEdgeRows - 2) && allCellEdges[sVertEdge] == 1 && !visited.Contains(sVertEdge))
						neighbourIndices.Add(sVertEdge); // add S vertical edge

					// Add the origin position edgeWPos.TopLeft, so that these edges can be excluded from future searches
					if (neighbourIndices.Count > 0)
						indicesWithEndPoints.Add(new IndicesWithEndPoint(neighbourIndices, edgeWPos.BotRight));
				}
			}

			/* Horizontal Edge
				 *	   NW    NE
				 *  W      x      E
				 *     SW    SE
				 *
				 * Example:
				 *      1,1    2,1
				 *   0,2    1,2    2,2
				 *      1,3    2,3

			*/
			else // it is a horizontal edge
			{
				// Western Edges
				if (x > 0 && (limitToPos == null || limitToPos == edgeWPos.TopLeft))
				{
					var neighbourIndices = new List<int>();

					var wVertEdge = index - 1;
					if (allCellEdges[wVertEdge] == 1 && !visited.Contains(wVertEdge))
						neighbourIndices.Add(wVertEdge); // W horizontal edge

					var nwVertEdge = index - CellEdgeCols;
					if (index > CellEdgeCols && allCellEdges[nwVertEdge] == 1 && !visited.Contains(nwVertEdge)) // if not the top horizontal edge
						neighbourIndices.Add(nwVertEdge); // add NW vertical edge

					var swVertEdge = index + CellEdgeCols;
					if (index < CellEdgeCols * (2 * CellEdgeRows - 1) && allCellEdges[swVertEdge] == 1 && !visited.Contains(swVertEdge)) // if not the bottom horizontal edge
						neighbourIndices.Add(swVertEdge); // add SW vertical edge

					if (neighbourIndices.Count > 0)
						indicesWithEndPoints.Add(new IndicesWithEndPoint(neighbourIndices, edgeWPos.TopLeft));
				}

				// Eastern Edges
				if (x < CellEdgeCols && (limitToPos == null || limitToPos == edgeWPos.BotRight))
				{
					var neighbourIndices = new List<int>();

					var eVertEdge = index + 1;
					if (allCellEdges[eVertEdge] == 1 && !visited.Contains(eVertEdge))
						neighbourIndices.Add(eVertEdge); // E horizontal edge

					var neVertEdge = index - CellEdgeCols + 1;
					if (index > CellEdgeCols && allCellEdges[neVertEdge] == 1 && !visited.Contains(neVertEdge)) // if not the top horizontal edge
						neighbourIndices.Add(index - CellEdgeCols + 1); // add NE vertical edge

					var seVertEdge = index + CellEdgeCols + 1;
					if (index < CellEdgeCols * (2 * CellEdgeRows - 1) && allCellEdges[seVertEdge] == 1 && !visited.Contains(seVertEdge)) // if not the bottom horizontal edge
						neighbourIndices.Add(index + CellEdgeCols + 1); // add SE vertical edge

					if (neighbourIndices.Count > 0)
						indicesWithEndPoints.Add(new IndicesWithEndPoint(neighbourIndices, edgeWPos.BotRight));
				}
			}

			return indicesWithEndPoints;
		}

		public enum EdgeMaskOp { And, Or }

		public void ApplyEdgeMask(CellEdges mask, EdgeMaskOp op)
		{
			for (var i = 0; i < AllCellEdges.Length; i++)
			{
				var val = op == EdgeMaskOp.Or ? AllCellEdges[i] | mask.AllCellEdges[i] : AllCellEdges[i] & mask.AllCellEdges[i];
				AllCellEdges[i] = val;
				if (val == 1)
					IndexList[i] = 1;
				else if (IndexList.ContainsKey(i)) // val must be 0, so we check if the index exists and only remove if it does
					IndexList.Remove(i);
			}
		}

		public int GetAnyIndex(Dictionary<int, int> dict)
		{
			using (var iter = dict.GetEnumerator())
				return iter.MoveNext() ? iter.Current.Key : -1;
		}

		public void AddObstacleEdges(Obstacle obstacle)
		{
			foreach (var edgeSet in obstacle)
			{
				foreach (var (index, _) in edgeSet)
				{
					AllCellEdges[index] = 1;
					IndexList[index] = 1;
				}
			}
		}

		public Obstacle GenerateObstacleEdgeSets(Map map, BasicCellDomain bcd, CellEdges cellEdgeMask)
		{
			var edgeSets = new Obstacle();
			var edges = new List<(int, List<WPos>)>();
			var visitedEdgeIndices = new HashSet<int>();
			ApplyEdgeMask(cellEdgeMask, EdgeMaskOp.And);
			var indexListCopy = IndexList.ToDictionary(x => x.Key, x => x.Value);

			//var cellEdgesWithMask = bcd.CellEdgesMask;

			var currEdgeIndex = GetAnyIndex(indexListCopy);
			var currEdgeStartPos = EdgeToWPos(currEdgeIndex).TopLeft;
			edges.Add((currEdgeIndex, EdgeToWPosList(currEdgeIndex)));

			while (indexListCopy.Count > 0)
			{
				var (currEdgeTopLeft, currEdgeBotRight) = EdgeToWPos(currEdgeIndex);
				var currEdgeEndPos = currEdgeStartPos == currEdgeTopLeft ? currEdgeBotRight : currEdgeTopLeft;

				var currEdgeNeighbours = GetNeighbourIndices(currEdgeIndex, ref visitedEdgeIndices,
					//ref cellEdgesWithMask.AllCellEdges,
					ref AllCellEdges,
					currEdgeEndPos);

				if (currEdgeNeighbours.Count == 0)
				{
					edgeSets.Add(edges.ToList());
					edges.Clear();
					currEdgeIndex = GetAnyIndex(indexListCopy);
					currEdgeStartPos = EdgeToWPos(currEdgeIndex).TopLeft;

					if (currEdgeIndex == -1)
						break;

					//if (visitedEdgeIndices.Contains(currEdgeIndex))
					//	throw new Exception("Cannot add two of the same index!");

					indexListCopy.Remove(currEdgeIndex);
					edges.Add((currEdgeIndex, EdgeToWPosList(currEdgeIndex)));
					visitedEdgeIndices.Add(currEdgeIndex);
					continue;
				}

				var neighbourIndices = currEdgeNeighbours[0].Indices;

				if (neighbourIndices.Count > 1) // Was .Count > 1
				{
					var testIndex = -1;
					if (neighbourIndices.Count >= 4)
						throw new DataMisalignedException($"Cannot have {neighbourIndices.Count} edges! Can only be 2 or 3.");

					if (IsVerticalEdge(currEdgeIndex))
					{
						var rightCell = new CPos(GetXFromIndex(currEdgeIndex, CellFromIndex.TopLeft),
							GetYFromIndex(currEdgeIndex, CellFromIndex.TopLeft));
						var leftCell = rightCell + new CVec(-1, 0);
						if (map.Contains(leftCell) && bcd.CellNodesDict.ContainsKey(leftCell))
						{
							// If edge is pointing down, we take the top edge of the left cell, otherwise we take the bottom edge of
							// the left cell. Since it is a vertical edge one pos must be higher than the other
							if (currEdgeStartPos.Y < currEdgeEndPos.Y)
								testIndex = CellBotEdge(leftCell.X, leftCell.Y);
							else
								testIndex = CellTopEdge(leftCell.X, leftCell.Y);
						}
						else if (map.Contains(rightCell) && bcd.CellNodesDict.ContainsKey(rightCell))
						{
							if (currEdgeStartPos.Y < currEdgeEndPos.Y)
								testIndex = CellBotEdge(rightCell.X, rightCell.Y);
							else
								testIndex = CellTopEdge(rightCell.X, rightCell.Y);
						}
					}
					else // Horizontal Edge
					{
						var botCell = new CPos(GetXFromIndex(currEdgeIndex, CellFromIndex.TopLeft),
							GetYFromIndex(currEdgeIndex, CellFromIndex.TopLeft));
						var topCell = botCell + new CVec(0, -1);
						if (map.Contains(topCell) && bcd.CellNodesDict.ContainsKey(topCell))
						{
							// If edge is pointing left, we take the left edge of the top cell, otherwise we take the right edge of
							// the top cell. Since it is a horizontal edge one pos must be to the left of the other
							if (currEdgeStartPos.X < currEdgeEndPos.X)
								testIndex = CellRightEdge(topCell.X, topCell.Y);
							else
								testIndex = CellLeftEdge(topCell.X, topCell.Y);
						}

						if (map.Contains(botCell) && bcd.CellNodesDict.ContainsKey(botCell))
						{
							if (currEdgeStartPos.X < currEdgeEndPos.X)
								testIndex = CellRightEdge(botCell.X, botCell.Y);
							else
								testIndex = CellLeftEdge(botCell.X, botCell.Y);
						}
					}

					//if (testIndex == -1)
					//	throw new DataMisalignedException("Impossible state: No test index found despite >1 valid neighbours");

					foreach (var ind in neighbourIndices)
						if (ind == testIndex)
						{
							currEdgeIndex = testIndex;
							currEdgeStartPos = currEdgeNeighbours[0].EndPoint;

							//if (visitedEdgeIndices.Contains(currEdgeIndex))
							//	throw new Exception("Cannot add two of the same index!");

							visitedEdgeIndices.Add(currEdgeIndex);
							indexListCopy.Remove(currEdgeIndex);
							edges.Add((currEdgeIndex, EdgeToWPosList(currEdgeIndex)));
						}
				}
				else
				{
					currEdgeIndex = currEdgeNeighbours[0].Indices[0];
					currEdgeStartPos = currEdgeNeighbours[0].EndPoint;

					//if (visitedEdgeIndices.Contains(currEdgeIndex))
					//	throw new Exception("Cannot add two of the same index!");

					visitedEdgeIndices.Add(currEdgeIndex);
					indexListCopy.Remove(currEdgeIndex); // We exclude indices so they can no longer be used for a domain
					edges.Add((currEdgeIndex, EdgeToWPosList(currEdgeIndex)));
				}
			}

			if (edges.Count > 0)
				edgeSets.Add(edges.ToList());

			return edgeSets;
		}

		public List<List<WPos>> GenerateConnectedCellEdges(Map map, int xMin = 0, int xMax = -1, int yMin = 0, int yMax = -1)
		{
			var x = xMin;
			var y = yMin;

			if (xMax == -1)
				xMax = CellEdgeCols;

			if (yMax == -1)
				yMax = CellEdgeRows;

			var endX = 0;
			var endY = new int[CellEdgeCols];

			var edges = new List<List<WPos>>();

			while (y < yMax)
			{
				while (x < xMax)
				{
					var currCell = new CPos(x, y);

					// We only check if another endpoint exists if we have passed the last assigned endpoint (endX)
					if (x > endX)
					{
						endX = x;

						// If both the current and next cell are occupied, then we extend the end cell further
						if (GetTopEdge(x, y) == 1 && endX + 1 < xMax)
							while (endX + 1 < yMax && GetTopEdge(endX + 1, y) == 1 &&
								(y == yMin || (GetRightEdge(endX, y) == 0 && GetRightEdge(endX, y - 1) == 0))) // confirm there is no intersection
								endX++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (endX != x)
							edges.Add(new List<WPos>() { map.TopLeftOfCell(currCell), map.TopRightOfCell(new CPos(endX, y)) });
						else if (GetTopEdge(x, y) == 1) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.TopEdgeOfCell(currCell));
					}

					// Same logic as endX above
					if (y > endY[x])
					{
						endY[x] = y;

						// If both the current and next cell are occupied, then we extend the end cell further
						endY[x] = y;
						if (GetLeftEdge(x, y) == 1 && endY[x] + 1 < yMax)
							while (endY[x] + 1 < yMax && GetLeftEdge(x, endY[x] + 1) == 1 &&
								(x == xMin || (GetBotEdge(x, endY[x]) == 0 && GetBotEdge(x - 1, endY[x]) == 0))) // confirm there is no intersection
								endY[x]++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (endY[x] != y)
							edges.Add(new List<WPos>() { map.TopLeftOfCell(currCell), map.BottomLeftOfCell(new CPos(x, endY[x])) });
						else if (GetLeftEdge(x, y) == 1) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.LeftEdgeOfCell(currCell));
					}

					x++;
				}

				y++;
				x = xMin;
				endX = xMin;
			}

			return edges;
		}
	}
}
