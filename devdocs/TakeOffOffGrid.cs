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

using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class TakeOffOffGrid : Activity
	{
		readonly MobileOffGrid mobileOffGrid;
		readonly IMove move;

		public TakeOffOffGrid(Actor self)
		{
			mobileOffGrid = self.Trait<MobileOffGrid>();
			move = self.Trait<IMove>();
		}

		protected override void OnFirstRun(Actor self)
		{
			System.Console.WriteLine("TakeOffOffGrid.OnFirstRun() called at " + (System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond));
			if (mobileOffGrid.ForceLanding)
				return;

			if (self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition).Length >= mobileOffGrid.Info.MinAirborneAltitude)
				return;

			// We are taking off, so remove influence in ground cells.
			mobileOffGrid.RemoveInfluence();

			if (mobileOffGrid.Info.TakeoffSounds.Length > 0)
				Game.Sound.Play(SoundType.World, mobileOffGrid.Info.TakeoffSounds, self.World, mobileOffGrid.CenterPosition);
		}

		public override bool Tick(Actor self)
		{
			// Refuse to take off if it would land immediately again.
			if (mobileOffGrid.ForceLanding)
			{
				Cancel(self);
				return true;
			}

			var dat = self.World.Map.DistanceAboveTerrain(mobileOffGrid.CenterPosition);
			if (dat < mobileOffGrid.Info.CruiseAltitude)
			{
				// If we're a VTOL, rise before flying forward
				/*if (mobileOffGrid.Info.VTOL)
				{
					MoveOffGrid.VerticalTakeOffOrLandTick(self, mobileOffGrid, mobileOffGrid.Facing, mobileOffGrid.Info.CruiseAltitude);
					return false;
				}*/

				MoveOffGrid.MoveOffGridTick(self, mobileOffGrid, mobileOffGrid.Facing, mobileOffGrid.Info.CruiseAltitude);
				return false;
			}

			return true;
		}
	}
}
