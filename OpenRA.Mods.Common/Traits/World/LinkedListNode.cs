using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static OpenRA.Mods.Common.Traits.ThetaPathfinderExecutionManager;

namespace OpenRA.Mods.Common.Traits
{
	public class LinkedListNodeHead<T> : LinkedListNode<T>
	{
		public new int ID = -1;
		public bool Blocked;

		public LinkedListNodeHead(LinkedListNode<T> node, bool blocked)
			: base(node.Value, node.IsValidParent)
		{
			Parent = node.Parent;
			Children = node.Children;
			Head = null;
			Value = node.Value;
			Blocked = blocked;
		}

		public LinkedListNodeHead(T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator, bool blocked, int id = -1)
			: base(value, parentValidator)
		{
			Blocked = blocked;
			if (id != -1)
				ID = id;
		}

		public LinkedListNodeHead(LinkedListNode<T> parent, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator,
			bool blocked, int id = -1)
			: base(parent, value, parentValidator)
		{
			Blocked = blocked;
			if (id != -1)
				ID = id;
		}
	}

	public class LinkedListNode<T>
	{
		public LinkedListNode<T> Parent;
		public List<LinkedListNode<T>> Children = new();
		public Func<LinkedListNode<T>, LinkedListNode<T>, bool> IsValidParent;
		public LinkedListNodeHead<T> Head;
		public T Value;

		public int ID => GetID();

		public int GetID()
		{
			if (this is LinkedListNodeHead<T> head)
				return head.ID;
			return Head.ID;
		}

		public LinkedListNode(LinkedListNode<T> parent, LinkedListNodeHead<T> head, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
			: this(parent, value, parentValidator)
			=> Head = head;

		public LinkedListNode(LinkedListNodeHead<T> head, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
			: this(value, parentValidator)
			=> Head = head;

		public LinkedListNode(LinkedListNode<T> parent, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
			: this(value, parentValidator)
			=> Parent = parent;

		public LinkedListNode(T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
		{
			Value = value;
			IsValidParent = (LinkedListNode<T> parent, LinkedListNode<T> child)
				=> parentValidator(parent, child) && !(parent.Parent == child && child.Parent == parent);
		}

		public bool MatchesDomain(LinkedListNode<T> other) => ID == other.ID;
		public static bool DomainsAreMatching(LinkedListNode<T> a, LinkedListNode<T> b) => a.ID == b.ID;

		public void AssignHead(LinkedListNodeHead<T> head) => Head = head;

		public (LinkedListNodeHead<T> Node, int Dist) FindHead()
		{
			var dist = 0;
			var currNode = this;
			while (currNode.Parent != null && dist < 9000)
			{
				currNode = currNode.Parent;
				dist++;
			}

			if (dist >= 9000)
				throw new DataMisalignedException($"An infinite loop exists between {currNode.Value} and {currNode.Parent.Value}.");

			return (new LinkedListNodeHead<T>(currNode, currNode.Head.Blocked), dist); // We retain the blocked status of the head
		}

		public bool ValueEquals(LinkedListNode<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);
		public void AddChild(LinkedListNode<T> child) => Children.Add(child);
	}
}
