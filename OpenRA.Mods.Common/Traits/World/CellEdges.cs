using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenRA.Mods.Common.Traits
{

	public class CellEdges
	{
#pragma warning disable IDE1006 // Naming Styles
		const int le = 0b0001;
		const int re = 0b0010;
		const int te = 0b0100;
		const int be = 0b1000;
#pragma warning restore IDE1006 // Naming Styles

		public int[] AllCellEdges;
		public int CPosCols;
		public int CPosRows;

		public CellEdges(World world) => InitializeAllCellEdges(world);

		// col = x, row = y
		int Get(int col, int row) => AllCellEdges[row * CPosCols + col];
		void Set(int col, int row, int val) => AllCellEdges[row * CPosCols + col] = val;
		void Add(int col, int row, int val) => AllCellEdges[row * CPosCols + col] |= val;
		void Remove(int col, int row, int val) => AllCellEdges[row * CPosCols + col] &= ~val;

		public void InitializeAllCellEdges(World world)
		{
			CPosCols = world.Map.MapSize.X;
			CPosRows = world.Map.MapSize.Y;
			AllCellEdges = new int[CPosCols * CPosRows];
		}

		// NOTE: This requires all cells to be loaded, if any cell is loaded afterwards this becomes invalid
		public void LoadEdges(World world, ref BasicCellDomain[,] allCellBCDs, List<DomainNode<CPos>> cellNodes, bool clearFirst = true)
		{
			if (clearFirst)
				InitializeAllCellEdges(world);

			foreach (var cell in cellNodes)
				AddEdgeIfNoNeighbourExists(world.Map, allCellBCDs, cell.Value);
		}

		public void AddEdge(CPos cell, int edge) => Add(cell.X, cell.Y, edge);

		// Removes a cellNode edge from the list of edges. The order of positions does not matter
		public void RemoveEdge(CPos cell, int edge) => Remove(cell.X, cell.Y, edge);

		public void AddEdgeIfNoNeighbourExists(Map map, BasicCellDomain[,] allCellBCDs, CPos cell)
		{
			var t = cell + new CVec(0, -1);
			var b = cell + new CVec(0, 1);
			var l = cell + new CVec(-1, 0);
			var r = cell + new CVec(1, 0);

			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null &&
				allCellBCDs[t.X, t.Y].DomainIsBlocked != allCellBCDs[cell.X, cell.Y].DomainIsBlocked)
				AddEdge(cell, te);
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null &&
				allCellBCDs[b.X, b.Y].DomainIsBlocked != allCellBCDs[cell.X, cell.Y].DomainIsBlocked)
				AddEdge(cell, be);
			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null &&
				allCellBCDs[l.X, l.Y].DomainIsBlocked != allCellBCDs[cell.X, cell.Y].DomainIsBlocked)
				AddEdge(cell, le);
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null &&
				allCellBCDs[r.X, r.Y].DomainIsBlocked != allCellBCDs[cell.X, cell.Y].DomainIsBlocked)
				AddEdge(cell, re);
		}

		public void RemoveEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellBlocked = allCellBCDs[cell.X, cell.Y].DomainIsBlocked;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].DomainIsBlocked == cellBlocked)
			{
				RemoveEdge(cell, le);
				RemoveEdge(l, re);
			}

			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].DomainIsBlocked == cellBlocked)
			{
				RemoveEdge(cell, re);
				RemoveEdge(r, le);
			}

			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].DomainIsBlocked == cellBlocked)
			{
				RemoveEdge(cell, te);
				RemoveEdge(t, be);
			}

			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].DomainIsBlocked == cellBlocked)
			{
				RemoveEdge(cell, be);
				RemoveEdge(b, te);
			}
		}

		public void AddEdgeIfNeighbourExists(Map map, CPos cell, ref BasicCellDomain[,] allCellBCDs)
		{
			var l = new CPos(cell.X - 1, cell.Y);
			var r = new CPos(cell.X + 1, cell.Y);
			var t = new CPos(cell.X, cell.Y - 1);
			var b = new CPos(cell.X, cell.Y + 1);

			var cellBlocked = allCellBCDs[cell.X, cell.Y].DomainIsBlocked;

			if (map.Contains(l) && allCellBCDs[l.X, l.Y] != null && allCellBCDs[l.X, l.Y].DomainIsBlocked == cellBlocked)
				AddEdge(cell, le);
			if (map.Contains(r) && allCellBCDs[r.X, r.Y] != null && allCellBCDs[r.X, r.Y].DomainIsBlocked == cellBlocked)
				AddEdge(cell, re);
			if (map.Contains(t) && allCellBCDs[t.X, t.Y] != null && allCellBCDs[t.X, t.Y].DomainIsBlocked == cellBlocked)
				AddEdge(cell, te);
			if (map.Contains(b) && allCellBCDs[b.X, b.Y] != null && allCellBCDs[b.X, b.Y].DomainIsBlocked == cellBlocked)
				AddEdge(cell, be);
		}

		public void AddCellEdges(Map map, DomainNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
		{
			AddEdge(cellNode.Value, le);
			AddEdge(cellNode.Value, re);
			AddEdge(cellNode.Value, te);
			AddEdge(cellNode.Value, be);

			RemoveEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);
		}

		public void RemoveCellEdges(Map map, DomainNode<CPos> cellNode, ref BasicCellDomain[,] allCellBCDs)
			=> AddEdgeIfNeighbourExists(map, cellNode.Value, ref allCellBCDs);

		public List<List<WPos>> GenerateCellEdges(Map map)
		{
			var edges = new List<List<WPos>>();

			for (var i = 0; i < CPosRows * CPosCols; i++)
			{
				var x = i % CPosCols;
				var y = i / CPosCols;

				if ((Get(x, y) & te) == te)
					edges.Add(map.TopEdgeOfCell(new CPos(x, y)));
				if ((Get(x, y) & be) == be)
					edges.Add(map.BottomEdgeOfCell(new CPos(x, y)));
				if ((Get(x, y) & le) == le)
					edges.Add(map.LeftEdgeOfCell(new CPos(x, y)));
				if ((Get(x, y) & re) == re)
					edges.Add(map.RightEdgeOfCell(new CPos(x, y)));
			}

			return edges;
		}

		public List<List<WPos>> GenerateConnectedCellEdges(Map map)
		{
			var x = 0;
			var y = 0;
			var botEndX = 0;
			var topEndX = 0;
			var leftEndY = new int[CPosCols];
			var rightEndY = new int[CPosCols];

			var edges = new List<List<WPos>>();

			while (y < CPosRows)
			{
				while (x < CPosCols)
				{
					var currCell = new CPos(x, y);

					// We only check if another endpoint exists if we have passed the last assigned endpoint (endX)
					if (x > topEndX)
					{
						topEndX = x;

						// If both the current and next cell are occupied, then we extend the end cell further
						if ((Get(x, y) & te) == te && topEndX + 1 < CPosCols)
							while (topEndX + 1 < CPosCols && (Get(topEndX + 1, y) & te) == te)
								topEndX++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (topEndX != x)
							edges.Add(new List<WPos>() { map.TopLeftOfCell(currCell), map.TopRightOfCell(new CPos(topEndX, y)) });
						else if ((Get(x, y) & te) == te) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.TopEdgeOfCell(currCell));
					}

					if (x > botEndX)
					{
						botEndX = x;

						// If both the current and next cell are occupied, then we extend the end cell further
						if ((Get(x, y) & be) == be && botEndX + 1 < CPosCols)
							while (botEndX + 1 < CPosCols && (Get(botEndX + 1, y) & be) == be)
								botEndX++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (botEndX != x)
							edges.Add(new List<WPos>() { map.BottomLeftOfCell(currCell), map.BottomRightOfCell(new CPos(botEndX, y)) });
						else if ((Get(x, y) & be) == be) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.BottomEdgeOfCell(currCell));
					}

					// Same logic as endX above
					if (y > leftEndY[x])
					{
						leftEndY[x] = y;

						// If both the current and next cell are occupied, then we extend the end cell further
						leftEndY[x] = y;
						if ((Get(x, y) & le) == le && leftEndY[x] + 1 < CPosRows)
							while (leftEndY[x] + 1 < CPosRows && (Get(x, leftEndY[x] + 1) & le) == le)
								leftEndY[x]++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (leftEndY[x] != y)
							edges.Add(new List<WPos>() { map.TopLeftOfCell(currCell), map.BottomLeftOfCell(new CPos(x, leftEndY[x])) });
						else if ((Get(x, y) & le) == le) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.LeftEdgeOfCell(currCell));
					}

					if (y > rightEndY[x])
					{
						rightEndY[x] = y;

						// If both the current and next cell are occupied, then we extend the end cell further
						if ((Get(x, y) & re) == re && rightEndY[x] + 1 < CPosRows)
							while (rightEndY[x] + 1 < CPosRows && (Get(x, rightEndY[x] + 1) & re) == re)
								rightEndY[x]++;

						// We have either not started, reached the end of the map, or found an end cell
						// If an end cell was found, we add the edge from start cell to end cell
						if (rightEndY[x] != y)
							edges.Add(new List<WPos>() { map.TopRightOfCell(currCell), map.BottomRightOfCell(new CPos(x, rightEndY[x])) });
						else if ((Get(x, y) & re) == re) // Otherwise we check if an edge exists on the start cell, and if so we add it
							edges.Add(map.RightEdgeOfCell(currCell));
					}

					x++;
				}

				y++;
				x = 0;
				topEndX = 0;
				botEndX = 0;
			}

			return edges;
		}
	}
}
