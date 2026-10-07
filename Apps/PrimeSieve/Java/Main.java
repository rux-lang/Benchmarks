// PrimeSieve: finds every prime up to n with the sieve of Eratosthenes over a byte array and prints
// how many there are and their sum.
public final class Main {
    public static void main(String[] args) {
        long limit = args.length > 0 ? Long.parseLong(args[0]) : 100000000;

        byte[] composite = new byte[Math.toIntExact(limit + 1)];
        for (long i = 2; i * i <= limit; i++) {
            if (composite[(int) i] != 0)
                continue;
            for (long j = i * i; j <= limit; j += i)
                composite[(int) j] = 1;
        }

        long count = 0;
        long sum = 0;
        for (int k = 2; k <= limit; k++) {
            if (composite[k] == 0) {
                count++;
                sum += k;
            }
        }
        System.out.print(count + " " + sum + "\n");
        System.out.flush();
    }
}
