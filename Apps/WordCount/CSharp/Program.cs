// WordCount: generates a pseudo-random text from a pseudo-random vocabulary, counts how often each
// word occurs with a hash map, and prints the 10 most frequent words.
using System.Runtime.InteropServices;

long wordCount = args.Length > 0 ? long.Parse(args[0]) : 10000000;
long vocabularySize = args.Length > 1 ? long.Parse(args[1]) : 100000;

ulong state = 2;

// Vocabulary: words of 2..12 letters, stored back to back.
var vocabulary = new List<char>();
var starts = new long[vocabularySize + 1];
for (long k = 0; k < vocabularySize; k++)
{
    starts[k] = vocabulary.Count;
    ulong length = 2 + SplitMix(ref state) % 11;
    for (ulong i = 0; i < length; i++)
        vocabulary.Add((char)('a' + (int)(SplitMix(ref state) % 26)));
}
starts[vocabularySize] = vocabulary.Count;

// Text: the generator runs twice, first to measure the text and then to write it.
ulong textState = state;
long textLength = 0;
for (long i = 0; i < wordCount; i++)
{
    long word = PickWord(ref state, vocabularySize);
    textLength += starts[word + 1] - starts[word] + 1;
}
state = textState;
var text = new char[textLength];
long position = 0;
for (long i = 0; i < wordCount; i++)
{
    long word = PickWord(ref state, vocabularySize);
    for (long j = starts[word]; j < starts[word + 1]; j++)
        text[position++] = vocabulary[(int)j];
    text[position++] = (i + 1) % 16 == 0 ? '\n' : ' ';
}

// Count words: maximal runs of the letters a..z.
var counts = new Dictionary<string, int>();
var lookup = counts.GetAlternateLookup<ReadOnlySpan<char>>();
long total = 0;
long start = 0;
for (long i = 0; i <= textLength; i++)
{
    if (i < textLength && text[i] >= 'a' && text[i] <= 'z')
        continue;
    if (i > start)
    {
        ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, text.AsSpan((int)start, (int)(i - start)), out _);
        count++;
        total++;
    }
    start = i + 1;
}

// Top 10 by count (descending), then by word (ascending).
var topWords = new string[10];
var topCounts = new int[10];
int filled = 0;
foreach (var (word, count) in counts)
{
    int slot = filled;
    while (slot > 0 && Before(word, count, topWords[slot - 1], topCounts[slot - 1]))
        slot--;
    if (slot >= 10)
        continue;
    int last = filled < 10 ? filled : 9;
    for (int s = last; s > slot; s--)
    {
        topWords[s] = topWords[s - 1];
        topCounts[s] = topCounts[s - 1];
    }
    topWords[slot] = word;
    topCounts[slot] = count;
    if (filled < 10)
        filled++;
}

var output = new System.Text.StringBuilder();
output.Append($"words {total} distinct {counts.Count}\n");
for (int s = 0; s < filled; s++)
    output.Append($"{topCounts[s]} {topWords[s]}\n");
Console.Write(output.ToString());

static bool Before(string word, int count, string otherWord, int otherCount) =>
    count != otherCount ? count > otherCount : string.CompareOrdinal(word, otherWord) < 0;

static long PickWord(ref ulong state, long vocabularySize)
{
    ulong a = SplitMix(ref state) % (ulong)vocabularySize;
    ulong b = SplitMix(ref state) % (ulong)vocabularySize;
    return (long)(a < b ? a : b);
}

static ulong SplitMix(ref ulong state)
{
    state += 0x9E3779B97F4A7C15;
    ulong z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
    return z ^ (z >> 31);
}
