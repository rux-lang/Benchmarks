// Sort: sorts pseudo-random 32-bit integers with a hand-written quicksort, checks the order and
// prints a hash of the sorted array.
public final class Main {
    static long state;

    public static void main(String[] args) {
        int count = args.length > 0 ? Math.toIntExact(Long.parseLong(args[0])) : 10000000;

        state = 3;
        int[] values = new int[count];
        for (int i = 0; i < count; i++)
            values[i] = (int) (splitMix() >>> 32);

        quickSort(values, 0, count - 1);

        long hash = 0xCBF29CE484222325L;
        for (int i = 0; i < count; i++) {
            if (i > 0 && values[i - 1] > values[i]) {
                System.out.print("unsorted\n");
                System.out.flush();
                System.exit(1);
            }
            hash = (hash ^ (values[i] & 0xFFFFFFFFL)) * 0x100000001B3L;
        }
        System.out.print(count + " " + hex16(hash) + "\n");
        System.out.flush();
    }

    // Sorts values[low..=high]: median-of-three quicksort that recurses into the smaller part and
    // leaves ranges of fewer than 16 elements to insertion sort.
    static void quickSort(int[] values, int low, int high) {
        while (high - low >= 16) {
            int middle = low + (high - low) / 2;
            if (values[middle] < values[low]) swap(values, low, middle);
            if (values[high] < values[low]) swap(values, low, high);
            if (values[high] < values[middle]) swap(values, middle, high);
            int pivot = values[middle];
            int i = low, j = high;
            while (i <= j) {
                while (values[i] < pivot) i++;
                while (values[j] > pivot) j--;
                if (i <= j) {
                    swap(values, i, j);
                    i++;
                    j--;
                }
            }
            if (j - low < high - i) {
                quickSort(values, low, j);
                low = i;
            } else {
                quickSort(values, i, high);
                high = j;
            }
        }
        for (int i = low + 1; i <= high; i++) {
            int value = values[i];
            int j = i - 1;
            while (j >= low && values[j] > value) {
                values[j + 1] = values[j];
                j--;
            }
            values[j + 1] = value;
        }
    }

    static void swap(int[] values, int a, int b) {
        int temporary = values[a];
        values[a] = values[b];
        values[b] = temporary;
    }

    static long splitMix() {
        state += 0x9E3779B97F4A7C15L;
        long z = state;
        z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
        z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
        return z ^ (z >>> 31);
    }

    // Lower-case hex, zero-padded to 16 digits.
    static String hex16(long value) {
        String digits = Long.toHexString(value);
        return "0".repeat(16 - digits.length()) + digits;
    }
}
