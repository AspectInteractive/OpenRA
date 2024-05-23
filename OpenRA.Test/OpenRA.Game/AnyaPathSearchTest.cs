using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Network;

#pragma warning disable SA1005 // Single line comments should begin with single space
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
namespace OpenRA.Test
{
	[TestFixture]
	public class AnyaPathSearchTest
	{
		private string modIDstr = "ra";
		private string mapName = "warwind.oramap";
		private OrderManager orderManager;

		internal void PrepareWorld()
		{
			var mods = new InstalledMods(new[] { Path.Combine(Platform.EngineDir, "mods") }, new string[0]);
			var modData = new ModData(mods[modIDstr], mods, true);
			var mapPreview = modData.MapCache.SingleOrDefault(m => Path.GetFileName(m.Package.Name) == mapName);
			var map = modData.PrepareMap(mapPreview.Uid);
			orderManager = new OrderManager(new EchoConnection());
			//world = new World(modData, map, orderManager, WorldType.Regular);
		}

		[TestCase(TestName = "Testing basic Anya Path Search")]
		public void BasicExample()
		{
			// var inputs = new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
			var inputs = GenRandomIntList(1, 11, 10, true);
			System.Console.WriteLine($"Inputs: {string.Join(", ", inputs)}");
			PrepareWorld();
			//var anyaSearch = new AnyaPathSearch();
			//anyaSearch.AnyaFindPath(world.Map.WPosFromCCPos(new CCPos(0, 0)), world.Map.WPosFromCCPos(new CCPos(5, 5)));
		}

		// Generate a random list of integers within [minInt, maxInt)
		public static List<int> GenRandomIntList(int minInt, int maxIntExc, int maxSize, bool uniqueOnly)
		{
			if (uniqueOnly && Math.Abs(maxIntExc - minInt) < maxSize)
				throw new InvalidOperationException($"A unique list of size {maxSize} needs more integers than those within [{minInt}, {maxIntExc})");

			var rnd = new Random();
			var randomList = new List<int>();
			while (randomList.Count < maxSize)
			{
				var newInt = rnd.Next(minInt, maxIntExc);
				if (!uniqueOnly || !randomList.Contains(newInt))
					randomList.Add(newInt);
			}

			return randomList;
		}
	}
}
