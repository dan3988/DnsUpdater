namespace DnsUpdater;

internal struct ArrayBuilder<T>
{
	private const int DefaultCapacity = 4;

	private T[]? _array;
	private int _count;

	public readonly T[] Value => _array ?? Array.Empty<T>();

	public readonly int Count => _count;

	public ArrayBuilder(int capacity)
	{
		_array = capacity == 0 ? null : new T[capacity];
	}

	public void Add(T item)
	{
		if (_count == 0)
		{
			_array = new T[DefaultCapacity];
		}
		else if (_count == _array!.Length)
		{
			Array.Resize(ref _array, _count << 1);
		}

		_array[_count++] = item;
	}

	public T[] ToArray()
	{
		if (_count == 0)
			return Array.Empty<T>();

		Trim();
		return _array!;
	}

	public void Trim()
		=> Array.Resize(ref _array, _count);
}
