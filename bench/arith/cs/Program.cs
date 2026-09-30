// Measurement point 2, 'arith': the C# twin of arith.lyr.
const int width = 2000, height = 2000, limit = 200;
long inside = 0;
for (int py = 0; py < height; py++)
{
    for (int px = 0; px < width; px++)
    {
        double cx = -2.0 + px * (3.0 / width);
        double cy = -1.5 + py * (3.0 / height);
        double x = 0.0, y = 0.0;
        int i = 0;
        while (i < limit && x * x + y * y <= 4.0)
        {
            double xt = x * x - y * y + cx;
            y = 2.0 * x * y + cy;
            x = xt;
            i++;
        }
        if (i == limit) inside++;
    }
}
System.Console.WriteLine(inside);
