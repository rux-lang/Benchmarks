// PrimeSieve: finds every prime up to n with the sieve of Eratosthenes over a byte array and prints
// how many there are and their sum.
long limit = args.Length > 0 ? long.Parse(args[0]) : 100000000;

var composite = new byte[limit + 1];
for (long i = 2; i * i <= limit; i++)
{
    if (composite[i] != 0)
        continue;
    for (long j = i * i; j <= limit; j += i)
        composite[j] = 1;
}

long count = 0;
long sum = 0;
for (long k = 2; k <= limit; k++)
{
    if (composite[k] == 0)
    {
        count++;
        sum += k;
    }
}
Console.WriteLine($"{count} {sum}");
