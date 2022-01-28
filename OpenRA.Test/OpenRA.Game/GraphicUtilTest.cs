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
using System.Linq;
using NUnit.Framework;
using OpenRA.Graphics;

namespace OpenRA.Test
{
	[TestFixture]
	public class GraphicUtilTest
	{
		[TestCase(TestName = "Rotate a 2D Square using the RotatePoint function")]
		public void RotatePointTest()
		{
			var angleToRotate = new WAngle(128);
			var oneSidedThreshold = new float3(0.5F, 0.5F, 0.5F);

			// Initial points
			var initialPoints = new List<float3>()
			{
				new float3(420, 540, 568),
				new float3(468, 540, 568),
				new float3(468, 588, 568),
				new float3(420, 588, 568)
			};

			var centerPoint = (initialPoints[0] + initialPoints[2]) / 2;

			// Expected Rotated points
			var expectedRotatedPoints = new List<float3>()
			{
				new float3(444, 530, 568),
				new float3(478, 564, 568),
				new float3(444, 598, 568),
				new float3(410, 564, 568)
			};

			// Rotate each point
			for (var i = 0; i < initialPoints.Count; i++)
			{
				var angleToRotateSin = (float)Math.Sin(angleToRotate.RendererRadians());
				var angleToRotateCos = (float)Math.Cos(angleToRotate.RendererRadians());
				var rotatedPoint = Util.RotatePoint(initialPoints.ElementAt(i), centerPoint, angleToRotateSin, angleToRotateCos);
				var expectedPoint = expectedRotatedPoints.ElementAt(i);

				// Check the rotate points are correct
				Assert.That(rotatedPoint.X <= expectedPoint.X + oneSidedThreshold.X &&
							rotatedPoint.X >= expectedPoint.X - oneSidedThreshold.X &&
							rotatedPoint.Y <= expectedPoint.Y + oneSidedThreshold.Y &&
							rotatedPoint.Y >= expectedPoint.Y - oneSidedThreshold.Y);

				System.Console.WriteLine($"rotated: {rotatedPoint}, expected: {expectedPoint}, diff: {expectedPoint - rotatedPoint}");
			}
		}
	}
}
