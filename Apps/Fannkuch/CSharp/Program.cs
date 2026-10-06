// Fannkuch: for every permutation of 0..n-1, counts how many prefix reversals ("pancake flips")
// it takes until 0 comes first (single-threaded fannkuch-redux from the Benchmarks Game).
int n = args.Length > 0 ? int.Parse(args[0]) : 10;

var permutation = new int[n];
var current = new int[n];
var counters = new int[n];
for (int i = 0; i < n; i++)
    current[i] = i;

int maxFlips = 0;
long checksum = 0;
long permutationIndex = 0;
int r = n;
while (true)
{
    while (r != 1)
    {
        counters[r - 1] = r;
        r--;
    }

    for (int i = 0; i < n; i++)
        permutation[i] = current[i];
    int flips = 0;
    int first = permutation[0];
    while (first != 0)
    {
        for (int low = 0, high = first; low < high; low++, high--)
            (permutation[low], permutation[high]) = (permutation[high], permutation[low]);
        flips++;
        first = permutation[0];
    }
    if (flips > maxFlips)
        maxFlips = flips;
    checksum += permutationIndex % 2 == 0 ? flips : -flips;

    // Next permutation in the order used by the reference program.
    while (true)
    {
        if (r == n)
        {
            Console.Write($"{checksum}\nPfannkuchen({n}) = {maxFlips}\n");
            return;
        }
        int rotated = current[0];
        for (int i = 0; i < r; i++)
            current[i] = current[i + 1];
        current[r] = rotated;
        counters[r]--;
        if (counters[r] > 0)
            break;
        r++;
    }
    permutationIndex++;
}
