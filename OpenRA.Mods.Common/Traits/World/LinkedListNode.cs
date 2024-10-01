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
		public T Value;

		public LinkedListNode(LinkedListNode<T> parent, T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
		{
			Parent = parent;
			Value = value;
			IsValidParent = parentValidator;
		}

		public LinkedListNode(T value, Func<LinkedListNode<T>, LinkedListNode<T>, bool> parentValidator)
		{
			Value = value;
			IsValidParent = parentValidator;
		}

		public bool ValueEquals(LinkedListNode<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

		public void AddChild(LinkedListNode<T> child) => Children.Add(child);
		public void AddChildren(List<LinkedListNode<T>> children) => Children.AddRange(children);
		public void SetChildren(List<LinkedListNode<T>> children) => Children = children;
		public void SetParent(LinkedListNode<T> parent) => Parent = parent;
		public void SetValue(T value) => Value = value;

		// Removes the parent and sets new parents for the children
		public LinkedListNode<T> RemoveParentAndReturnNewParent(List<LinkedListNode<T>> neighboursOfChildren)
		{
			Parent = null;
			if (Children.Count == 0)
				return null;

			//var test = Children.Select(c1 => (c1, Children.Where(cx => !cx.ValueEquals(c1) && IsValidParent(c1, cx)).ToList()));

			// For each child, we run IsValidParent from that child to all other children to identify if they are
			// a valid parent and only keep them if they are valid. Then we sort the original list of children
			// by the highest number first (most valid children), to get the best candidates for parents.
			var bestCandidateParentWithChildren
				= Children.Select(c1 => (c1, Children.Where(cx => !cx.ValueEquals(c1) && IsValidParent(c1, cx)).ToList()))
					.OrderByDescending(c => c.Item2.Count).FirstOrDefault();

			return bestCandidateParentWithChildren.c1;
		}
	}
}
