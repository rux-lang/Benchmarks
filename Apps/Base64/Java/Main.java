// Base64: encodes pseudo-random bytes as Base64 and decodes them again with a hand-written codec,
// checks the round trip, and prints the encoded length and a hash of the encoded text.
import java.util.Arrays;

public final class Main {
    static final String ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    static long state;

    public static void main(String[] args) {
        long mebibytes = args.length > 0 ? Long.parseLong(args[0]) : 32;
        long rounds = args.length > 1 ? Long.parseLong(args[1]) : 4;

        int length = Math.toIntExact(mebibytes * 1048576);
        byte[] data = new byte[length];
        state = 4;
        for (int i = 0; i < length; i += 8) {
            long value = splitMix();
            for (int b = 0; b < 8; b++)
                data[i + b] = (byte) (value >>> (8 * b));
        }

        int[] decodeTable = new int[256];
        Arrays.fill(decodeTable, -1);
        for (int i = 0; i < 64; i++)
            decodeTable[ALPHABET.charAt(i)] = i;

        int encodedLength = Math.toIntExact((length + 2L) / 3 * 4);
        byte[] encoded = new byte[encodedLength];
        byte[] decoded = new byte[length];
        for (long round = 0; round < rounds; round++) {
            encode(data, encoded);
            if (decode(encoded, decoded, decodeTable) != length)
                fail();
            for (int i = 0; i < length; i++)
                if (decoded[i] != data[i])
                    fail();
        }

        long hash = 0xCBF29CE484222325L;
        for (byte b : encoded)
            hash = (hash ^ (b & 0xFF)) * 0x100000001B3L;
        System.out.print(encodedLength + " " + hex16(hash) + "\n");
        System.out.flush();
    }

    static void fail() {
        System.out.print("round trip failed\n");
        System.out.flush();
        System.exit(1);
    }

    static void encode(byte[] input, byte[] output) {
        int full = input.length / 3 * 3;
        int o = 0;
        for (int i = 0; i < full; i += 3) {
            int triple = ((input[i] & 0xFF) << 16) | ((input[i + 1] & 0xFF) << 8) | (input[i + 2] & 0xFF);
            output[o] = (byte) ALPHABET.charAt((triple >> 18) & 63);
            output[o + 1] = (byte) ALPHABET.charAt((triple >> 12) & 63);
            output[o + 2] = (byte) ALPHABET.charAt((triple >> 6) & 63);
            output[o + 3] = (byte) ALPHABET.charAt(triple & 63);
            o += 4;
        }
        int rest = input.length - full;
        if (rest == 1) {
            int triple = (input[full] & 0xFF) << 16;
            output[o] = (byte) ALPHABET.charAt((triple >> 18) & 63);
            output[o + 1] = (byte) ALPHABET.charAt((triple >> 12) & 63);
            output[o + 2] = (byte) '=';
            output[o + 3] = (byte) '=';
        } else if (rest == 2) {
            int triple = ((input[full] & 0xFF) << 16) | ((input[full + 1] & 0xFF) << 8);
            output[o] = (byte) ALPHABET.charAt((triple >> 18) & 63);
            output[o + 1] = (byte) ALPHABET.charAt((triple >> 12) & 63);
            output[o + 2] = (byte) ALPHABET.charAt((triple >> 6) & 63);
            output[o + 3] = (byte) '=';
        }
    }

    // Returns the number of decoded bytes, or -1 for invalid input.
    static int decode(byte[] input, byte[] output, int[] table) {
        int o = 0;
        for (int i = 0; i < input.length; i += 4) {
            int a = table[input[i] & 0xFF];
            int b = table[input[i + 1] & 0xFF];
            if (a < 0 || b < 0)
                return -1;
            output[o++] = (byte) ((a << 2) | (b >> 4));
            if (input[i + 2] == '=')
                break;
            int c = table[input[i + 2] & 0xFF];
            if (c < 0)
                return -1;
            output[o++] = (byte) (((b & 15) << 4) | (c >> 2));
            if (input[i + 3] == '=')
                break;
            int d = table[input[i + 3] & 0xFF];
            if (d < 0)
                return -1;
            output[o++] = (byte) (((c & 3) << 6) | d);
        }
        return o;
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
