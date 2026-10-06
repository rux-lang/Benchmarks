// Base64: encodes pseudo-random bytes as Base64 and decodes them again with a hand-written codec,
// checks the round trip, and prints the encoded length and a hash of the encoded text.
long mebibytes = args.Length > 0 ? long.Parse(args[0]) : 32;
long rounds = args.Length > 1 ? long.Parse(args[1]) : 4;

const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

long length = mebibytes * 1048576;
var data = new byte[length];
ulong state = 4;
for (long i = 0; i < length; i += 8)
{
    ulong value = SplitMix(ref state);
    for (int b = 0; b < 8; b++)
        data[i + b] = (byte)(value >> (8 * b));
}

var decodeTable = new int[256];
Array.Fill(decodeTable, -1);
for (int i = 0; i < 64; i++)
    decodeTable[Alphabet[i]] = i;

long encodedLength = (length + 2) / 3 * 4;
var encoded = new byte[encodedLength];
var decoded = new byte[length];
for (long round = 0; round < rounds; round++)
{
    Encode(data, encoded);
    if (Decode(encoded, decoded, decodeTable) != length)
        return Fail();
    for (long i = 0; i < length; i++)
        if (decoded[i] != data[i])
            return Fail();
}

ulong hash = 0xCBF29CE484222325;
foreach (byte b in encoded)
    hash = (hash ^ b) * 0x100000001B3;
Console.WriteLine($"{encodedLength} {hash:x16}");
return 0;

static int Fail()
{
    Console.WriteLine("round trip failed");
    return 1;
}

static void Encode(byte[] input, byte[] output)
{
    long full = input.Length / 3 * 3;
    long o = 0;
    for (long i = 0; i < full; i += 3)
    {
        int triple = (input[i] << 16) | (input[i + 1] << 8) | input[i + 2];
        output[o] = (byte)Alphabet[(triple >> 18) & 63];
        output[o + 1] = (byte)Alphabet[(triple >> 12) & 63];
        output[o + 2] = (byte)Alphabet[(triple >> 6) & 63];
        output[o + 3] = (byte)Alphabet[triple & 63];
        o += 4;
    }
    long rest = input.Length - full;
    if (rest == 1)
    {
        int triple = input[full] << 16;
        output[o] = (byte)Alphabet[(triple >> 18) & 63];
        output[o + 1] = (byte)Alphabet[(triple >> 12) & 63];
        output[o + 2] = (byte)'=';
        output[o + 3] = (byte)'=';
    }
    else if (rest == 2)
    {
        int triple = (input[full] << 16) | (input[full + 1] << 8);
        output[o] = (byte)Alphabet[(triple >> 18) & 63];
        output[o + 1] = (byte)Alphabet[(triple >> 12) & 63];
        output[o + 2] = (byte)Alphabet[(triple >> 6) & 63];
        output[o + 3] = (byte)'=';
    }
}

// Returns the number of decoded bytes, or -1 for invalid input.
static long Decode(byte[] input, byte[] output, int[] table)
{
    long o = 0;
    for (long i = 0; i < input.Length; i += 4)
    {
        int a = table[input[i]];
        int b = table[input[i + 1]];
        if (a < 0 || b < 0)
            return -1;
        output[o++] = (byte)((a << 2) | (b >> 4));
        if (input[i + 2] == '=')
            break;
        int c = table[input[i + 2]];
        if (c < 0)
            return -1;
        output[o++] = (byte)(((b & 15) << 4) | (c >> 2));
        if (input[i + 3] == '=')
            break;
        int d = table[input[i + 3]];
        if (d < 0)
            return -1;
        output[o++] = (byte)(((c & 3) << 6) | d);
    }
    return o;
}

static ulong SplitMix(ref ulong state)
{
    state += 0x9E3779B97F4A7C15;
    ulong z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
    return z ^ (z >> 31);
}
