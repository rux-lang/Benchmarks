// Sort: sorts pseudo-random 32-bit integers with a hand-written quicksort, checks the order and
// prints a hash of the sorted array.
long count = args.Length > 0 ? long.Parse(args[0]) : 10000000;

ulong state = 3;
var values = new int[count];
for (long i = 0; i < count; i++)
    values[i] = (int)(uint)(SplitMix(ref state) >> 32);

QuickSort(values, 0, count - 1);

ulong hash = 0xCBF29CE484222325;
for (long i = 0; i < count; i++)
{
    if (i > 0 && values[i - 1] > values[i])
    {
        Console.WriteLine("unsorted");
        return 1;
    }
    hash = (hash ^ (uint)values[i]) * 0x100000001B3;
}
Console.WriteLine($"{count} {hash:x16}");
return 0;

// Sorts values[low..=high]: median-of-three quicksort that recurses into the smaller part and
// leaves ranges of fewer than 16 elements to insertion sort.
static void QuickSort(int[] values, long low, long high)
{
    while (high - low >= 16)
    {
        long middle = low + (high - low) / 2;
        if (values[middle] < values[low]) Swap(values, low, middle);
        if (values[high] < values[low]) Swap(values, low, high);
        if (values[high] < values[middle]) Swap(values, middle, high);
        int pivot = values[middle];
        long i = low, j = high;
        while (i <= j)
        {
            while (values[i] < pivot) i++;
            while (values[j] > pivot) j--;
            if (i <= j)
            {
                Swap(values, i, j);
                i++;
                j--;
            }
        }
        if (j - low < high - i)
        {
            QuickSort(values, low, j);
            low = i;
        }
        else
        {
            QuickSort(values, i, high);
            high = j;
        }
    }
    for (long i = low + 1; i <= high; i++)
    {
        int value = values[i];
        long j = i - 1;
        while (j >= low && values[j] > value)
        {
            values[j + 1] = values[j];
            j--;
        }
        values[j + 1] = value;
    }
}

static void Swap(int[] values, long a, long b) => (values[a], values[b]) = (values[b], values[a]);

static ulong SplitMix(ref ulong state)
{
    state += 0x9E3779B97F4A7C15;
    ulong z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
    return z ^ (z >> 31);
}
