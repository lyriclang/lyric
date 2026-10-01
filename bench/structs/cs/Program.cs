// Measurement point 2, 'structs': the C# twin of structs.lyr — an array of structs, in place.
const int count = 100000, steps = 2000;
const double dt = 0.01;
var ps = new Particle[count];
for (int i = 0; i < count; i++) ps[i] = new Particle { x = i, y = i * 2.0, vx = 1.0, vy = -0.5 };
for (int s = 0; s < steps; s++)
{
    for (int i = 0; i < count; i++)
    {
        ps[i].x = ps[i].x + ps[i].vx * dt;
        ps[i].y = ps[i].y + ps[i].vy * dt;
        ps[i].vx = ps[i].vx - ps[i].y * 0.001 * dt;
        ps[i].vy = ps[i].vy + ps[i].x * 0.001 * dt;
    }
}
double sum = 0.0;
for (int i = 0; i < count; i++) sum = sum + ps[i].x + ps[i].y;
System.Console.WriteLine((long)sum);

struct Particle { public double x, y, vx, vy; }
