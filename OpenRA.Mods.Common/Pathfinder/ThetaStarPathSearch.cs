#region Copyright & License Information
/*
 * Copyright 2007-2021 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */

/* Original C++ Code this class has been derived from is under MIT License,
 * and is owned by T. Uras and S. Koenig,  2015. An Empirical Comparison of
 * Any-Angle Path-Planning Algorithms. In: Proceedings of the 8th Annual
 * Symposium on Combinatorial Search. Code available at: http://idm-lab.org/anyangle
 */

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

#pragma warning disable SA1512 // SingleLineCommentsMustNotBeFollowedByBlankLine
#pragma warning disable SA1108 // BlockStatementsMustNotContainEmbeddedComments
#pragma warning disable SA1307 // AccessibleFieldsMustBeginWithUpperCaseLetter
#pragma warning disable SA1513 // ClosingCurlyBracketMustBeFollowedByBlankLine
#pragma warning disable SA1515 // SingleLineCommentsMustBePrecededByBlankLine
#pragma warning disable SA1312 // Variable names should begin with lower-case letter

namespace OpenRA.Mods.Common.Pathfinder
{
	public class ThetaStarPathSearch
	{
		static readonly List<WPos> EmptyPath = new List<WPos>(0);

		public void RenderPathIfOverlay(List<WPos> path)
		{
			var overlay = thisWorld.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault();
			if (overlay.Enabled)
			{
				var renderPath = new List<WPos>();
				foreach (var pos in path)
					renderPath.Add(pos);

				if (path.Count > 1) // cannot render a path of length 1
					overlay.AddPath(renderPath);
			}
		}

		public bool AtStart = true;
		public WPos Source;
		public WPos Dest;

		public class CCState : IComparable<CCState>, IEquatable<CCState>
		{
			public CCPos CC;
			public int Hval;
			public World thisWorld;
			public int Gval = int.MaxValue;
			public int Fval => Gval + Hval;
			public CCState ParentState;
			public StateListType InList = StateListType.NoList;

			public CCState(CCPos cc, WPos goal, World world)
			{
				thisWorld = world;
				CC = cc;
				Hval = HeuristicFunction(cc, goal, thisWorld);
			}
			public CCState(CCPos cc, WPos goal, CCState parentState, World world)
			{
				thisWorld = world;
				CC = cc;
				Hval = HeuristicFunction(cc, goal, thisWorld);
				ParentState = parentState;
			}

			public CCState(CCPos cc, WPos goal, int gVal, CCState parentState, World world)
			{
				thisWorld = world;
				CC = cc;
				Hval = HeuristicFunction(cc, goal, thisWorld);
				Gval = gVal;
				ParentState = parentState;
			}
			public CCState(CCState state)
			{
				thisWorld = state.thisWorld;
				CC = state.CC;
				Hval = state.Hval;
				Gval = state.Gval;
				ParentState = state.ParentState;
			}

			public CCState(CCPos cc, WPos goal, int gVal, World world)
			{
				thisWorld = world;
				CC = cc;
				Hval = HeuristicFunction(cc, goal, thisWorld);
				Gval = gVal;
			}

			public void RenderInIfOverlay(World world)
			{
				var overlay = world.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault();
				if (overlay.Enabled)
					overlay.AddState(this);
			}
			public void RenderInIfOverlayWithCheck(World world)
			{
				var overlay = world.WorldActor.TraitsImplementing<ThetaStarPathfinderOverlay>().FirstEnabledTraitOrDefault();
				if (overlay.Enabled)
				{
					overlay.RemoveState(this);
					overlay.AddState(this);
				}
			}

			public static void Reflect(CCState fromState, ref CCState toState)
			{
				toState.Gval = fromState.Gval;
				toState.Hval = fromState.Hval;
			}
			public int GetEuclidDistanceTo(CCState toState)
			{
				var fromStatePos = thisWorld.Map.WPosFromCCPos(CC);
				var toStatePos = thisWorld.Map.WPosFromCCPos(toState.CC);
				return (int)(toStatePos - fromStatePos).HorizontalLength;
			}

			public static bool operator ==(CCState state1, CCState state2) { return state1.CC == state2.CC; }
			public bool Equals(CCState other) { return this == other; }
			public static bool operator !=(CCState state1, CCState state2) { return state1.CC != state2.CC; }

			public override bool Equals(object obj)
			{
				if (ReferenceEquals(this, obj))
					return true;
				if (ReferenceEquals(obj, null))
					return false;
				throw new NotImplementedException();
			}

			public override int GetHashCode()
			{
				var hash = default(HashCode);
				hash.Add(CC);
				return hash.ToHashCode();
			}

			public int CompareTo(CCState other)
			{
				if (Fval == other.Fval)
					return 0;
				else if (Fval > other.Fval)
					return 1;
				else // Fval < other.Fval
					return -1;
			}
		}

		private List<CCState> initialStates = new List<CCState>();

		private void AddCCListToStateList(List<CCPos> ccList, ref List<CCState> ccStateList,
													  WPos rootPos, WPos goalPos)
		{
			foreach (var cc in ccList)
				ccStateList.Add(new CCState(cc, goalPos, thisWorld));
		}

		private LinkedList<CCState> OpenList { get; set; }
		private LinkedList<CCState> ClosedList { get; set; }
		private Dictionary<(int x, int y), CCState> ccStateList = new Dictionary<(int x, int y), CCState>();
		public enum StateListType : byte { OpenList, ClosedList, NoList }

		private readonly World thisWorld;
		private readonly Actor self;
		private readonly MobileOffGrid mobileOffGrid;
		private readonly Locomotor locomotor;
		private readonly int ccPosMaxSizeX;
		private readonly int ccPosMaxSizeY;
		private readonly int ccPosMinSizeX;
		private readonly int ccPosMinSizeY;
		private readonly int cPosMaxSizeX;
		private readonly int cPosMaxSizeY;
		private readonly int cPosMinSizeX;
		private readonly int cPosMinSizeY;
		private enum CellSurroundingCorner : byte { TopLeft, TopRight, BottomLeft, BottomRight }

		private void AddStateToOpen(CCState state)
		{
			RemoveStateFromOpen(state);
			RemoveStateFromClosed(state);
			UpdateState(state);

			// Remove any matching state that is already in the list
			var currItem = OpenList.First;
			while (currItem != null)
			{
				if (currItem.Value == state)
					OpenList.Remove(currItem); // Remove with existing properties (== checks for ID match _not_ values)
				currItem = currItem.Next;
			}

			// Find the first item that is larger and add the item directly before it
			currItem = OpenList.First;
			while (currItem != null)
			{
				if (state.Fval > currItem.Value.Fval) // By not using <= we ensure this is a stable sort
					currItem = currItem.Next;
				else
					break;
			}

			if (currItem != null)
				OpenList.AddBefore(currItem, state);
			else if (currItem == null && OpenList.Count > 0)
				OpenList.AddLast(state);
			else
				OpenList.AddFirst(state);
		}

		private void AddStateToClosed(CCState state)
		{
			RemoveStateFromOpen(state);
			RemoveStateFromClosed(state);
			UpdateState(state);

			// Remove any matching state that is already in the list
			var currItem = ClosedList.First;
			while (currItem != null)
			{
				if (currItem.Value == state)
					ClosedList.Remove(currItem); // Remove with existing properties (== checks for ID match _not_ values)
				currItem = currItem.Next;
			}

			// Find the first item that is larger and add the item directly before it
			currItem = ClosedList.First;
			while (currItem != null)
			{
				if (state.Fval > currItem.Value.Fval) // By not using <= we ensure this is a stable sort
					currItem = currItem.Next;
				else
					break;
			}

			if (currItem != null)
				ClosedList.AddBefore(currItem, state);
			else if (currItem == null && ClosedList.Count > 0)
				ClosedList.AddLast(state);
			else
				ClosedList.AddFirst(state);
		}

		private CCState GetState(CCPos cc)
		{
			var ccKey = (cc.X, cc.Y);
			if (!ccStateList.ContainsKey(ccKey))
				ccStateList.Add(ccKey, new CCState(cc, Dest, thisWorld));
			return ccStateList[ccKey];
		}

		private void UpdateState(CCState state)
		{
			var cc = state.CC;
			var ccKey = (cc.X, cc.Y);
			if (!ccStateList.ContainsKey(ccKey))
				ccStateList.Add(ccKey, state);
			ccStateList[ccKey] = state;
		}
		private void UpdateState(CCPos cc, CCState parentState, int gval)
		{
			var ccKey = (cc.X, cc.Y);
			if (!ccStateList.ContainsKey(ccKey))
				ccStateList.Add(ccKey, new CCState(cc, Dest, gval, parentState, thisWorld));
			ccStateList[ccKey].Gval = gval;
			ccStateList[ccKey].ParentState = parentState;
		}
		private void UpdateState(CCState ccState, CCState parentState, int gval)
		{ UpdateState(ccState.CC, parentState, gval); }

		private void UpdateState(CCPos cc, int gval)
		{
			var ccKey = (cc.X, cc.Y);
			if (!ccStateList.ContainsKey(ccKey))
				ccStateList.Add(ccKey, new CCState(cc, Dest, gval, thisWorld));
			ccStateList[ccKey].Gval = gval;
		}
		private void UpdateState(CCState ccState, int gval)
		{ UpdateState(ccState.CC, gval); }

		private void UpdateState(CCPos cc, CCState parentState)
		{
			var ccKey = (cc.X, cc.Y);
			if (!ccStateList.ContainsKey(ccKey))
				ccStateList.Add(ccKey, new CCState(cc, Dest, parentState, thisWorld));
			ccStateList[ccKey].ParentState = parentState;
		}
		private void UpdateState(CCState ccState, CCState parentState)
		{ UpdateState(ccState.CC, parentState); }

		private void RemoveStateFromOpen(CCState state)
		{
			// Remove any matching state that is already in the list
			var currItem = OpenList.First;
			while (currItem != null)
			{
				if (currItem.Value == state)
					OpenList.Remove(currItem); // Remove with existing properties (== checks for ID match _not_ values)
				currItem = currItem.Next;
			}
		}

		private void RemoveStateFromClosed(CCState state)
		{
			// Remove any matching state that is already in the list
			var currItem = ClosedList.First;
			while (currItem != null)
			{
				if (currItem.Value == state)
					ClosedList.Remove(currItem); // Remove with existing properties (== checks for ID match _not_ values)
				currItem = currItem.Next;
			}
		}

		private void ResetLists()
		{
			OpenList = new LinkedList<CCState>();
			ClosedList = new LinkedList<CCState>();
		}

		private CCState PopFirstFromOpen()
		{
			var firstState = OpenList.First();
			OpenList.RemoveFirst();
			AddStateToClosed(firstState);
			return firstState;
		}

		// This will pad the ccPos in a path with a set amount of padding based on the actor's radius
		// Four cases: Note that only one direction is shown below, but other directions are simply mirrors
		//
		// XO                    OX                   XO                    XO
		// XX - move point TR    OO - move point BL   XO - move point R     OX - should not happen (to be tested)
		public WPos PadCC(CCPos cc)
		{
			var ccPos = thisWorld.Map.WPosFromCCPos(cc);
			var unitRadius = mobileOffGrid.UnitRadius.Length;

			var topLeftBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.TopLeft);
			var topRightBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.TopRight);
			var botLeftBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.BottomLeft);
			var botRightBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.BottomRight);

			var blockedList = new List<bool>() { topLeftBlocked, topRightBlocked, botLeftBlocked, botRightBlocked };
			var areBlocked = blockedList.Where(c => c == true).ToList();

			if (areBlocked.Count == 1 || areBlocked.Count == 3)
			{
				if (topLeftBlocked || (topLeftBlocked && botLeftBlocked && topRightBlocked))
					return new WPos(ccPos.X + unitRadius, ccPos.Y + unitRadius, ccPos.Z);
				if (topRightBlocked || (topRightBlocked && topLeftBlocked && botRightBlocked))
					return new WPos(ccPos.X - unitRadius, ccPos.Y + unitRadius, ccPos.Z);
				if (botLeftBlocked || (botLeftBlocked && topLeftBlocked && botRightBlocked))
					return new WPos(ccPos.X + unitRadius, ccPos.Y - unitRadius, ccPos.Z);
				if (botRightBlocked || (botRightBlocked && botLeftBlocked && topRightBlocked))
					return new WPos(ccPos.X - unitRadius, ccPos.Y - unitRadius, ccPos.Z);
			}
			else if (areBlocked.Count == 2)
			{
				if (topLeftBlocked && topRightBlocked)
					return new WPos(ccPos.X, ccPos.Y + unitRadius, ccPos.Z);
				if (topLeftBlocked && botLeftBlocked)
					return new WPos(ccPos.X + unitRadius, ccPos.Y, ccPos.Z);
				if (botLeftBlocked && botRightBlocked)
					return new WPos(ccPos.X, ccPos.Y - unitRadius, ccPos.Z);
				if (topRightBlocked && botRightBlocked)
					return new WPos(ccPos.X - unitRadius, ccPos.Y, ccPos.Z);
				return ccPos; // This is the case where the 2 blocked cells are diagonally adjacent, and should never happen.
			}

			return ccPos;
		}

		public List<WPos> ThetaStarFindPath(WPos sourcePos, WPos destPos)
		{
			ResetLists();

			if (sourcePos == destPos)
				return new List<WPos>();

			List<WPos> path = new List<WPos>();
			Source = sourcePos;
			Dest = destPos;

			// We first check if we can move to the target directly. If so, skip all pathfinding and return the list (sourcePos, destPos)
			/*if (IsPathObservable(sourcePos, destPos))
			{
				path.Add(sourcePos);
				path.Add(destPos);

				#if DEBUGWITHOVERLAY
				RenderPath(path);
				#endif

				return path;
			}*/

			var sourceCCPos = GetNearestCCPos(sourcePos);
			var destCCPos = GetNearestCCPos(destPos);

			// If CCPos cannot be traversed to, find nearest one that can be
			if (!CcinMap(destCCPos) || GetNeighbours(destCCPos).Count == 0)
			{
				var newCellCandidates = new List<CCPos>();
				var candidates = new List<CCPos>();
				var newCandidates = new List<CCPos>() { destCCPos };
				while (newCellCandidates.Count == 0)
				{
					// Assign candidates to last set of new candidates and flush new candidates
					candidates = newCandidates;
					newCandidates = new List<CCPos>();
					foreach (var c in candidates)
					{
						newCandidates = newCandidates.Union(new List<CCPos>()
						{
							new CCPos(c.X, c.Y - 1, c.Layer),
							new CCPos(c.X - 1, c.Y - 1, c.Layer),
							new CCPos(c.X + 1, c.Y - 1, c.Layer),
							new CCPos(c.X, c.Y + 1, c.Layer),
							new CCPos(c.X - 1, c.Y + 1, c.Layer),
							new CCPos(c.X + 1, c.Y + 1, c.Layer),
							new CCPos(c.X - 1, c.Y, c.Layer),
							new CCPos(c.X + 1, c.Y, c.Layer)
						}).ToList();
					}

					foreach (var nc in newCandidates)
						if (GetNeighbours(nc).Count > 0)
							newCellCandidates.Add(nc);
				}

				var distFromDest = int.MaxValue;
				var bestCandidate = newCandidates.ElementAt(0);

				foreach (var c in newCandidates)
				{
					var newDist = (int)(destPos - thisWorld.Map.WPosFromCCPos(c)).HorizontalLength;
					if (newDist < distFromDest)
					{
						distFromDest = newDist;
						bestCandidate = c;
					}
				}

				destCCPos = bestCandidate;
				destPos = thisWorld.Map.WPosFromCCPos(bestCandidate);
			}

			var startState = GetState(sourceCCPos);
			var goalState = GetState(destCCPos);
			startState.Gval = 0;
			startState.ParentState = startState;
			AddStateToOpen(startState);

			var numExpansions = 0;
			var maxExpansions = 1000;
			CCState minState = startState;
			while (OpenList.Count > 0 && numExpansions < maxExpansions)
			{
				minState = PopFirstFromOpen();
				if (goalState.Gval <= minState.Fval)
					break;

				numExpansions++;

				var minStateNeighbours = GetNeighbours(minState.CC);

				for (var i = 0; i < minStateNeighbours.Count; i++)
				{
					var succState = GetState(minStateNeighbours.ElementAt(i));

					/*#if DEBUGWITHOVERLAY
					succState.RenderInIfOverlay(thisWorld);
					#endif*/

					if (!ClosedList.Any(state => state.CC == succState.CC))
					{
						int newGval;
						CCState newParentState;
						if (IsPathObservable(minState.ParentState.CC, succState.CC))
						{
							newParentState = minState.ParentState;
							newGval = newParentState.Gval + newParentState.GetEuclidDistanceTo(succState);
						}
						else
						{
							newParentState = minState;
							newGval = newParentState.Gval + minState.GetEuclidDistanceTo(succState);
						}
						if (newGval < succState.Gval)
						{
							succState.Gval = newGval;
							succState.ParentState = newParentState;
							AddStateToOpen(succState);
						}
					}
				}

				#if DEBUGWITHOVERLAY
				Func<CCState, CCState> incrementFunc = state => state.ParentState;
				var currState = new CCState(minState);
				while (currState != startState)
				{
					System.Console.WriteLine($"CurrentMinStateCC: {currState.CC}, OpenList.Count: {OpenList.Count}" +
											 $", numExpansion: {numExpansions}");
					currState.RenderInIfOverlayWithCheck(thisWorld);
					currState = incrementFunc(currState);
				}
				#endif
			}

			if (goalState.Gval < int.MaxValue)
			{
				Func<CCState, CCState> incrementFunc = state => state.ParentState;
				var currState = goalState;
				path.Add(destPos); // We add the goal first since it is a WPos, then increment first initially
				currState = incrementFunc(currState);
				while (currState != startState)
				{
					var paddedCCpos = PadCC(currState.CC);
					// path.Add(thisWorld.Map.WPosFromCCPos(currState.CC)); // Swap above line with this if not using padding
					path.Add(paddedCCpos);
					currState = incrementFunc(currState);
				}
				// path.Add(sourcePos);
				path.Reverse();

				#if DEBUGWITHOVERLAY
				RenderPathIfOverlay(new List<WPos>() { sourcePos }.Union(path).ToList());
				#endif

				return path;
			}

			return EmptyPath;
		}

		private bool LineOfSight(CCPos cc1, CCPos cc2)
		{
			var x1 = cc1.X;
			var y1 = cc1.Y;
			var x2 = cc2.X;
			var y2 = cc2.Y;

			var dy = cc2.Y - cc1.Y;
			var dx = cc2.X - cc1.X;

			var f = 0;
			int sy;
			int sx;
			int Xoffset;
			int Yoffset;

			if (dy < 0)
			{
				dy = -dy;
				sy = -1;
				Yoffset = -1; // Cell is to the North
			}
			else
			{
				sy = 1;
				Yoffset = 0; // Cell is to the South
			}
			if (dx < 0)
			{
				dx = -dx;
				sx = -1;
				Xoffset = -1; // Cell is to the West
			}
			else
			{
				sx = 1;
				Xoffset = 0; // Cell is to the East
			}

			if (dx >= dy) // Move along the x axis and increment/decrement y when f >= dx.
			{
				while (x1 != x2)
				{
					f += dy;
					if (f >= dx) // We are changing rows, we might need to check two cells this iteration.
					{
						if (IsCellBlocked(new CPos(x1 + Xoffset, y1 + Yoffset)))
							return false;
						y1 += sy;
						f -= dx;
					}
					if (f != 0) // If f == 0, then we are crossing the row at a corner point and we don't need to check both cells.
						if (IsCellBlocked(new CPos(x1 + Xoffset, y1 + Yoffset)))
							return false;
					if (dy == 0) // If we are moving along a horizontal line, either the north or the south cell should be unblocked.
						if (IsCellBlocked(new CPos(x1 + Xoffset, y1 - 1)) &&
							IsCellBlocked(new CPos(x1 + Xoffset, y1)))
							return false;

					x1 += sx;
				}
			}
			else // if (dx < dy). Move along the y axis and increment/decrement x when f >= dy.
			{
				while (y1 != y2)
				{
					f += dx;
					if (f >= dy)
					{
						if (IsCellBlocked(new CPos(x1 + Xoffset, y1 + Yoffset)))
							return false;
						x1 += sx;
						f -= dy;
					}
					if (f != 0)
						if (IsCellBlocked(new CPos(x1 + Xoffset, y1 + Yoffset)))
							return false;
					if (dx == 0)
						if (IsCellBlocked(new CPos(x1 - 1, y1 + Yoffset)) &&
							IsCellBlocked(new CPos(x1,     y1 + Yoffset)))
							return false;

					y1 += sy;
				}
			}

			return true;
		}


		// This could potentially be optimised with great care, currently it returns a bounding box of cells for a given line (WPos -> Wpos)
		public List<CPos> GetAllCellsUnderneathALine(WPos a0, WPos a1) { return GetAllCellsUnderneathALine(thisWorld, a0, a1); }

		// This could potentially be optimised with great care, currently it returns a bounding box of cells for a given line (WPos -> Wpos)
		public static List<CPos> GetAllCellsUnderneathALine(World world, WPos a0, WPos a1)
		{
			var startingCornerCell = world.Map.CellContaining(a0);
			var endingCornerCell = world.Map.CellContaining(a1);

			var leftMostX = Math.Min(startingCornerCell.X, endingCornerCell.X);
			var rightMostX = Math.Max(startingCornerCell.X, endingCornerCell.X);
			var topMostY = Math.Min(startingCornerCell.Y, endingCornerCell.Y);
			var bottomMostY = Math.Max(startingCornerCell.Y, endingCornerCell.Y);

			var cellsUnderneathLine = new List<CPos>();
			for (var currCellX = leftMostX; currCellX <= rightMostX; currCellX += 1)
				for (var currCellY = topMostY; currCellY <= bottomMostY; currCellY += 1)
					cellsUnderneathLine.Add(new CPos(currCellX, currCellY));
			return cellsUnderneathLine;
		}

		public bool AreCellsIntersectingPath(List<CPos> cells, WPos sourcePos, WPos destPos)
		{ return AreCellsIntersectingPath(thisWorld, self, locomotor, cells, sourcePos, destPos); }

		public static bool AreCellsIntersectingPath(World world, Actor self, Locomotor locomotor,
													List<CPos> cells, WPos sourcePos, WPos destPos)
		{
			foreach (var cell in cells)
				if (IsCellBlocked(self, locomotor, cell) && world.Map.AnyCellEdgeIntersectsWithLine(cell, sourcePos, destPos))
					return true;
			return false;
		}

		public bool IsPathObservable(WPos rootPos, WPos destPos) { return IsPathObservable(thisWorld, self, locomotor, rootPos, destPos); }
		public static bool IsPathObservable(World world, Actor self, Locomotor locomotor, WPos rootPos, WPos destPos)
		{
			var cellsUnderneathLine = GetAllCellsUnderneathALine(world, rootPos, destPos);
			return !AreCellsIntersectingPath(world, self, locomotor, cellsUnderneathLine, rootPos, destPos);
		}

		public bool IsPathObservable(CCPos rootCC, CCPos destCC)
		{ return IsPathObservable(thisWorld.Map.WPosFromCCPos(rootCC), thisWorld.Map.WPosFromCCPos(destCC)); }
		public bool IsPathObservable(WPos rootPos, CCPos destCC) { return IsPathObservable(rootPos, thisWorld.Map.WPosFromCCPos(destCC)); }
		public bool IsPathObservable(CCPos rootCC, WPos destPos) { return IsPathObservable(thisWorld.Map.WPosFromCCPos(rootCC), destPos); }

		private static int HeuristicFunction(CCPos cc, WPos goalPos, World world)
		{
			var ccPos = world.Map.WPosFromCCPos(cc);
			return (int)(goalPos - ccPos).HorizontalLength;
		}
		private static int HeuristicFunction(WPos startPos, WPos goalPos)
		{
			return (int)(goalPos - startPos).HorizontalLength;
		}

		private bool CcXinMap(int x) { return x >= ccPosMinSizeX && x <= ccPosMaxSizeX; } // 1 larger than CPos bounds which is MapSize.X - 1
		private bool CcYinMap(int y) { return y >= ccPosMinSizeY && y <= ccPosMaxSizeY; } // 1 larger than CPos bounds which is MapSize.Y - 1
		private static bool CcinMap(CCPos ccPos, World world) // These must be static to allow the HueristicFunction to be static
		{
			return ccPos.X >= 0 && ccPos.X <= world.Map.MapSize.X
				&& ccPos.Y >= 0 && ccPos.Y <= world.Map.MapSize.Y;
		}
		private bool CcinMap(CCPos ccPos) { return CcinMap(ccPos, thisWorld); }

		private bool CPosinMap(CPos cPos)
		{
			return cPos.X >= cPosMinSizeX && cPos.X <= cPosMaxSizeX &&
				   cPos.Y >= cPosMinSizeY && cPos.Y <= cPosMaxSizeY;
		}

		private static CCPos ClosestCCPosInMap(CCPos ccPos, World world) // These must be static to allow the HueristicFunction to be static
		{
			return new CCPos(Math.Max(Math.Min(ccPos.X, world.Map.MapSize.X), 0),
			                 Math.Max(Math.Min(ccPos.Y, world.Map.MapSize.Y), 0));
		}
		private CCPos ClosestCCPosInMap(CCPos ccPos) { return ClosestCCPosInMap(ccPos, thisWorld); }

		// need to use this func to clamp to cell to ensure compatibility with isometric grids
		private static CCPos GetNearestCCPos(WPos pos, World world)
		{
			var cellContainingPos = world.Map.CellContaining(pos);
			var distToTopLeft = (pos - world.Map.TopLeftOfCell(cellContainingPos)).HorizontalLength;
			var distToTopRight = (pos - world.Map.TopRightOfCell(cellContainingPos)).HorizontalLength;
			var distToBottomLeft = (pos - world.Map.BottomLeftOfCell(cellContainingPos)).HorizontalLength;
			var distToBottomRight = (pos - world.Map.BottomRightOfCell(cellContainingPos)).HorizontalLength;
			var minDist = Math.Min(Math.Min(Math.Min(distToTopLeft, distToTopRight), distToBottomLeft), distToBottomRight);

			CCPos nearestCC;
			if (distToTopLeft == minDist)
				nearestCC = Map.TopLeftCCPos(cellContainingPos);
			else if (distToTopRight == minDist)
				nearestCC = Map.TopRightCCPos(cellContainingPos);
			else if (distToBottomLeft == minDist)
				nearestCC = Map.BottomLeftCCPos(cellContainingPos);
			else if (distToBottomRight == minDist)
				nearestCC = Map.BottomRightCCPos(cellContainingPos);
			else
				nearestCC = new CCPos(-1, -1); // will fail the if check below

			if (CcinMap(nearestCC, world))
				return nearestCC;
			else
				return ClosestCCPosInMap(nearestCC, world);
		}

		private CCPos GetNearestCCPos(WPos pos) { return GetNearestCCPos(pos, thisWorld); }

		private bool IsCellBlocked(CPos? cell) { return IsCellBlocked(self, locomotor, cell);	}
		private static bool IsCellBlocked(Actor self, Locomotor locomotor, CPos? cell)
		{
			if (cell == null)
				return true; // All invalid cells are blocked
			return MobileOffGrid.CellIsBlocked(self, locomotor, (CPos)cell);
		}

		private bool CellSurroundingCCPosIsBlocked(CCPos ccPos, CellSurroundingCorner cellSurroundingCorner)
		{
			switch (cellSurroundingCorner)
			{
				case CellSurroundingCorner.TopLeft:
					return IsCellBlocked(thisWorld.Map.CellTopLeftOfCCPos(ccPos));
				case CellSurroundingCorner.TopRight:
					return IsCellBlocked(thisWorld.Map.CellTopRightOfCCPos(ccPos));
				case CellSurroundingCorner.BottomLeft:
					return IsCellBlocked(thisWorld.Map.CellBottomLeftOfCCPos(ccPos));
				case CellSurroundingCorner.BottomRight:
					return IsCellBlocked(thisWorld.Map.CellBottomRightOfCCPos(ccPos));
				default:
					return false;
			}
		}

		private List<CCPos> GetNeighbours(CCPos cc)
		{
			var neighbourList = new List<CCPos>();

			var ccT = new CCPos(cc.X, cc.Y - 1, cc.Layer);
			var ccTL = new CCPos(cc.X - 1, cc.Y - 1, cc.Layer);
			var ccTR = new CCPos(cc.X + 1, cc.Y - 1, cc.Layer);
			var ccB = new CCPos(cc.X, cc.Y + 1, cc.Layer);
			var ccBL = new CCPos(cc.X - 1, cc.Y + 1, cc.Layer);
			var ccBR = new CCPos(cc.X + 1, cc.Y + 1, cc.Layer);
			var ccL = new CCPos(cc.X - 1, cc.Y, cc.Layer);
			var ccR = new CCPos(cc.X + 1, cc.Y, cc.Layer);

			var TLBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.TopLeft);
			var TRBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.TopRight);
			var BLBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.BottomLeft);
			var BRBlocked = CellSurroundingCCPosIsBlocked(cc, CellSurroundingCorner.BottomRight);

			if ((TLBlocked && BRBlocked && !TRBlocked && !BLBlocked) ||
				(TRBlocked && BLBlocked && !TLBlocked && !BRBlocked))
				return neighbourList; // diagonally blocked corner so we cannot proceed

			var topBlocked = TLBlocked && TRBlocked;
			var botBlocked = BLBlocked && BRBlocked;
			var leftBlocked = TLBlocked && BLBlocked;
			var rightBlocked = TRBlocked && BRBlocked;

			if (CcinMap(ccT) && !topBlocked)
				neighbourList.Add(ccT);
			if (CcinMap(ccTL) && !TLBlocked)
				neighbourList.Add(ccTL);
			if (CcinMap(ccTR) && !TRBlocked)
				neighbourList.Add(ccTR);
			if (CcinMap(ccB) && !botBlocked)
				neighbourList.Add(ccB);
			if (CcinMap(ccBL) && !BLBlocked)
				neighbourList.Add(ccBL);
			if (CcinMap(ccBR) && !BRBlocked)
				neighbourList.Add(ccBR);
			if (CcinMap(ccL) && !leftBlocked)
				neighbourList.Add(ccL);
			if (CcinMap(ccR) && !rightBlocked)
				neighbourList.Add(ccR);

			return neighbourList;
		}

		#region Constructors
		public ThetaStarPathSearch(World world, Actor self)
		{
			thisWorld = world;
			this.self = self;
			mobileOffGrid = self.TraitsImplementing<MobileOffGrid>().Where(Exts.IsTraitEnabled).FirstOrDefault();
			locomotor = thisWorld.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			ccPosMaxSizeX = thisWorld.Map.MapSize.X;
			ccPosMinSizeX = 0;
			ccPosMaxSizeY = thisWorld.Map.MapSize.Y;
			ccPosMinSizeY = 0;
			cPosMaxSizeX = thisWorld.Map.MapSize.X - 1;
			cPosMinSizeX = 0;
			cPosMaxSizeY = thisWorld.Map.MapSize.Y - 1;
			cPosMinSizeY = 0;
			ResetLists();
		}
		#endregion

	}
}
