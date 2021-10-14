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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

#pragma warning disable SA1512 // SingleLineCommentsMustNotBeFollowedByBlankLine
#pragma warning disable SA1108 // BlockStatementsMustNotContainEmbeddedComments
#pragma warning disable SA1307 // AccessibleFieldsMustBeginWithUpperCaseLetter
#pragma warning disable SA1513 // ClosingCurlyBracketMustBeFollowedByBlankLine
#pragma warning disable SA1515 // SingleLineCommentsMustBePrecededByBlankLine

namespace OpenRA.Mods.Common.Pathfinder
{
	public class AnyaPathSearch
	{
		public const int T = -1;
		public const int B = 1;
		public const int TL = 0;
		public const int TR = 1;
		public const int BL = 2;
		public const int BR = 3;
		static readonly List<WPos> EmptyPath = new List<WPos>(0);

		public bool AtStart = true;

		public class Interval
		{
			public List<CCPos> CCs;
			public bool Observable = false;
			public Dictionary<int, bool> Blocked = new Dictionary<int, bool>() { { T, false }, { B, false } };
			public Interval(List<CCPos> ccs) { CCs = ccs; }
			public Interval()
				: this(new List<CCPos>()) { }

			public static bool operator ==(Interval interval1, Interval interval2)
			{ return interval1.CCs.SequenceEqual(interval2.CCs); }
			public static bool operator !=(Interval interval1, Interval interval2)
			{ return !interval1.CCs.SequenceEqual(interval2.CCs); }

			public int Clearance => Math.Abs(CCs.LastOrDefault().X - CCs.FirstOrDefault().X);
			public bool ClearanceExists => Math.Abs(CCs.LastOrDefault().X - CCs.FirstOrDefault().X) > 0;
			public bool Empty => CCs.Count == 0;
			public bool NotEmptyAndHasClearance => CCs.Count > 0 && Math.Abs(CCs.LastOrDefault().X - CCs.FirstOrDefault().X) > 0;

			public override bool Equals(object obj)
			{
				if (ReferenceEquals(this, obj))
					return true;
				if (obj is null)
					return false;
				throw new NotImplementedException();
			}

			public void RenderIntervalIn(World world)
			{
				if (CCs.Count > 0)
					world.WorldActor.TraitsImplementing<AnyaPathfinderOverlay>().FirstEnabledTraitOrDefault().AddInterval(this);
			}

			public override int GetHashCode() { return CCs.GetHashCode(); }
		}

		public class IntervalState : IComparable<IntervalState>, IEquatable<IntervalState>
		{
			public WPos Pos;
			public Interval Interval;
			public int Hval = 0;
			public World thisWorld;
			public int Gval = int.MaxValue;
			public int Fval = 0;
			public IntervalState ParentState;
			public StateListType InList = StateListType.NoList;
			public IntervalState() { }
			public IntervalState(Interval interval) { Interval = interval; }
			public IntervalState(Interval interval, WPos pos)
			{
				Interval = interval;
				Pos = pos;
			}

			public IntervalState(Interval interval, WPos root, WPos goal, int gVal, IntervalState parentState, World world)
			{
				thisWorld = world;
				Interval = interval;
				Pos = root;
				Hval = HeuristicFunction(root, interval, goal, thisWorld);
				Gval = gVal;
				ParentState = parentState;
			}

			public IntervalState(Interval interval, WPos root, WPos goal, int gVal, World world)
			{
				thisWorld = world;
				Interval = interval;
				Pos = root;
				Hval = HeuristicFunction(root, interval, goal, thisWorld);
				Gval = gVal;
			}

			public IntervalState(Interval interval, WPos root, WPos goal, World world)
			{
				thisWorld = world;
				Interval = interval;
				Pos = root;
				Hval = HeuristicFunction(root, interval, goal, thisWorld);
			}

			public static void Reflect(IntervalState fromState, ref IntervalState toState)
			{
				toState.Gval = fromState.Gval;
				toState.Hval = fromState.Hval;
				toState.Fval = fromState.Gval + fromState.Hval;
			}

			public static bool operator ==(IntervalState state1, IntervalState state2)
			{
				var posMatch = state1.Pos == state2.Pos;
				var intervalMatch = state1.Interval == state2.Interval;
				return posMatch && intervalMatch;
			}

			public bool Equals(IntervalState other) { return this == other; }
			public static bool operator !=(IntervalState state1, IntervalState state2)
			{
				var posMatch = state1.Pos == state2.Pos;
				var intervalMatch = state1.Interval == state2.Interval;
				return posMatch || intervalMatch;
			}

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
				hash.Add(Pos);
				hash.Add(Interval);
				return hash.ToHashCode();
			}

			public int CompareTo(IntervalState other)
			{
				if (Fval == other.Fval)
					return 0;
				else if (Fval > other.Fval)
					return 1;
				else // Fval < other.Fval
					return -1;
			}
		}

		public List<IntervalState> InitialStates = new List<IntervalState>();

		public void AddIntervalListToStateList(List<Interval> intervalList, ref List<IntervalState> stateList,
													  WPos rootPos, WPos goalPos)
		{
			foreach (var interval in intervalList)
				stateList.Add(new IntervalState(interval, rootPos, goalPos, thisWorld));
		}

		public LinkedList<IntervalState> OpenList { get; private set; }
		public LinkedList<IntervalState> ClosedList { get; private set; }
		public enum StateListType : byte { OpenList, ClosedList, NoList }

		private readonly World thisWorld;
		private readonly Actor self;
		private readonly int ccPosMaxSizeX;
		private readonly int ccPosMaxSizeY;
		private readonly int ccPosMinSizeX;
		private readonly int ccPosMinSizeY;
		private readonly int cPosMaxSizeX;
		private readonly int cPosMaxSizeY;
		private readonly int cPosMinSizeX;
		private readonly int cPosMinSizeY;

		public enum IntervalSide : byte { Left, Right, Both, None }
		public enum CellSurroundingCorner : byte { TopLeft, TopRight, BottomLeft, BottomRight }

		public void AddStateToOpen(IntervalState state)
		{
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
		public void AddStateToClosed(IntervalState state)
		{
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

		public void RemoveStateFromOpen(IntervalState state)
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

		public void RemoveStateFromClosed(IntervalState state)
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

		public void ResetLists()
		{
			OpenList = new LinkedList<IntervalState>();
			ClosedList = new LinkedList<IntervalState>();
		}

		public IntervalState PopFirstFromOpen()
		{
			var firstState = OpenList.First();
			OpenList.RemoveFirst();
			AddStateToClosed(firstState);
			return firstState;
		}

		public List<WPos> AnyaFindPath(WPos sourcePos, WPos destPos)
		{
			ResetLists();

			List<WPos> path = new List<WPos>();
			var destCCPos = GetNearestCCPos(destPos);
			var pathFound = false;
			var goalState = new IntervalState();

			if (sourcePos == destPos)
				return new List<WPos>();

			GenerateStartSuccessors(sourcePos, destPos);

			var maxIters = 100; // For testing only
			while (OpenList.Count > 0)
			{
				maxIters--;
				if (maxIters <= 0)
					break;

				var minState = PopFirstFromOpen();
				if (minState.Interval.CCs.Any(cc => cc.Y == destCCPos.Y) &&
					minState.Interval.CCs.FirstOrDefault().X <= destCCPos.X &&
					minState.Interval.CCs.LastOrDefault().X >= destCCPos.X)
				{
					// may need code to create path here
					pathFound = true;
					goalState = minState;
					break;
				}

				var successors = new List<IntervalState>();
				GenerateSuccessors(minState.Pos, minState.Interval, destPos, ref successors);

				for (var i = 0; i < successors.Count; i += 1)
				{
					var currSucc = successors.ElementAt(i);
					if (!ClosedList.Any(state => state == currSucc)) // check where Closed is being set
					{
						var newGval = minState.Gval + (int)(currSucc.Pos - minState.Pos).HorizontalLengthSquared;
						if (newGval < currSucc.Gval)
						{
							currSucc.Gval = newGval;
							currSucc.ParentState = minState;
							AddStateToOpen(currSucc);
						}
					}
				}

				// gen successors
			}

			if (pathFound)
			{
				path.Add(destPos);
				var currState = goalState;
				var nextState = currState;
				while (currState.Pos != sourcePos)
				{
					if (currState.Pos != nextState.Pos)
						path.Add(currState.Pos);
					currState = nextState;
					nextState = currState.ParentState;
				}

				path.Add(sourcePos);
				path.Reverse();
				return path;
			}

			return EmptyPath;
		}

		public static int HeuristicFunction(WPos rootPos, Interval interval, WPos goalPos, World world)
		{
			if (interval.CCs.Count == 0) // If interval is empty we cannot use this candidate, so return the highest cost possible
				return int.MaxValue;

			// To be implemented
			var rootCCPos = GetNearestCCPos(rootPos, world);
			var goalCCPos = GetNearestCCPos(goalPos, world);
			var intervalFirstCC = interval.CCs.FirstOrDefault();
			var intervalFirstCCWPos = world.Map.WPosFromCCPos(intervalFirstCC);
			var intervalLastCC = interval.CCs.LastOrDefault();
			var intervalLastCCWPos = world.Map.WPosFromCCPos(intervalLastCC);
			if (intervalFirstCC.Y == rootCCPos.Y && rootCCPos.Y == goalCCPos.Y)
			{
				if (rootCCPos.X < intervalFirstCC.X && goalCCPos.X < intervalFirstCC.X)
				{
					return (int)((intervalFirstCCWPos - rootPos).HorizontalLengthSquared +
						   (intervalFirstCCWPos - goalPos).HorizontalLengthSquared);
				}
				else if (rootCCPos.X > intervalLastCC.X && goalCCPos.X > intervalLastCC.X)
				{
					return (int)((intervalLastCCWPos - rootPos).HorizontalLengthSquared +
						   (intervalLastCCWPos - goalPos).HorizontalLengthSquared);
				}
				else
				{
					return (int)(goalPos - rootPos).HorizontalLengthSquared;
				}
			}

			// If the root and the goal are both on the same side of the interval, take the mirror image of the goal wrt the interval
			var goalY = goalCCPos.Y;
			var deltaRoot = rootCCPos.Y - intervalFirstCC.Y; // Normalize the y-coordinates so that the interval's y coordinate corresponds to 0.
			var deltaGoal = goalCCPos.Y - intervalFirstCC.Y;
			if (deltaRoot * deltaGoal > 0)
				goalY = intervalFirstCC.Y - deltaGoal;

			var goalYWPos = world.Map.WPosFromCCPos(new CCPos(goalCCPos.X, goalY));

			var x = WPos.GetIntersectingX(rootPos, goalYWPos, intervalFirstCCWPos.Y);
			if (x < intervalFirstCCWPos.X)
				x = intervalFirstCCWPos.X;
			if (x > intervalLastCCWPos.X)
				x = intervalLastCCWPos.X;

			var newCC = new CCPos(x, intervalFirstCC.Y);
			var newCCWPos = world.Map.WPosFromCCPos(newCC);

			return (int)((newCCWPos - rootPos).HorizontalLengthSquared + (newCCWPos - goalPos).HorizontalLengthSquared);
		}

		public int HeuristicFunction(WPos rootPos, Interval interval, WPos goalPos)
		{
			return HeuristicFunction(rootPos, interval, goalPos, thisWorld);
		}
		public bool CcXinMap(int x) { return x >= ccPosMinSizeX && x <= ccPosMaxSizeX; } // 1 larger than CPos bounds which is MapSize.X - 1
		public bool CcYinMap(int y) { return y >= ccPosMinSizeY && y <= ccPosMaxSizeY; } // 1 larger than CPos bounds which is MapSize.Y - 1
		public static bool CcinMap(CCPos ccPos, World world) // These must be static to allow the HueristicFunction to be static
		{
			return ccPos.X >= 0 && ccPos.X <= world.Map.MapSize.X
				&& ccPos.Y >= 0 && ccPos.Y <= world.Map.MapSize.Y;
		}
		public bool CcinMap(CCPos ccPos) { return CcinMap(ccPos, thisWorld); }

		public bool CPosinMap(CPos cPos)
		{
			return cPos.X >= cPosMinSizeX && cPos.X <= cPosMaxSizeX &&
				   cPos.Y >= cPosMinSizeY && cPos.Y <= cPosMaxSizeY;
		}

		public static CCPos ClosestCCPosInMap(CCPos ccPos, World world) // These must be static to allow the HueristicFunction to be static
		{
			return new CCPos(Math.Max(Math.Min(ccPos.X, world.Map.MapSize.X), 0),
			                 Math.Max(Math.Min(ccPos.Y, world.Map.MapSize.Y), 0));
		}
		public CCPos ClosestCCPosInMap(CCPos ccPos) { return ClosestCCPosInMap(ccPos, thisWorld); }

		// Note that the row will be offset by 45 degrees if using an Isometric map type (this is necessary for Anya to work, as it requires a conventional grid)
		public List<CCPos> GetBoundingCCPosOfRowWithOffset(CCPos ccPos, int offset, int startX = int.MinValue, int endX = int.MaxValue)
		{
			var leftMostX = Math.Max(ccPosMinSizeX, startX);
			var rightMostX = Math.Min(ccPosMaxSizeX, endX); // 1 larger than CPos bounds which is MapSize.X - 1

			var newCCPosWithOffsetY = ccPos.Y + offset;
			if (CcYinMap(newCCPosWithOffsetY))
			{
				return new List<CCPos>()
				{
					new CCPos(leftMostX, newCCPosWithOffsetY, ccPos.Layer),
					new CCPos(rightMostX, newCCPosWithOffsetY, ccPos.Layer)
				};
			}
			else
				return new List<CCPos>(); // out of bounds (top of map) so we return an empty list
		}

		// Note that the row will be offset by 45 degrees if using an Isometric map type (this is necessary for Anya to work, as it requires a conventional grid)
		public Interval GetFirstInterval(CCPos ccPos, int offset, IntervalSide intervalSide, int unblockedDirY = 0)
		{
			if (!CcYinMap(ccPos.Y + offset)) // Return blank interval if new Y is out of bounds
				return new Interval();

			var currCCPos = new CCPos(ccPos.X, ccPos.Y + offset, ccPos.Layer);
			var blockedTop = false;
			var blockedBottom = false;
			var ccPosInRow = new List<CCPos>();
			Func<CCPos, bool> leftPathIsBlockedFunc;
			Func<CCPos, bool> rightPathIsBlockedFunc;

			if (unblockedDirY == 1) // Bottom is not meant to be blocked so we use it for checking if the path forward is blocked
			{
				leftPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.BottomLeft);
				rightPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.BottomRight);
			}
			else if (unblockedDirY == -1)
			{
				leftPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.TopLeft);
				rightPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.TopRight);
			}
			else // unblockedDirY == 0
			{
				leftPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.TopLeft) &&
						 					 CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.BottomLeft);
				rightPathIsBlockedFunc = c => CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.TopRight) &&
						 					  CellSurroundingCCPosIsBlocked(c, CellSurroundingCorner.BottomRight);
			}

			if (intervalSide == IntervalSide.Left)
			{
				// We continue until the blocked status changes from the beginning.
				// E.g.: Blocked cells are included until an unblocked cell is found
				var firstCCPos = currCCPos;
				var priorCCPos = currCCPos;
				blockedTop = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.TopLeft);
				blockedBottom = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.BottomLeft);
				while (currCCPos.X > ccPosMinSizeX && !leftPathIsBlockedFunc(currCCPos))
				{
					priorCCPos = currCCPos;
					currCCPos = new CCPos(currCCPos.X - 1, currCCPos.Y, currCCPos.Layer);
				}
				if (firstCCPos.X - currCCPos.X > 0)
				{
					ccPosInRow.Add(firstCCPos);
					ccPosInRow.Add(currCCPos);
				}
			}
			else if (intervalSide == IntervalSide.Right)
			{
				var firstCCPos = currCCPos;
				var priorCCPos = currCCPos;
				blockedTop = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.TopRight);
				blockedBottom = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.BottomRight);
				while (currCCPos.X < ccPosMaxSizeX && !rightPathIsBlockedFunc(currCCPos))
				{
					priorCCPos = currCCPos;
					currCCPos = new CCPos(currCCPos.X + 1, currCCPos.Y, currCCPos.Layer);
				}
				if (currCCPos.X - firstCCPos.X > 0)
				{
					ccPosInRow.Add(firstCCPos);
					ccPosInRow.Add(currCCPos);
				}
			}

			if (ccPosInRow.Count <= 1) // Do not return a single CCPos interval
				return new Interval();

			var intervalCCs = ccPosInRow.OrderBy(cc => cc.X).ThenBy(cc => cc.Y).ToList();
			var newInterval = new Interval(intervalCCs);
			newInterval.Blocked[T] = blockedTop;
			newInterval.Blocked[B] = blockedBottom;

			#if DEBUGWITHOVERLAY
			newInterval.RenderIntervalIn(thisWorld);
			#endif

			return newInterval;
		}

		// need to use this func to clamp to cell to ensure compatibility with isometric grids
		public static CCPos GetNearestCCPos(WPos pos, World world)
		{
			var cellContainingPos = world.Map.CellContaining(pos);
			var distToTopLeft = (pos - world.Map.TopLeftOfCell(cellContainingPos)).HorizontalLengthSquared;
			var distToTopRight = (pos - world.Map.TopRightOfCell(cellContainingPos)).HorizontalLengthSquared;
			var distToBottomLeft = (pos - world.Map.BottomLeftOfCell(cellContainingPos)).HorizontalLengthSquared;
			var distToBottomRight = (pos - world.Map.BottomRightOfCell(cellContainingPos)).HorizontalLengthSquared;
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

		public CCPos GetNearestCCPos(WPos pos) { return GetNearestCCPos(pos, thisWorld); }

		public CCPos GetFarthestCCPosFromRoot(List<CCPos> intervalCCPosList, WPos rootPos)
		{
			var maxDist = long.MinValue;
			var maxDistCcPos = default(CCPos);
			foreach (var ccPos in intervalCCPosList)
			{
				var ccPosToRootPosDist = (thisWorld.Map.WPosFromCCPos(ccPos) - rootPos).HorizontalLengthSquared;
				if (ccPosToRootPosDist > maxDist)
				{
					maxDist = ccPosToRootPosDist;
					maxDistCcPos = ccPos;
				}
			}

			return maxDistCcPos;
		}

		public Interval GetInterval(CCPos rootCCPos, int offset, IntervalSide intervalSide, int startX = int.MinValue, int endX = int.MaxValue)
		{
			var interval = new Interval();
			if (intervalSide != IntervalSide.None)
			{
				if (intervalSide == IntervalSide.Both)
					interval = new Interval(GetBoundingCCPosOfRowWithOffset(rootCCPos, offset));
				else if (intervalSide == IntervalSide.Left)
					interval = new Interval(GetBoundingCCPosOfRowWithOffset(rootCCPos, offset, endX: rootCCPos.X));
				else if (intervalSide == IntervalSide.Right)
					interval = new Interval(GetBoundingCCPosOfRowWithOffset(rootCCPos, offset, startX: rootCCPos.X));
			}
			else
				interval = new Interval(GetBoundingCCPosOfRowWithOffset(rootCCPos, offset, startX, endX));

			#if DEBUGWITHOVERLAY
			interval.RenderIntervalIn(thisWorld);
			#endif

			return interval;
		}

		public Interval GetInterval(WPos rootPos, int offset, IntervalSide intervalSide, int startX = int.MinValue, int endX = int.MaxValue)
		{
			return GetInterval(GetNearestCCPos(rootPos), offset, intervalSide, startX, endX);
		}

		public bool IsCellBlocked(CPos? cell)
		{
			if (cell == (CPos?)null || !CPosinMap((CPos)cell))
				return true; // All invalid cells are blocked

			var locomotor = thisWorld.WorldActor.TraitsImplementing<Locomotor>().FirstEnabledTraitOrDefault();
			return locomotor.MovementCostToEnterCell(default, (CPos)cell, BlockedByActor.None, self) == short.MaxValue;
		}

		public int NumberOfCellsBlocked(List<CPos> cells)
		{
			var numberOfCellsBlocked = 0;
			foreach (var cell in cells)
				if (IsCellBlocked(cell))
					numberOfCellsBlocked += 1;
			return numberOfCellsBlocked;
		}

		public bool CellSurroundingCCPosIsBlocked(CCPos ccPos, CellSurroundingCorner cellSurroundingCorner)
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

		public bool CCPosDirectionIsBlocked(CCPos ccPos, int dirX, int dirY)
		{
			if (dirX > 1 || dirX < -1 || dirY > 1 || dirY < -1 || (dirX == 0 && dirY == 0))
				throw new ArgumentException("Invalid dirX or dirY provided, values must be within [-1,1] and not both 0");

			if (dirX != 0)
			{
				if (dirY != 0)
				{
					if (dirX == 1 && dirY == 1)
						return IsCellBlocked(thisWorld.Map.CellBottomRightOfCCPos(ccPos));
					if (dirX == -1 && dirY == -1)
						return IsCellBlocked(thisWorld.Map.CellTopLeftOfCCPos(ccPos));
					if (dirX == -1 && dirY == 1)
						return IsCellBlocked(thisWorld.Map.CellBottomLeftOfCCPos(ccPos));
					if (dirX == 1 && dirY == -1)
						return IsCellBlocked(thisWorld.Map.CellTopRightOfCCPos(ccPos));
				}
				else // dirX != 0, dirY == 0
				{
					if (dirX == 1)
						return IsCellBlocked(thisWorld.Map.CellTopRightOfCCPos(ccPos)) ||
						       IsCellBlocked(thisWorld.Map.CellBottomRightOfCCPos(ccPos));
					if (dirX == -1)
						return IsCellBlocked(thisWorld.Map.CellTopLeftOfCCPos(ccPos)) ||
						       IsCellBlocked(thisWorld.Map.CellBottomLeftOfCCPos(ccPos));
				}
			}
			else if (dirY != 0)
			{
				if (dirY == 1)
					return IsCellBlocked(thisWorld.Map.CellBottomLeftOfCCPos(ccPos)) ||
						   IsCellBlocked(thisWorld.Map.CellBottomRightOfCCPos(ccPos));
				if (dirY == -1)
					return IsCellBlocked(thisWorld.Map.CellTopLeftOfCCPos(ccPos)) ||
						   IsCellBlocked(thisWorld.Map.CellTopRightOfCCPos(ccPos));
			}

			// should never happen
			return false;
		}

		// no longer used
		public bool CCPosIsCornerOfObstacle(CCPos ccPos)
		{
			var cellsSurroundingCCPos = thisWorld.Map.GetCellsSurroundingCCPos(ccPos);
			return NumberOfCellsBlocked(Map.CellsSurroundingCCPosToList(cellsSurroundingCCPos)) == 1;
		}

		public bool CCPosIsConvexCorner(CCPos ccPos)
		{
			var topLeftBlocked = CellSurroundingCCPosIsBlocked(ccPos, CellSurroundingCorner.TopLeft);
			var topRightBlocked = CellSurroundingCCPosIsBlocked(ccPos, CellSurroundingCorner.TopRight);
			var botLeftBlocked = CellSurroundingCCPosIsBlocked(ccPos, CellSurroundingCorner.BottomLeft);
			var botRightBlocked = CellSurroundingCCPosIsBlocked(ccPos, CellSurroundingCorner.BottomRight);

			System.Console.WriteLine($"ccPos {ccPos} has blockages TL:{topLeftBlocked}" +
									 $",TR:{topRightBlocked},BL:{botLeftBlocked},BR:{botRightBlocked}");

			// At least one corner is blocked while one diagonal is free)
			return (!topLeftBlocked && !botRightBlocked && (topRightBlocked || botLeftBlocked)) ||
			       ((topLeftBlocked || botRightBlocked) && !topRightBlocked && !botLeftBlocked);
		}

		public List<Interval> SplitIntervalAtCornerPoints(Interval interval)
		{
			if (interval.Empty)
				return new List<Interval>();

			var intervalSet = new List<Interval>();
			var intervalFirstCC = interval.CCs.FirstOrDefault();
			var intervalLastCC = interval.CCs.LastOrDefault();
			var currCCPos = intervalFirstCC;
			var priorCCPos = currCCPos;
			while (currCCPos.X <= intervalLastCC.X)
			{
				// make sure to close interval if this is the last ccPos
				if ((CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.TopRight) &&
					CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.BottomRight)) ||
					currCCPos == intervalLastCC)
				{
					var intervalToAdd = new Interval(new List<CCPos>() { priorCCPos, currCCPos });
					intervalToAdd.Blocked[T] = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.TopRight);
					intervalToAdd.Blocked[B] = CellSurroundingCCPosIsBlocked(currCCPos, CellSurroundingCorner.BottomRight);
					intervalSet.Add(intervalToAdd);
					priorCCPos = currCCPos; // Next interval will start from the end of the last interval

					#if DEBUGWITHOVERLAY
					intervalToAdd.RenderIntervalIn(thisWorld);
					#endif
				}
				currCCPos = new CCPos(currCCPos.X + 1, currCCPos.Y, currCCPos.Layer);
			}

			return intervalSet;
		}

		public List<Interval> GenerateSubIntervals(int offset, int startX, int startY, int endX,
													IntervalSide intervalSide, int bound = int.MinValue, int unBlockedDirY = 0)
		{
			if (!CcYinMap(startY + offset)) // Return blank interval set if new Y is out of bounds
				return new List<Interval>();

			var intervalToUse = new Interval();
			var startCCPos = new CCPos(startX, startY);

			// If we are getting IntervalTowardsLeft or IntervalTowardsRight
			if (intervalSide == IntervalSide.Left || intervalSide == IntervalSide.Right)
			{
				// Get the clearance
				var firstInterval = GetFirstInterval(startCCPos, offset, intervalSide, unBlockedDirY);

				if (firstInterval.CCs.Count == 0) // Return blank if there are no CCs available beyond this point
					return new List<Interval>();

				var intervalFirstCC = firstInterval.CCs.FirstOrDefault();
				var intervalLastCC = firstInterval.CCs.LastOrDefault();
				int boundToUse;

				if (bound != int.MinValue) // Bound has been set
				{
					// Use the lesser of the bound and the clearance
					if (intervalSide == IntervalSide.Left)
					{
						boundToUse = intervalFirstCC.X < bound ? bound : intervalFirstCC.X;
						if (boundToUse < startX)
							intervalToUse = GetInterval(startCCPos, offset, IntervalSide.None, boundToUse, startX);
					}
					else // intervalSide == IntervalSide.Right
					{
						boundToUse = intervalLastCC.X > bound ? bound : intervalLastCC.X;
						if (boundToUse > startX)
							intervalToUse = GetInterval(startCCPos, offset, IntervalSide.None, startX, boundToUse);
					}
				}
				else
				{
					if (intervalSide == IntervalSide.Left)
						if (intervalFirstCC.X < startX)
							intervalToUse = GetInterval(startCCPos, offset, IntervalSide.None, intervalFirstCC.X, startX);
					else // intervalSide == IntervalSide.Right
						if (intervalLastCC.X > startX)
							intervalToUse = GetInterval(startCCPos, offset, IntervalSide.None, startX, intervalLastCC.X);
				}
			}
			else if (intervalSide == IntervalSide.Both)
			{
				var intervalLeft = GetFirstInterval(startCCPos, offset, IntervalSide.Left);
				var intervalRight = GetFirstInterval(startCCPos, offset, IntervalSide.Right);
				intervalToUse = MergedInterval(intervalLeft, intervalRight);
				if (!intervalToUse.NotEmptyAndHasClearance)
					intervalToUse = new Interval();
			}
			else
			{ // intervalSide is None, so we get all points from start to end
				intervalToUse = GetInterval(startCCPos, offset, IntervalSide.None, startX, endX);
			}

			intervalToUse.CCs = intervalToUse.CCs.OrderBy(cc => cc.X).ThenBy(cc => cc.Y).ToList();

			#if DEBUGWITHOVERLAY
			intervalToUse.RenderIntervalIn(thisWorld);
			#endif

			// We return the splitted sub intervals
			return SplitIntervalAtCornerPoints(intervalToUse);
		}

		public List<Interval> GenerateSubIntervals(int offset, CCPos startCCPos, int endX,
													IntervalSide intervalSide, int bound = int.MinValue, int unBlockedDirY = 0)
		{
			return GenerateSubIntervals(offset, startCCPos.X, startCCPos.Y, endX, intervalSide, bound, unBlockedDirY);
		}

		public List<Interval> GenerateSubIntervals(int offset, WPos startPos, int endX,
													IntervalSide intervalSide, int bound = int.MinValue, int unBlockedDirY = 0)
		{
			return GenerateSubIntervals(offset, GetNearestCCPos(startPos).X, GetNearestCCPos(startPos).Y, endX,
										intervalSide, bound, unBlockedDirY);
		}

		public Interval MergedInterval(Interval interval1, Interval interval2)
		{
			var mergedIntervalCCs = interval1.CCs.Union(interval2.CCs).OrderBy(cc => cc.X).ThenBy(cc => cc.Y).ToList();
			var mergedInterval = new Interval(new List<CCPos>()
				{ mergedIntervalCCs.FirstOrDefault(), mergedIntervalCCs.LastOrDefault() });
			return mergedInterval;
		}

		public void GenerateStartSuccessors(WPos start, WPos goal)
		{
			var nearestCCPos = GetNearestCCPos(start);

			var intervalSet = new List<Interval>();

			// Add top left, top right, bottom left, bottom right intervals
			for (var dir = -1; dir <= 1; dir += 1)
			{
				var intervalLeft = GetFirstInterval(nearestCCPos, dir, IntervalSide.Left);
				var intervalRight = GetFirstInterval(nearestCCPos, dir, IntervalSide.Right);
				if (intervalLeft.NotEmptyAndHasClearance || intervalRight.NotEmptyAndHasClearance)
				{
					if (dir != 0)
					{
						var intervalLeftAndRight = MergedInterval(intervalLeft, intervalRight);
						if (intervalLeftAndRight.NotEmptyAndHasClearance)
							intervalSet = intervalSet.Union(SplitIntervalAtCornerPoints(intervalLeftAndRight)).ToList();

						#if DEBUGWITHOVERLAY
						foreach (var interval in intervalSet)
							interval.RenderIntervalIn(thisWorld);
						#endif
					}
					else // dir == 0
					{
						if (intervalLeft.NotEmptyAndHasClearance)
							intervalSet.Add(intervalLeft);
						if (intervalRight.NotEmptyAndHasClearance)
							intervalSet.Add(intervalRight);
					}
				}
			}

			foreach (var interval in intervalSet)
			{
				if (interval.CCs.Count > 0)
				{
					var state = new IntervalState(interval, start, goal, thisWorld);
					InitialStates.Add(state);
					AddStateToOpen(state);
				}
			}

			foreach (var intervalState in OpenList)
				intervalState.Gval = 0;

		}

		public CCPos FurthestCCPosFromPos(WPos sourceWPos, List<CCPos> destCCs)
		{
			var maxDist = long.MinValue;
			var maxCCindex = 0;
			for (var i = 0; i < destCCs.Count; i += 1)
			{
				var ccWPos = thisWorld.Map.WPosFromCCPos(destCCs.ElementAt(i));
				var ccWPosDistToSource = (ccWPos - sourceWPos).HorizontalLengthSquared;
				if (ccWPosDistToSource > maxDist)
				{
					maxDist = ccWPosDistToSource;
					maxCCindex = i;
				}
			}

			return destCCs.ElementAt(maxCCindex);
		}

		public CCPos FurthestCCPosFromPos(CCPos sourceCC, List<CCPos> destCCs)
		{
			return FurthestCCPosFromPos(thisWorld.Map.WPosFromCCPos(sourceCC), destCCs);
		}

		public void GenerateSuccessors_DifferentRow(WPos rootPos, Interval interval, WPos goal, ref List<IntervalState> successors)
		{
			var dirY = interval.CCs.FirstOrDefault().Y < GetNearestCCPos(rootPos).Y ? -1 : 1; // if root is lower, direction is up, and vice versa
			var reverseDirY = dirY * (-1);
			var newRowY = interval.CCs.FirstOrDefault().Y + dirY;
			var newRowWPosY = thisWorld.Map.WPosFromCCPos(new CCPos(0, newRowY)).Y;
			var intervalFirstCC = interval.CCs.FirstOrDefault();
			var intervalFirstCCWPos = thisWorld.Map.WPosFromCCPos(intervalFirstCC);
			var intervalLastCC = interval.CCs.LastOrDefault();
			var intervalLastCCWPos = thisWorld.Map.WPosFromCCPos(intervalLastCC);

			var intersectingLeftEdge = WPos.GetIntersectingX(rootPos, intervalFirstCCWPos, newRowWPosY);
			var intersectingRightEdge = WPos.GetIntersectingX(rootPos, intervalLastCCWPos, newRowWPosY);

			// Because we have not implemented WPos corners yet, we have to round to the nearest CCPos
			intersectingLeftEdge = GetNearestCCPos(new WPos(intersectingLeftEdge, newRowWPosY, intervalFirstCC.Layer)).X;
			intersectingRightEdge = GetNearestCCPos(new WPos(intersectingRightEdge, newRowWPosY, intervalFirstCC.Layer)).X;

			var nextLeftInterval = GetFirstInterval(intervalFirstCC, dirY, IntervalSide.Left);
			var leftBound = nextLeftInterval.NotEmptyAndHasClearance ? nextLeftInterval.CCs.FirstOrDefault().X : intervalFirstCC.X;
			var nextRightInterval = GetFirstInterval(intervalLastCC, dirY, IntervalSide.Right);
			var rightBound = nextRightInterval.NotEmptyAndHasClearance ? nextRightInterval.CCs.LastOrDefault().X : intervalLastCC.X;

			var newLeft = intersectingLeftEdge < leftBound ? leftBound : intersectingLeftEdge;
			var newRight = intersectingRightEdge > rightBound ? rightBound : intersectingRightEdge;

			if (interval.Blocked[dirY])
			{
				var intervalSet = GenerateSubIntervals(0, newLeft, newRowY, newRight, IntervalSide.None);
				AddIntervalListToStateList(intervalSet, ref successors, rootPos, goal);

				#if DEBUGWITHOVERLAY
				foreach (var intval in intervalSet)
					intval.RenderIntervalIn(thisWorld);
				#endif
			}

			// TO DO: Add double support
			// NOTE: Once WPos corners are implemented, we need to add Math.Abs(intervalFirstCC.XDbl - intervalFirstCC.X)
			if (CCPosIsConvexCorner(intervalFirstCC))
			{
				if (CCPosDirectionIsBlocked(intervalFirstCC, -1, reverseDirY))
				{
					// TO DO: Add caching of states within an Index table like in the C++ code (anya_index_table)
					var intervalLeftOfInterval = GetFirstInterval(intervalFirstCC, 0, IntervalSide.Left);
					if (intervalLeftOfInterval.CCs.Count > 0)
						successors.Add(new IntervalState(intervalLeftOfInterval, intervalFirstCCWPos, goal, thisWorld));
				}

				if (CCPosDirectionIsBlocked(intervalFirstCC, 1, dirY) &&
				    CCPosDirectionIsBlocked(intervalFirstCC, -1, reverseDirY))
				{
					var intervalSet = GenerateSubIntervals(0, intervalFirstCC.X, newRowY, 0, IntervalSide.Left, unBlockedDirY: reverseDirY);
					AddIntervalListToStateList(intervalSet, ref successors, intervalFirstCCWPos, goal);
				}
				else if (CCPosDirectionIsBlocked(intervalFirstCC, -1, dirY))
				{
					if (intervalFirstCC.X > GetNearestCCPos(rootPos).X)
					{
						var intervalSet = GenerateSubIntervals(0, intervalFirstCC.X, newRowY, 0, IntervalSide.Right, newLeft, reverseDirY);
						AddIntervalListToStateList(intervalSet, ref successors, intervalFirstCCWPos, goal);
					}
				}
				else if (CCPosDirectionIsBlocked(intervalFirstCC, -1, reverseDirY))
				{
					if (newLeft == intersectingLeftEdge)
					{
						var intervalSet = GenerateSubIntervals(0, newLeft, newRowY, 0, IntervalSide.Left, unBlockedDirY: reverseDirY);
						AddIntervalListToStateList(intervalSet, ref successors, intervalFirstCCWPos, goal);
					}
				}
			}

			if (CCPosIsConvexCorner(intervalLastCC))
			{
				if (CCPosDirectionIsBlocked(intervalLastCC, 1, reverseDirY))
				{
					// TO DO: Add caching of states within an Index table like in the C++ code (anya_index_table)
					var intervalRightOfInterval = GetFirstInterval(intervalLastCC, 0, IntervalSide.Right);
					if (intervalRightOfInterval.CCs.Count > 0)
						successors.Add(new IntervalState(intervalRightOfInterval, intervalLastCCWPos, goal, thisWorld));
				}

				if (CCPosDirectionIsBlocked(intervalLastCC, -1, dirY) &&
				    CCPosDirectionIsBlocked(intervalLastCC, 1, reverseDirY))
				{
					var intervalSet = GenerateSubIntervals(0, intervalLastCC.X, newRowY, 0, IntervalSide.Right, unBlockedDirY: reverseDirY);
					AddIntervalListToStateList(intervalSet, ref successors, intervalLastCCWPos, goal);
				}
				else if (CCPosDirectionIsBlocked(intervalLastCC, 1, dirY))
				{
					if (intervalLastCC.X < GetNearestCCPos(rootPos).X)
					{
						var intervalSet = GenerateSubIntervals(0, intervalLastCC.X, newRowY, 0, IntervalSide.Left, newRight, reverseDirY);
						AddIntervalListToStateList(intervalSet, ref successors, intervalLastCCWPos, goal);
					}
				}
				else if (CCPosDirectionIsBlocked(intervalLastCC, 1, reverseDirY))
				{
					if (newRight == intersectingRightEdge)
					{
						var intervalSet = GenerateSubIntervals(0, newRight, newRowY, 0, IntervalSide.Right, unBlockedDirY: reverseDirY);
						AddIntervalListToStateList(intervalSet, ref successors, intervalLastCCWPos, goal);
					}
				}
			}
		}

		public void GenerateSuccessors_SameRow(WPos start, Interval interval, WPos goal, ref List<IntervalState> successors)
		{
			var intervalFirstCC = interval.CCs.FirstOrDefault();
			var intervalFirstCCWPos = thisWorld.Map.WPosFromCCPos(intervalFirstCC);
			var intervalLastCC = interval.CCs.LastOrDefault();
			var intervalLastCCWPos = thisWorld.Map.WPosFromCCPos(intervalLastCC);
			var startCCPos = GetNearestCCPos(start);

			var intervalLeft = GetFirstInterval(intervalFirstCC, 0, IntervalSide.Left);
			var intervalRight = GetFirstInterval(intervalLastCC, 0, IntervalSide.Right);

			if (startCCPos.X >= intervalLastCC.X && intervalLeft.NotEmptyAndHasClearance) // at least one CCPos is unblocked
			{
				successors.Add(new IntervalState(intervalLeft, start, goal, thisWorld));
				for (var dirY = -1; dirY <= 1; dirY += 1)
				{
					if (dirY != 0 && intervalLeft.Blocked[dirY]) // Top is blocked
					{
						var intervalSet = GenerateSubIntervals(dirY, intervalFirstCC, 0, IntervalSide.Left, unBlockedDirY: dirY * (-1));
						AddIntervalListToStateList(intervalSet, ref successors, intervalFirstCCWPos, goal);
					}
				}
			}

			if (startCCPos.X <= intervalFirstCC.X && intervalRight.NotEmptyAndHasClearance) // at least one CCPos is unblocked
			{
				successors.Add(new IntervalState(intervalRight, start, goal, thisWorld));
				for (var dirY = -1; dirY <= 1; dirY += 1)
				{
					if (dirY != 0 && intervalRight.Blocked[dirY]) // Top is blocked
					{
						var intervalSet = GenerateSubIntervals(dirY, intervalLastCC, 0, IntervalSide.Right, unBlockedDirY: dirY * (-1));
						AddIntervalListToStateList(intervalSet, ref successors, intervalLastCCWPos, goal);
					}
				}
			}
		}

		public void GenerateSuccessors(WPos start, Interval interval, WPos goal, ref List<IntervalState> successors)
		{
			if (interval.CCs.Count > 0)
			{
				if (GetNearestCCPos(start).Y != interval.CCs.FirstOrDefault().Y)
					GenerateSuccessors_DifferentRow(start, interval, goal, ref successors);
				else
					GenerateSuccessors_SameRow(start, interval, goal, ref successors);
			}
		}

		#region Constructors
		public AnyaPathSearch(World world, Actor self)
		{
			thisWorld = world;
			this.self = self;
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
