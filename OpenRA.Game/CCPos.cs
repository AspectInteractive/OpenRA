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
using Eluant;
using Eluant.ObjectBinding;
using OpenRA.Scripting;

namespace OpenRA
{
	public class CCPos : IScriptBindable, ILuaAdditionBinding, ILuaSubtractionBinding, ILuaEqualityBinding, ILuaTableBinding, IEquatable<CPos>
	{
		public const int TL = 0;
		public const int TR = 1;
		public const int BL = 2;
		public const int BR = 3;

		public Dictionary<int, bool> Blocked = new Dictionary<int, bool>()
													{
														{ TL, false },
														{ TR, false },
														{ BL, false },
														{ BR, false }
													};

		// Coordinates are packed in a 32 bit signed int
		// X and Y are 12 bits (signed): -2048...2047
		// Layer is an unsigned byte
		// Packing is XXXX XXXX XXXX YYYY YYYY YYYY LLLL LLLL
		public readonly int Bits;

		// X is padded to MSB, so bit shift does the correct sign extension
		public int X => Bits >> 20;

		// Align Y with a short, cast, then shift the rest of the way
		// The signed short bit shift does the correct sign extension
		public int Y => ((short)(Bits >> 4)) >> 4;

		public byte Layer => (byte)Bits;
		private static void XYLayerToBits(ref int bits, int x, int y, byte layer) => bits = (x & 0xFFF) << 20 | (y & 0xFFF) << 8 | layer;

		public CCPos(int bits) { Bits = bits; }
		public CCPos(World world, CPos cell, Func<CPos, WPos> cornerFunc)
		{
			if (cornerFunc == world.Map.TopLeftOfCell)
				XYLayerToBits(ref Bits, cell.X, cell.Y, 0);
			else if (cornerFunc == world.Map.TopRightOfCell)
				XYLayerToBits(ref Bits, cell.X + 1, cell.Y, 0);
			else if (cornerFunc == world.Map.BottomLeftOfCell)
				XYLayerToBits(ref Bits, cell.X, cell.Y + 1, 0);
			else if (cornerFunc == world.Map.BottomRightOfCell)
				XYLayerToBits(ref Bits, cell.X + 1, cell.Y + 1, 0);
		}

		public CCPos(int x, int y)
			: this(x, y, 0) { }

		public CCPos(int x, int y, byte layer) { XYLayerToBits(ref Bits, x, y, layer); }

		public static readonly CCPos Zero = new CCPos(0, 0, 0);

		public static explicit operator CCPos(int2 a) { return new CCPos(a.X, a.Y); }

		public static CCPos operator +(CVec a, CCPos b) { return new CCPos(a.X + b.X, a.Y + b.Y, b.Layer); }
		public static CCPos operator +(CCPos a, CVec b) { return new CCPos(a.X + b.X, a.Y + b.Y, a.Layer); }
		public static CCPos operator -(CCPos a, CVec b) { return new CCPos(a.X - b.X, a.Y - b.Y, a.Layer); }
		public static CVec operator -(CCPos a, CCPos b) { return new CVec(a.X - b.X, a.Y - b.Y); }

		public static bool operator ==(CCPos me, CCPos other) { return me.Bits == other.Bits; }
		public static bool operator !=(CCPos me, CCPos other) { return !(me == other); }

		public override int GetHashCode() { return Bits.GetHashCode(); }

		public bool Equals(CPos other) { return Bits == other.Bits; }
		public override bool Equals(object obj) { return obj is CPos && Equals((CPos)obj); }

		public override string ToString() { return X + "," + Y; }

		public MPos ToMPos(Map map)
		{
			return ToMPos(map.Grid.Type);
		}

		public MPos ToMPos(MapGridType gridType)
		{
			if (gridType == MapGridType.Rectangular)
				return new MPos(X, Y);

			// Convert from RectangularIsometric cell (x, y) position to rectangular map position (u, v)
			//  - The staggered rows make this fiddly (hint: draw a diagram!)
			// (a) Consider the relationships:
			//  - +1x (even -> odd) adds (0, 1) to (u, v)
			//  - +1x (odd -> even) adds (1, 1) to (u, v)
			//  - +1y (even -> odd) adds (-1, 1) to (u, v)
			//  - +1y (odd -> even) adds (0, 1) to (u, v)
			// (b) Therefore:
			//  - ax + by adds (a - b)/2 to u (only even increments count)
			//  - ax + by adds a + b to v
			var u = (X - Y) / 2;
			var v = X + Y;
			return new MPos(u, v);
		}

		#region Scripting interface

		public LuaValue Add(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			if (!left.TryGetClrValue(out CPos a) || !right.TryGetClrValue(out CVec b))
				throw new LuaException($"Attempted to call CPos.Add(CPos, CVec) with invalid arguments ({left.WrappedClrType().Name}, {right.WrappedClrType().Name})");

			return new LuaCustomClrObject(a + b);
		}

		public LuaValue Subtract(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			var rightType = right.WrappedClrType();
			if (!left.TryGetClrValue(out CPos a))
				throw new LuaException($"Attempted to call CPos.Subtract(CPos, (CPos|CVec)) with invalid arguments ({left.WrappedClrType().Name}, {rightType.Name})");

			if (rightType == typeof(CPos))
			{
				right.TryGetClrValue(out CPos b);
				return new LuaCustomClrObject(a - b);
			}
			else if (rightType == typeof(CVec))
			{
				right.TryGetClrValue(out CVec b);
				return new LuaCustomClrObject(a - b);
			}

			throw new LuaException($"Attempted to call CPos.Subtract(CPos, (CPos|CVec)) with invalid arguments ({left.WrappedClrType().Name}, {rightType.Name})");
		}

		public LuaValue Equals(LuaRuntime runtime, LuaValue left, LuaValue right)
		{
			if (!left.TryGetClrValue(out CPos a) || !right.TryGetClrValue(out CPos b))
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
					case "Layer": return Layer;
					default: throw new LuaException($"CPos does not define a member '{key}'");
				}
			}

			set => throw new LuaException("CPos is read-only. Use CPos.New to create a new value");
		}

		#endregion
	}
}
