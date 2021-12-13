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
using Eluant;
using Eluant.ObjectBinding;
using OpenRA.Primitives;
using OpenRA.Scripting;

namespace OpenRA
{
	public readonly struct WPos : IScriptBindable, ILuaAdditionBinding, ILuaSubtractionBinding, ILuaEqualityBinding, ILuaTableBinding, IEquatable<WPos>
	{
		public readonly int X, Y, Z;

		public WPos(int x, int y, int z) { X = x; Y = y; Z = z; }
		public WPos(WDist x, WDist y, WDist z) { X = x.Length; Y = y.Length; Z = z.Length; }

		public static readonly WPos Zero = new(0, 0, 0);

		public static explicit operator WVec(in WPos a) { return new WVec(a.X, a.Y, a.Z); }

		public static WPos operator +(in WPos a, in WVec b) { return new WPos(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
		public static WPos operator -(in WPos a, in WVec b) { return new WPos(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
		public static WVec operator -(in WPos a, in WPos b) { return new WVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }

		public static bool operator ==(in WPos me, in WPos other) { return me.X == other.X && me.Y == other.Y && me.Z == other.Z; }
		public static bool operator !=(in WPos me, in WPos other) { return !(me == other); }

		/// <summary>
		/// Returns the linear interpolation between points 'a' and 'b'.
		/// </summary>
		public static WPos Lerp(in WPos a, in WPos b, int mul, int div) { return a + (b - a) * mul / div; }
		public int2 XYToInt2() { return new int2(X, Y); }
		public static int2 XYToInt2(in WPos a) { return new int2(a.X, a.Y); }

		/// <summary>
		/// Returns the linear interpolation between points 'a' and 'b'.
		/// </summary>
		public static WPos Lerp(in WPos a, in WPos b, long mul, long div)
		{
			// The intermediate variables may need more precision than
			// an int can provide, so we can't use WPos.
			var x = (int)(a.X + (b.X - a.X) * mul / div);
			var y = (int)(a.Y + (b.Y - a.Y) * mul / div);
			var z = (int)(a.Z + (b.Z - a.Z) * mul / div);

			return new WPos(x, y, z);
		}

		public static WPos LerpQuadratic(in WPos a, in WPos b, WAngle pitch, int mul, int div)
		{
			// Start with a linear lerp between the points
			var ret = Lerp(a, b, mul, div);

			if (pitch.Angle == 0)
				return ret;

			// Add an additional quadratic variation to height
			// Uses decimal to avoid integer overflow
			var offset = (decimal)(b - a).Length * pitch.Tan() * mul * (div - mul) / (1024 * div * div);
			var clampedOffset = (int)(offset + ret.Z).Clamp(int.MinValue, int.MaxValue);

			return new WPos(ret.X, ret.Y, clampedOffset);
		}

		public static int GetIntersectingX(WPos p1, WPos p2, int y)
		{
			return Math.Abs(p2.Y - p1.Y) < double.Epsilon ? p1.Y : ((p2.X - p1.X) * (y - p1.Y) / (p2.Y - p1.Y)) + p1.X;
		}

		public static int GetIntersectingY(WPos p1, WPos p2, int x)
		{
			return Math.Abs(p2.X - p1.X) < double.Epsilon ? p1.X : ((p2.Y - p1.Y) * (x - p1.X) / (p2.X - p1.X)) + p1.Y;
		}

		// Check the direction these three points rotate
		private static int DoTwoLinesIntersect_RotationDirection(WPos p1, WPos p2, WPos p3)
		{
			if (((p3.Y - p1.Y) * (p2.X - p1.X)) > ((p2.Y - p1.Y) * (p3.X - p1.X)))
				return 1;
			else if (((p3.Y - p1.Y) * (p2.X - p1.X)) == ((p2.Y - p1.Y) * (p3.X - p1.X)))
				return 0;
			else
				return -1;
		}

		private static bool DoTwoLinesIntersect_ContainsSegment(WPos p1, WPos p2, WPos s)
		{
			if (p1.X < p2.X && p1.X < s.X && s.X < p2.X)
				return true;
			else if (p2.X < p1.X && p2.X < s.X && s.X < p1.X)
				return true;
			else if (p1.Y < p2.Y && p1.Y < s.Y && s.Y < p2.Y)
				return true;
			else if (p2.Y < p1.Y && p2.Y < s.Y && s.Y < p1.Y)
				return true;
			else if ((p1.X == s.X && p1.Y == s.Y) || (p2.X == s.X && p2.Y == s.Y))
				return true;
			return false;
		}

		public static bool DoTwoLinesIntersect(WPos a0, WPos a1, WPos b0, WPos b1)
		{
			var f1 = DoTwoLinesIntersect_RotationDirection(a0, a1, b1);
			var f2 = DoTwoLinesIntersect_RotationDirection(a0, a1, b0);
			var f3 = DoTwoLinesIntersect_RotationDirection(a0, b0, b1);
			var f4 = DoTwoLinesIntersect_RotationDirection(a1, b0, b1);

			// If the faces rotate opposite directions, they intersect.
			var intersect = f1 != f2 && f3 != f4;

			// If the segments are on the same line, we have to check for overlap.
			if (f1 == 0 && f2 == 0 && f3 == 0 && f4 == 0)
			{
				intersect = DoTwoLinesIntersect_ContainsSegment(a0, a1, b0) ||
							DoTwoLinesIntersect_ContainsSegment(a0, a1, b1) ||
							DoTwoLinesIntersect_ContainsSegment(b0, b1, a0) ||
							DoTwoLinesIntersect_ContainsSegment(b0, b1, a1);
			}

			return intersect;
		}

		public static WPos? FindIntersectionTwoLines(WPos p1, WPos p2, WPos p3, WPos p4)
		{
			// Line AB represented as a1x + b1y = c1
			var a = p2.Y - p1.Y;
			var b = p1.X - p2.X;
			var c = a * p1.X + b * p1.Y;

			// Line CD represented as a2x + b2y = c2
			var a1 = p4.Y - p3.Y;
			var b1 = p3.X - p4.X;
			var c1 = a1 * p3.X + b1 * p3.Y;
			var det = a * b1 - a1 * b;

			if (det == 0)
				return null;
			else
			{
				var x = (Fix64)(b1 * c - b * c1) / (Fix64)det;
				var y = (Fix64)(a * c1 - a1 * c) / (Fix64)det;
				return new WPos((int)x, (int)y, 0);
			}
		}

		// Returns 1 if the lines intersect, otherwise 0. In addition, if the lines
		// intersect the intersection point may be stored in the floats i_x and i_y.
		#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
		#pragma warning disable SA1513 // Closing brace should be followed by blank line
		public static WPos? FindIntersection(WPos p0, WPos p1, WPos p2, WPos p3)
		{
			var s1x = p1.X - p0.X;
			var s1y = p1.Y - p0.Y;
			var s2x = p3.X - p2.X;
			var s2y = p3.Y - p2.Y;
			var det = (Fix64)(-s2x * s1y + s1x * s2y);
			var p0overlaps = p2.X <= p0.X && p0.X <= p3.X && p2.Y <= p0.Y && p0.Y <= p3.Y;
			var p1overlaps = p2.X <= p1.X && p1.X <= p3.X && p2.Y <= p1.Y && p1.Y <= p3.Y;

			if (det != (Fix64)0)
			{
				var s = (Fix64)(-s1y * (p0.X - p2.X) + s1x * (p0.Y - p2.Y)) / det;
				var t = (Fix64)(s2x * (p0.Y - p2.Y) - s2y * (p0.X - p2.X)) / det;
				if (s >= (Fix64)0 && s <= (Fix64)1 && t >= (Fix64)0 && t <= (Fix64)1) // Collision detected
					return new WPos((int)((Fix64)p0.X + (t * (Fix64)s1x))
								   , (int)((Fix64)p0.Y + (t * (Fix64)s1y)), 0);
			}
			// Check if lines are collinear, one of the end points must intersect
			else if (p0overlaps)
				if (p1overlaps)
					return p0; // since entire line is in the segment it does not matter which point we return
				else if (p2.X > p1.X || p2.Y > p1.Y) // p1 is to the left of p2->p3, so p2 is the intersection
					return p2;
				else if (p3.X < p1.X || p3.Y < p1.Y) // p1 is to the right of p2->p3, so p3 is the intersection
					return p3;
			else if (p1overlaps)
				if (p2.X > p0.X || p2.Y > p0.Y)
					return p2;
				else if (p3.X < p0.X || p3.Y < p0.Y)
					return p3;
			return null; // No collision, and parallel if det == 0
		}
		#pragma warning restore SA1515 // Single-line comment should be preceded by blank line
		#pragma warning restore SA1513 // Closing brace should be followed by blank line

		public override int GetHashCode() { return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode(); }

		public bool Equals(WPos other) { return other == this; }
		public override bool Equals(object obj) { return obj is WPos pos && Equals(pos); }

		public override string ToString() { return X + "," + Y + "," + Z; }

		#region Scripting interface

		public LuaValue Add(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			if (!left.TryGetClrValue(out WPos a) || !right.TryGetClrValue(out WVec b))
				throw new LuaException($"Attempted to call WPos.Add(WPos, WVec) with invalid arguments ({left.WrappedClrType().Name}, {right.WrappedClrType().Name})");

			return new LuaCustomClrObject(a + b);
		}

		public LuaValue Subtract(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			var rightType = right.WrappedClrType();
			if (!left.TryGetClrValue(out WPos a))
				throw new LuaException($"Attempted to call WPos.Subtract(WPos, (WPos|WVec)) with invalid arguments ({left.WrappedClrType().Name}, {rightType.Name})");

			if (rightType == typeof(WPos))
			{
				right.TryGetClrValue(out WPos b);
				return new LuaCustomClrObject(a - b);
			}
			else if (rightType == typeof(WVec))
			{
				right.TryGetClrValue(out WVec b);
				return new LuaCustomClrObject(a - b);
			}

			throw new LuaException($"Attempted to call WPos.Subtract(WPos, (WPos|WVec)) with invalid arguments ({left.WrappedClrType().Name}, {rightType.Name})");
		}

		public LuaValue Equals(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			if (!left.TryGetClrValue(out WPos a) || !right.TryGetClrValue(out WPos b))
				return false;

			return a == b;
		}

		public LuaValue this[LuaRuntime runtime, LuaValue key]
		{
			get
			{
				switch (key.ToString())
				{
					case "X": return X;
					case "Y": return Y;
					case "Z": return Z;
					default: throw new LuaException($"WPos does not define a member '{key}'");
				}
			}

			set => throw new LuaException("WPos is read-only. Use WPos.New to create a new value");
		}

		#endregion
	}

	public static class IEnumerableExtensions
	{
		public static WPos Average(this IEnumerable<WPos> source)
		{
			var length = 0;
			var x = 0L;
			var y = 0L;
			var z = 0L;
			foreach (var pos in source)
			{
				length++;
				x += pos.X;
				y += pos.Y;
				z += pos.Z;
			}

			if (length == 0)
				return WPos.Zero;

			x /= length;
			y /= length;
			z /= length;

			return new WPos((int)x, (int)y, (int)z);
		}
	}
}
