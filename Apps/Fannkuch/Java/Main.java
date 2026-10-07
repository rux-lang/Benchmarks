// Fannkuch: for every permutation of 0..n-1, counts how many prefix reversals ("pancake flips")
// it takes until 0 comes first (single-threaded fannkuch-redux from the Benchmarks Game).
public final class Main {
    public static void main(String[] args) {
        int n = args.length > 0 ? Integer.parseInt(args[0]) : 10;

        int[] permutation = new int[n];
        int[] current = new int[n];
        int[] counters = new int[n];
        for (int i = 0; i < n; i++)
            current[i] = i;

        int maxFlips = 0;
        long checksum = 0;
        long permutationIndex = 0;
        int r = n;
        while (true) {
            while (r != 1) {
                counters[r - 1] = r;
                r--;
            }

            for (int i = 0; i < n; i++)
                permutation[i] = current[i];
            int flips = 0;
            int first = permutation[0];
            while (first != 0) {
                for (int low = 0, high = first; low < high; low++, high--) {
                    int temporary = permutation[low];
                    permutation[low] = permutation[high];
                    permutation[high] = temporary;
                }
                flips++;
                first = permutation[0];
            }
            if (flips > maxFlips)
                maxFlips = flips;
            checksum += permutationIndex % 2 == 0 ? flips : -flips;

            // Next permutation in the order used by the reference program.
            while (true) {
                if (r == n) {
                    System.out.print(checksum + "\nPfannkuchen(" + n + ") = " + maxFlips + "\n");
                    System.out.flush();
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
    }
}
