// WordCount: generates a pseudo-random text from a pseudo-random vocabulary, counts how often each
// word occurs with a hash map, and prints the 10 most frequent words.
import java.util.HashMap;
import java.util.Map;

public final class Main {
    static long state;

    public static void main(String[] args) {
        long wordCount = args.length > 0 ? Long.parseLong(args[0]) : 10000000;
        long vocabularySize = args.length > 1 ? Long.parseLong(args[1]) : 100000;

        state = 2;

        // Vocabulary: words of 2..12 letters, stored back to back.
        StringBuilder vocabulary = new StringBuilder();
        int[] starts = new int[Math.toIntExact(vocabularySize + 1)];
        for (int k = 0; k < vocabularySize; k++) {
            starts[k] = vocabulary.length();
            long length = 2 + Long.remainderUnsigned(splitMix(), 11);
            for (long i = 0; i < length; i++)
                vocabulary.append((char) ('a' + (int) Long.remainderUnsigned(splitMix(), 26)));
        }
        starts[(int) vocabularySize] = vocabulary.length();

        // Text: the generator runs twice, first to measure the text and then to write it.
        long textState = state;
        long textLength = 0;
        for (long i = 0; i < wordCount; i++) {
            int word = pickWord(vocabularySize);
            textLength += starts[word + 1] - starts[word] + 1;
        }
        state = textState;
        char[] text = new char[Math.toIntExact(textLength)];
        int position = 0;
        for (long i = 0; i < wordCount; i++) {
            int word = pickWord(vocabularySize);
            for (int j = starts[word]; j < starts[word + 1]; j++)
                text[position++] = vocabulary.charAt(j);
            text[position++] = (i + 1) % 16 == 0 ? '\n' : ' ';
        }

        // Count words: maximal runs of the letters a..z.
        HashMap<String, Integer> counts = new HashMap<>();
        long total = 0;
        int start = 0;
        for (int i = 0; i <= text.length; i++) {
            if (i < text.length && text[i] >= 'a' && text[i] <= 'z')
                continue;
            if (i > start) {
                counts.merge(new String(text, start, i - start), 1, Integer::sum);
                total++;
            }
            start = i + 1;
        }

        // Top 10 by count (descending), then by word (ascending).
        String[] topWords = new String[10];
        int[] topCounts = new int[10];
        int filled = 0;
        for (Map.Entry<String, Integer> entry : counts.entrySet()) {
            String word = entry.getKey();
            int count = entry.getValue();
            int slot = filled;
            while (slot > 0 && before(word, count, topWords[slot - 1], topCounts[slot - 1]))
                slot--;
            if (slot >= 10)
                continue;
            int last = filled < 10 ? filled : 9;
            for (int s = last; s > slot; s--) {
                topWords[s] = topWords[s - 1];
                topCounts[s] = topCounts[s - 1];
            }
            topWords[slot] = word;
            topCounts[slot] = count;
            if (filled < 10)
                filled++;
        }

        StringBuilder output = new StringBuilder();
        output.append("words ").append(total).append(" distinct ").append(counts.size()).append('\n');
        for (int s = 0; s < filled; s++)
            output.append(topCounts[s]).append(' ').append(topWords[s]).append('\n');
        System.out.print(output);
        System.out.flush();
    }

    // String.compareTo compares UTF-16 code units, the same as string.CompareOrdinal in C#.
    static boolean before(String word, int count, String otherWord, int otherCount) {
        return count != otherCount ? count > otherCount : word.compareTo(otherWord) < 0;
    }

    static int pickWord(long vocabularySize) {
        long a = Long.remainderUnsigned(splitMix(), vocabularySize);
        long b = Long.remainderUnsigned(splitMix(), vocabularySize);
        return (int) (Long.compareUnsigned(a, b) < 0 ? a : b);
    }

    static long splitMix() {
        state += 0x9E3779B97F4A7C15L;
        long z = state;
        z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
        z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
        return z ^ (z >>> 31);
    }
}
