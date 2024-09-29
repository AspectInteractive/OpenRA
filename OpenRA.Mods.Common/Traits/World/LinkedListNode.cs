using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static OpenRA.Mods.Common.Traits.ThetaPathfinderExecutionManager;

namespace OpenRA.Mods.Common.Traits
{
	public class LinkedListNode<T>
	{
		public LinkedListNode<T> Parent;
		public List<LinkedListNode<T>> Children = new();
		public Func<LinkedListNode<T>, LinkedListNode<T>, bool> IsValidParent;
		public LinkedListNode<T> Head = null;
		public bool? Blocked = null;

		// Invariant: We can safely cast to (bool) because if Head exists it must have a Blocked status,
		// otherwise the node itself must be the Head and therefore must have a blocked status.
		public bool IsBlocked => Head != null ? (bool)Head.Blocked : (bool)Blocked;
		public T ParentValIfExists => Parent != null ? Parent.Value : default;
		public int ID => Head != null ? Head.ID : OwnID;
		public int OwnID = -1;
		public T Value;

		public LinkedListNode(LinkedListNode<T> parent, LinkedListNode<T> head, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
		{
			Head = head;
			Parent = parent;
			parent.AddChild(this);
			Value = value;
			IsValidParent = parentValidator;
		}

		// If no head is specified then this must be the head, therefore blocked and ownID must be set to a value
		// We do not use constructor chaining because we want to force the above rule
		public LinkedListNode(LinkedListNode<T> parent, T value,
			Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator, int ownID, bool blocked)
			: this(value, parentValidator, ownID, blocked)
			=> Parent = parent;

		// We do not use constructor chaining because we want to force blocked and ownID to require a value
		public LinkedListNode(T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator, int ownID,
			bool blocked)
		{
			if (ownID != int.MaxValue)
				OwnID = ownID;
			Value = value;
			IsValidParent = parentValidator;
			Blocked = blocked;
		}

		public LinkedListNode<T> GetHead() => Head ?? this;

		public bool MatchesDomain(LinkedListNode<T> other) => ID == other.ID;
		public static bool DomainsAreMatching(LinkedListNode<T> a, LinkedListNode<T> b) => a.ID == b.ID;

		public void ActionOnEveryNodeToParent(Action<LinkedListNode<T>> action, bool inclusive = true, int maxIters = 9000)
		{
			var dist = 0;
			var currNode = this;

			// Skip first iteration if non-inclusive
			if (!inclusive)
			{
				currNode = currNode.Parent;
				dist++;
			}

			while (currNode.Parent != null && dist < maxIters)
			{
				action(currNode);
				currNode = currNode.Parent;
				dist++;
			}

			if (dist >= maxIters)
				throw new DataMisalignedException($"An infinite loop exists between {currNode.Value} and {currNode.Parent.Value}.");
		}

		public (LinkedListNode<T> Node, int Dist) FindHead(bool blocked, int maxIters = 9000)
		{
			var dist = 0;
			var currNode = this;
			while (currNode.Parent != null && dist < maxIters)
			{
				currNode = currNode.Parent;
				dist++;
			}

			if (dist >= maxIters)
				throw new DataMisalignedException($"An infinite loop exists between {currNode.Value} and {currNode.Parent.Value}.");

			currNode.Head = null;
			currNode.Parent = null; // may be redundant but just in case
			currNode.Blocked = blocked;

			return (currNode, dist); // We retain the blocked status of the head
		}

		public bool ValueEquals(LinkedListNode<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);
		public void AddChild(LinkedListNode<T> child) => Children.Add(child);
	}
}
