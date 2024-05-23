using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common
{
	public class Footprint
	{
		public Dictionary<CVec, FootprintCellType> Cells { get; private set; } = new Dictionary<CVec, FootprintCellType>();

		public Footprint(MiniYaml yaml)
		{
			var footprintYaml = yaml.Nodes.FirstOrDefault(n => n.Key == "Footprint");
			var footprintChars = footprintYaml?.Value.Value.Where(x => !char.IsWhiteSpace(x)).ToArray() ?? new[] { 'x' };

			var dimensionsYaml = yaml.Nodes.FirstOrDefault(n => n.Key == "Dimensions");
			var dim = dimensionsYaml != null ? FieldLoader.GetValue<CVec>("Dimensions", dimensionsYaml.Value.Value) : new CVec(1, 1);

			if (footprintChars.Length != dim.X * dim.Y)
			{
				var fp = footprintYaml.Value.Value;
				var dims = dim.X + "x" + dim.Y;
				throw new YamlException($"Invalid footprint: {fp} does not match dimensions {dims}");
			}

			var index = 0;
			for (var y = 0; y < dim.Y; y++)
			{
				for (var x = 0; x < dim.X; x++)
				{
					var c = footprintChars[index++];
					if (!Enum.IsDefined(typeof(FootprintCellType), (FootprintCellType)c))
						throw new YamlException($"Invalid footprint cell type '{c}'");

					Cells[new CVec(x, y)] = (FootprintCellType)c;
				}
			}
		}

		public IEnumerable<CPos> FootprintTiles(CPos location, FootprintCellType type)
		{
			return Cells.Where(kv => kv.Value == type).Select(kv => location + kv.Key);
		}

		public IEnumerable<CPos> Tiles(CPos location)
		{
			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedPassable))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.Occupied))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedUntargetable))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedPassableTransitOnly))
				yield return t;
		}

		public IEnumerable<CPos> FrozenUnderFogTiles(CPos location)
		{
			foreach (var t in FootprintTiles(location, FootprintCellType.Empty))
				yield return t;

			foreach (var t in Tiles(location))
				yield return t;
		}

		public IEnumerable<CPos> OccupiedTiles(CPos location)
		{
			foreach (var t in FootprintTiles(location, FootprintCellType.Occupied))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedUntargetable))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedPassableTransitOnly))
				yield return t;
		}

		public IEnumerable<CPos> PathableTiles(CPos location)
		{
			foreach (var t in FootprintTiles(location, FootprintCellType.Empty))
				yield return t;

			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedPassable))
				yield return t;
		}

		public IEnumerable<CPos> TransitOnlyTiles(CPos location)
		{
			foreach (var t in FootprintTiles(location, FootprintCellType.OccupiedPassableTransitOnly))
				yield return t;
		}
	}
}
