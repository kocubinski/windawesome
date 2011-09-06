using System.Collections.Generic;

namespace Windawesome
{
	public sealed class HashMultiSet<T> : IEnumerable<T>
	{
		private readonly Dictionary<T, BoxedInt> set;
		private sealed class BoxedInt
		{
			public int i = 1;
		}

		public HashMultiSet(IEqualityComparer<T> comparer = null)
		{
			set = new Dictionary<T, BoxedInt>(comparer);
		}

		public AddResult Add(T item)
		{
			BoxedInt count;
			if (set.TryGetValue(item, out count))
			{
				count.i++;
				return AddResult.Added;
			}
			else
			{
				set[item] = new BoxedInt();
				return AddResult.AddedFirst;
			}
		}

		public AddResult AddUnique(T item)
		{
			if (set.ContainsKey(item))
			{
				return AddResult.AlreadyContained;
			}
			else
			{
				set[item] = new BoxedInt();
				return AddResult.AddedFirst;
			}
		}

		public RemoveResult Remove(T item)
		{
			BoxedInt count;
			if (set.TryGetValue(item, out count))
			{
				if (count.i == 1)
				{
					set.Remove(item);
					return RemoveResult.RemovedLast;
				}
				else
				{
					count.i--;
					return RemoveResult.Removed;
				}
			}

			return RemoveResult.NotFound;
		}

		public bool Contains(T item)
		{
			return set.ContainsKey(item);
		}

		public enum AddResult : byte
		{
			AddedFirst,
			Added,
			AlreadyContained
		}

		public enum RemoveResult : byte
		{
			NotFound,
			RemovedLast,
			Removed
		}

		public void Clear()
		{
			set.Clear();
		}

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			return set.Keys.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return set.Keys.GetEnumerator();
		}

		#endregion
	}
}
