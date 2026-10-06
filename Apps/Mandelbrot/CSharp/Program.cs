// Mandelbrot: renders the Mandelbrot set into a binary PPM image, writes it to Mandelbrot.ppm and
// prints the image size and the FNV-1a hash of the file contents.
int size = args.Length > 0 ? int.Parse(args[0]) : 2000;
int maxIterations = args.Length > 1 ? int.Parse(args[1]) : 500;

byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n");
var image = new byte[header.Length + 3L * size * size];
header.CopyTo(image, 0);

long offset = header.Length;
for (int py = 0; py < size; py++)
{
    double ci = 3.0 * py / size - 1.5;
    for (int px = 0; px < size; px++)
    {
        double cr = 3.0 * px / size - 2.0;
        double zr = 0.0, zi = 0.0;
        int iteration = 0;
        while (iteration < maxIterations)
        {
            double zr2 = zr * zr;
            double zi2 = zi * zi;
            if (zr2 + zi2 > 4.0)
                break;
            zi = 2.0 * zr * zi + ci;
            zr = zr2 - zi2 + cr;
            iteration++;
        }
        if (iteration < maxIterations)
        {
            image[offset] = (byte)(iteration * 7);
            image[offset + 1] = (byte)(iteration * 13);
            image[offset + 2] = (byte)(iteration * 29);
        }
        offset += 3;
    }
}

File.WriteAllBytes("Mandelbrot.ppm", image);

ulong hash = 0xCBF29CE484222325;
foreach (byte b in image)
    hash = (hash ^ b) * 0x100000001B3;
Console.WriteLine($"{size}x{size} {hash:x16}");
