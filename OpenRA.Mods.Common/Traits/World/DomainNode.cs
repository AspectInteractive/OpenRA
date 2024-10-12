using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static OpenRA.Mods.Common.Traits.ThetaPathfinderExecutionManager;

namespace OpenRA.Mods.Common.Traits
{
	public class DomainNode<T>
	{
		public T Value;

		public DomainNode(T value)
		{
			Value = value;
		}
	}
}
