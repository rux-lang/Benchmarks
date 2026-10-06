// MatrixMultiply: multiplies two generated n×n matrices of doubles with the cache-friendly i-k-j
// loop order and prints a hash of the result's bit patterns.
int n = args.Length > 0 ? int.Parse(args[0]) : 1024;

var a = new double[n * n];
var b = new double[n * n];
var c = new double[n * n];
for (int i = 0; i < n; i++)
{
    for (int j = 0; j < n; j++)
    {
        a[i * n + j] = ((i + 3 * j) % 17 - 8) / 8.0;
        b[i * n + j] = ((2 * i + j) % 13 - 6) / 6.0;
    }
}

for (int i = 0; i < n; i++)
{
    for (int k = 0; k < n; k++)
    {
        double aik = a[i * n + k];
        for (int j = 0; j < n; j++)
            c[i * n + j] += aik * b[k * n + j];
    }
}

ulong hash = 0xCBF29CE484222325;
foreach (double value in c)
    hash = (hash ^ BitConverter.DoubleToUInt64Bits(value)) * 0x100000001B3;
Console.WriteLine($"{n} {hash:x16}");
