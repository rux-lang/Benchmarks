// NBody: simulates the Sun and the four gas giants (Computer Language Benchmarks Game model) and
// prints the total energy before and after as raw IEEE-754 bit patterns.
public final class Main {
    static final double PI = 3.141592653589793;
    static final double SOLAR_MASS = 4.0 * PI * PI;
    static final double DAYS_PER_YEAR = 365.24;
    static final int BODIES = 5;

    static final double[] x = {0.0, 4.84143144246472090e+00, 8.34336671824457987e+00, 1.28943695621391310e+01, 1.53796971148509165e+01};
    static final double[] y = {0.0, -1.16032004402742839e+00, 4.12479856412430479e+00, -1.51111514016986312e+01, -2.59193146099879641e+01};
    static final double[] z = {0.0, -1.03622044471123109e-01, -4.03523417114321381e-01, -2.23307578892655734e-01, 1.79258772950371181e-01};
    static final double[] vx = {0.0, 1.66007664274403694e-03 * DAYS_PER_YEAR, -2.76742510726862411e-03 * DAYS_PER_YEAR, 2.96460137564761618e-03 * DAYS_PER_YEAR, 2.68067772490389322e-03 * DAYS_PER_YEAR};
    static final double[] vy = {0.0, 7.69901118419740425e-03 * DAYS_PER_YEAR, 4.99852801234917238e-03 * DAYS_PER_YEAR, 2.37847173959480950e-03 * DAYS_PER_YEAR, 1.62824170038242295e-03 * DAYS_PER_YEAR};
    static final double[] vz = {0.0, -6.90460016972063023e-05 * DAYS_PER_YEAR, 2.30417297573763929e-05 * DAYS_PER_YEAR, -2.96589568540237556e-05 * DAYS_PER_YEAR, -9.51592254519715870e-05 * DAYS_PER_YEAR};
    static final double[] mass = {SOLAR_MASS, 9.54791938424326609e-04 * SOLAR_MASS, 2.85885980666130812e-04 * SOLAR_MASS, 4.36624404335156298e-05 * SOLAR_MASS, 5.15138902046611451e-05 * SOLAR_MASS};

    public static void main(String[] args) {
        long steps = args.length > 0 ? Long.parseLong(args[0]) : 5000000;

        // Offset the Sun's momentum so the system's total momentum is zero.
        double px = 0.0, py = 0.0, pz = 0.0;
        for (int i = 0; i < BODIES; i++) {
            px += vx[i] * mass[i];
            py += vy[i] * mass[i];
            pz += vz[i] * mass[i];
        }
        vx[0] = -px / SOLAR_MASS;
        vy[0] = -py / SOLAR_MASS;
        vz[0] = -pz / SOLAR_MASS;

        double before = energy();
        for (long step = 0; step < steps; step++)
            advance(0.01);
        double after = energy();
        System.out.print(hex16(Double.doubleToRawLongBits(before)) + " "
            + hex16(Double.doubleToRawLongBits(after)) + "\n");
        System.out.flush();
    }

    static void advance(double dt) {
        for (int i = 0; i < BODIES; i++) {
            for (int j = i + 1; j < BODIES; j++) {
                double dx = x[i] - x[j];
                double dy = y[i] - y[j];
                double dz = z[i] - z[j];
                double distance2 = dx * dx + dy * dy + dz * dz;
                double magnitude = dt / (distance2 * Math.sqrt(distance2));
                vx[i] -= dx * mass[j] * magnitude;
                vy[i] -= dy * mass[j] * magnitude;
                vz[i] -= dz * mass[j] * magnitude;
                vx[j] += dx * mass[i] * magnitude;
                vy[j] += dy * mass[i] * magnitude;
                vz[j] += dz * mass[i] * magnitude;
            }
        }
        for (int i = 0; i < BODIES; i++) {
            x[i] += dt * vx[i];
            y[i] += dt * vy[i];
            z[i] += dt * vz[i];
        }
    }

    static double energy() {
        double energy = 0.0;
        for (int i = 0; i < BODIES; i++) {
            energy += 0.5 * mass[i] * (vx[i] * vx[i] + vy[i] * vy[i] + vz[i] * vz[i]);
            for (int j = i + 1; j < BODIES; j++) {
                double dx = x[i] - x[j];
                double dy = y[i] - y[j];
                double dz = z[i] - z[j];
                energy -= mass[i] * mass[j] / Math.sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
        return energy;
    }

    // Lower-case hex, zero-padded to 16 digits.
    static String hex16(long value) {
        String digits = Long.toHexString(value);
        return "0".repeat(16 - digits.length()) + digits;
    }
}
