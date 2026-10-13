using System.Text;

namespace Runner.Generator;

/// <summary>
/// src/Main.java, src/common/ (Rng and Common), and src/groupN/NameNNN.java: one final class per
/// module, its types nested inside. Java has no unsigned 64-bit type, so the code uses long with
/// &gt;&gt;&gt; and Long.remainderUnsigned, as the apps do.
/// </summary>
sealed class JavaEmitter : Emitter
{
    public override string Folder => "Java";

    protected override string Literal(ulong value) => Number(value) + "L";
    protected override string Rotl(string value, int amount) => $"Common.rotl({value}, {amount})";
    protected override string ShiftRight => ">>>";
    protected override bool ModIsCall => true;
    protected override string Mod(string value, ulong divisor) => $"Long.remainderUnsigned({value}, {Literal(divisor)})";
    protected override string Declare(Declare d) => $"long {d.Name} = {Expression(d.Value)};";
    protected override string LoopHead(Loop loop, bool variableUsed) =>
        $"for (long {loop.Variable} = 0; {loop.Variable} < {loop.Count}; {loop.Variable}++)";

    /// <summary>How code in one module names another module's class: nested in its file's class when the file holds several.</summary>
    static string ClassName(ModuleSpec from, ModuleSpec to) => to.SharesFile && to.File != from.File ? $"{to.File}.{to.Name}" : to.Name;

    /// <summary>The top-level class a module needs to import to reach another group's module.</summary>
    static string? Import(ModuleSpec from, ModuleSpec to) => to.Group == from.Group ? null : $"group{to.Group}.{to.File}";

    public override string ModuleFile(ModuleSpec m) => $"src/group{m.Group}/{m.File}.java";

    public override IEnumerable<SourceFile> Emit(ProjectSpec project)
    {
        yield return new SourceFile("src/common/Rng.java", $$"""
            // Rng: the SplitMix64 generator every module uses.
            // {{Banner}}
            package common;

            public final class Rng {
                private long state;

                public Rng(long seed) {
                    state = seed;
                }

                public long next() {
                    state += 0x9E3779B97F4A7C15L;
                    long z = state;
                    z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
                    z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
                    return z ^ (z >>> 31);
                }
            }

            """);
        yield return new SourceFile("src/common/Common.java", $$"""
            // Common: helpers shared by every module: rotation and FNV-1a mixing.
            // {{Banner}}
            package common;

            public final class Common {
                private Common() {
                }

                public static long rotl(long value, int amount) {
                    return Long.rotateLeft(value, amount);
                }

                public static long fnv(long hash, long value) {
                    return (hash ^ value) * 0x100000001B3L;
                }
            }

            """);
        var main = new StringBuilder();
        main.AppendLine($"// {project.Name}: runs every module once and prints an FNV-1a hash of their results.");
        main.AppendLine($"// {Banner}");
        main.AppendLine();
        main.AppendLine("public final class Main {");
        main.AppendLine("    public static void main(String[] args) {");
        main.AppendLine("        long hash = 0xCBF29CE484222325L;");
        foreach (int group in project.Groups)
            main.AppendLine($"        hash = group{group}.GroupRunner.runAll(hash);");
        main.AppendLine("        System.out.print(hex16(hash) + \"\\n\");");
        main.AppendLine("        System.out.flush();");
        main.AppendLine("    }");
        main.AppendLine();
        // As in the apps: String.format would pull java.text into the native image.
        main.AppendLine("    static String hex16(long value) {");
        main.AppendLine("        String digits = Long.toHexString(value);");
        main.AppendLine("        return \"0\".repeat(16 - digits.length()) + digits;");
        main.AppendLine("    }");
        main.AppendLine("}");
        yield return new SourceFile("src/Main.java", main.ToString());
        foreach (int group in project.Groups)
        {
            var runner = new StringBuilder();
            runner.AppendLine($"// Group {group}: a runner that runs the group's modules in order.");
            runner.AppendLine($"// {Banner}");
            runner.AppendLine($"package group{group};");
            runner.AppendLine();
            runner.AppendLine("import common.Common;");
            runner.AppendLine();
            runner.AppendLine("public final class GroupRunner {");
            runner.AppendLine("    private GroupRunner() {");
            runner.AppendLine("    }");
            runner.AppendLine();
            runner.AppendLine("    public static long runAll(long hash) {");
            runner.AppendLine("        long result = hash;");
            foreach (var m in project.InGroup(group))
                runner.AppendLine($"        result = Common.fnv(result, {(m.SharesFile ? m.File + "." : "")}{m.Name}.run({m.Index}L));");
            runner.AppendLine("        return result;");
            runner.AppendLine("    }");
            runner.AppendLine("}");
            yield return new SourceFile($"src/group{group}/GroupRunner.java", runner.ToString());
        }
        foreach (var file in project.Files)
        {
            var modules = file.ToList();
            var imports = new List<string>();
            if (modules.Any(m => m.UsesRotl || m.UsesFnv))
                imports.Add("common.Common");
            imports.Add("common.Rng");
            imports.AddRange(modules.SelectMany(m => m.Dependencies.Select(d => Import(m, d.Module))).OfType<string>().Distinct());
            if (modules.Any(m => m.Kind == Kind.Tally))
                imports.Add("java.util.HashMap");

            var text = new StringBuilder(FileComment("//", modules));
            text.AppendLine($"package group{modules[0].Group};");
            text.AppendLine();
            foreach (string import in imports)
                text.AppendLine($"import {import};");
            text.AppendLine();
            if (modules.Count == 1)
            {
                text.Append(Module(modules[0]));
            }
            else
            {
                text.AppendLine($"public final class {modules[0].File} {{");
                text.AppendLine($"    private {modules[0].File}() {{");
                text.AppendLine("    }");
                foreach (var m in modules)
                    text.AppendLine().Append(ModuleComment("//", m, "    ")).AppendLine(Indent(Module(m).TrimEnd(), 1));
                text.AppendLine("}");
            }
            yield return new SourceFile(ModuleFile(modules[0]), text.ToString());
        }
    }

    /// <summary>One module's class: a file's public class, or a class nested in its file's class.</summary>
    string Module(ModuleSpec m)
    {
        var body = new StringBuilder();
        body.AppendLine($"private {m.Name}() {{");
        body.AppendLine("}");
        body.AppendLine();
        body.AppendLine(Template(m).TrimEnd());
        foreach (var kernel in m.Kernels)
        {
            body.AppendLine();
            body.AppendLine($"private static long kernel{kernel.Suffix}(long x, long y) {{");
            Statements(body, kernel.Body, 1);
            body.AppendLine("}");
        }
        body.AppendLine();
        body.AppendLine("public static long mix(long x) {");
        body.AppendLine($"    return {MixBody(m, k => "kernel" + k, Literal)};");
        body.AppendLine("}");
        body.AppendLine();
        body.AppendLine("public static long run(long seed) {");
        body.AppendLine($"    long r = {Entry(m.Kind)}(seed ^ {Literal(m.Salt)});");
        foreach (var d in m.Dependencies)
            body.AppendLine($"    r ^= {ClassName(m, d.Module)}.mix(r + {Literal(d.Offset)});");
        if (m.Editable)
            body.AppendLine("    r ^= 0L; // bench:edit");
        body.AppendLine("    return mix(r);");
        body.AppendLine("}");

        var file = new StringBuilder();
        file.AppendLine($"public {(m.SharesFile ? "static " : "")}final class {m.Name} {{");
        file.AppendLine(Indent(body.ToString().TrimEnd(), 1));
        file.AppendLine("}");
        return file.ToString();
    }

    static string Entry(Kind kind) => kind switch
    {
        Kind.Vector => "simulate",
        Kind.Machine => "scan",
        Kind.Dispatch => "dispatch",
        Kind.Tally => "tally",
        Kind.Sorter => "sortAndSearch",
        Kind.Ring => "churn",
        Kind.Checksum => "checksum",
        Kind.Text => "render",
        Kind.Matrix => "power",
        Kind.Forest => "connect",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    string Template(ModuleSpec m) => m.Kind switch
    {
        Kind.Vector => $$"""
            private record Vec3(long x, long y, long z) {
                Vec3 add(Vec3 other) {
                    return new Vec3(x + other.x, y + other.y, z + other.z);
                }

                Vec3 scale(long factor) {
                    return new Vec3(x * factor, y * factor, z * factor);
                }

                long dot(Vec3 other) {
                    return x * other.x + y * other.y + z * other.z;
                }

                Vec3 cross(Vec3 other) {
                    return new Vec3(y * other.z - z * other.y, z * other.x - x * other.z, x * other.y - y * other.x);
                }
            }

            private static long simulate(long seed) {
                Rng rng = new Rng(seed);
                Vec3 sum = new Vec3(0, 0, 0);
                for (int i = 0; i < {{m.Count}}; i++) {
                    long x = rng.next();
                    long y = rng.next();
                    long z = rng.next();
                    Vec3 v = new Vec3(x, y, z);
                    Vec3 w = v.cross(sum).add(v.scale({{Literal(m.Multiplier)}}));
                    sum = sum.add(w);
                    if ((sum.dot(v) & 1) == 1) {
                        sum = sum.scale({{Literal(m.Constant)}});
                    }
                }
                return sum.dot(sum) ^ sum.x;
            }
            """,
        Kind.Machine => $$"""
            private enum State { IDLE, NUMBER, WORD, SPACE, DONE }

            private static State step(State state, long c) {
                return switch (state) {
                    case IDLE -> c < 48 ? State.SPACE : c < 58 ? State.NUMBER : State.WORD;
                    case NUMBER -> c >= 48 && c < 58 ? State.NUMBER : c == 0 ? State.DONE : State.SPACE;
                    case WORD -> c >= 58 ? State.WORD : c < 48 ? State.SPACE : State.NUMBER;
                    case SPACE -> c < 48 ? State.SPACE : State.IDLE;
                    case DONE -> State.DONE;
                };
            }

            private static long scan(long seed) {
                Rng rng = new Rng(seed);
                State state = State.IDLE;
                long numbers = 0;
                long words = 0;
                long spaces = 0;
                long acc = {{Literal(m.Constant)}};
                for (long i = 0; i < {{m.Count}}; i++) {
                    long c = rng.next() >>> 56;
                    state = step(state, c);
                    switch (state) {
                        case NUMBER -> {
                            numbers++;
                            acc = acc * 31 + c;
                        }
                        case WORD -> {
                            words++;
                            acc = Common.rotl(acc, 5) ^ c;
                        }
                        case SPACE -> spaces++;
                        case IDLE -> acc += i;
                        case DONE -> {
                            state = State.IDLE;
                            acc ^= {{Literal(m.Constant2)}};
                        }
                    }
                }
                return acc ^ (numbers << 40) ^ (words << 20) ^ spaces;
            }
            """,
        Kind.Dispatch => $$"""
            private interface Transform {
                long apply(long value);

                long weight();
            }

            private record Rotate(int amount, long bias) implements Transform {
                public long apply(long value) {
                    return Common.rotl(value, amount) ^ bias;
                }

                public long weight() {
                    return amount;
                }
            }

            private record Affine(long multiplier, long offset) implements Transform {
                public long apply(long value) {
                    return value * multiplier + offset;
                }

                public long weight() {
                    return multiplier;
                }
            }

            private record Fold(long mask) implements Transform {
                public long apply(long value) {
                    return (value ^ (value >>> 29)) + (value & mask);
                }

                public long weight() {
                    return mask >>> 32;
                }
            }

            private static long drive(Transform transform, long start, long steps) {
                long value = start;
                for (long i = 0; i < steps; i++) {
                    value = transform.apply(value) + i;
                }
                return value ^ transform.weight();
            }

            private static long dispatch(long seed) {
                Rng rng = new Rng(seed);
                Transform rotate = new Rotate({{m.Shift}}, {{Literal(m.Constant)}});
                Transform affine = new Affine({{Literal(m.Multiplier)}}, {{Literal(m.Constant2)}});
                Transform fold = new Fold({{Literal(m.Mask)}});
                long value = rng.next();
                for (int round = 0; round < {{m.Count}}; round++) {
                    long pick = Long.remainderUnsigned(rng.next(), 3);
                    if (pick == 0) {
                        value = drive(rotate, value, {{m.Inner}});
                    } else if (pick == 1) {
                        value = drive(affine, value, {{m.Inner}});
                    } else {
                        value = drive(fold, value, {{m.Inner}});
                    }
                }
                return value;
            }
            """,
        Kind.Tally => $$"""
            private static long tally(long seed) {
                Rng rng = new Rng(seed);
                HashMap<Long, Long> counts = new HashMap<>();
                for (long i = 0; i < {{m.Count}}; i++) {
                    long key = Long.remainderUnsigned(rng.next(), {{m.Size}});
                    counts.put(key, counts.getOrDefault(key, 0L) + i + 1);
                }
                long acc = {{Literal(m.Constant)}};
                for (long key = 0; key < {{m.Size}}; key++) {
                    Long count = counts.get(key);
                    if (count != null) {
                        acc = Common.fnv(acc, count) ^ key;
                    }
                }
                return acc ^ counts.size();
            }
            """,
        Kind.Sorter => $$"""
            private static void insertionSort(long[] values) {
                for (int i = 1; i < values.length; i++) {
                    long value = values[i];
                    int j = i;
                    while (j > 0 && values[j - 1] > value) {
                        values[j] = values[j - 1];
                        j--;
                    }
                    values[j] = value;
                }
            }

            private static int lowerBound(long[] values, long target) {
                int low = 0;
                int high = values.length;
                while (low < high) {
                    int middle = low + (high - low) / 2;
                    if (values[middle] < target) {
                        low = middle + 1;
                    } else {
                        high = middle;
                    }
                }
                return low;
            }

            private static long sortAndSearch(long seed) {
                Rng rng = new Rng(seed);
                long[] values = new long[{{m.Count}}];
                for (int i = 0; i < values.length; i++) {
                    values[i] = rng.next() >>> 2;
                }
                insertionSort(values);
                long acc = 0;
                for (int query = 0; query < {{m.Size}}; query++) {
                    long target = rng.next() >>> 2;
                    acc = acc * {{Literal(m.Multiplier)}} + lowerBound(values, target);
                }
                return acc ^ values[{{m.Count / 2}}];
            }
            """,
        Kind.Ring => $$"""
            private static final int CAPACITY = {{m.Size}};

            private static final class Ring {
                private final long[] items = new long[CAPACITY];
                private int head;
                private int count;

                void push(long value) {
                    items[(head + count) % CAPACITY] = value;
                    if (count < CAPACITY) {
                        count++;
                    } else {
                        head = (head + 1) % CAPACITY;
                    }
                }

                long pop() {
                    if (count == 0) {
                        return 0;
                    }
                    long value = items[head];
                    head = (head + 1) % CAPACITY;
                    count--;
                    return value;
                }

                long weightedSum() {
                    long sum = 0;
                    for (int i = 0; i < count; i++) {
                        sum += items[(head + i) % CAPACITY] * (i + 1);
                    }
                    return sum;
                }
            }

            private static long churn(long seed) {
                Rng rng = new Rng(seed);
                Ring ring = new Ring();
                long acc = 0;
                for (int i = 0; i < {{m.Count}}; i++) {
                    long value = rng.next();
                    if ((value & 3) == 0) {
                        acc ^= ring.pop();
                    } else {
                        ring.push(value ^ acc);
                    }
                }
                return acc ^ ring.weightedSum() ^ ring.count;
            }
            """,
        Kind.Checksum => $$"""
            private static void fillTable(long[] table) {
                for (int i = 0; i < table.length; i++) {
                    long crc = i;
                    for (int bit = 0; bit < 8; bit++) {
                        crc = (crc & 1) == 1 ? (crc >>> 1) ^ {{Literal(m.Constant2)}} : crc >>> 1;
                    }
                    table[i] = crc;
                }
            }

            private static long update(long[] table, long crc, long value) {
                return table[(int) ((crc ^ value) & 255)] ^ (crc >>> 8);
            }

            private static long checksum(long seed) {
                Rng rng = new Rng(seed);
                long[] table = new long[256];
                fillTable(table);
                long crc = -1L;
                for (int i = 0; i < {{m.Count}}; i++) {
                    crc = update(table, crc, rng.next() >>> 56);
                }
                return ~crc ^ Common.rotl(crc, {{m.Shift}});
            }
            """,
        Kind.Text => $$"""
            private static void appendDecimal(StringBuilder buffer, long value) {
                if (value == 0) {
                    buffer.append('0');
                    return;
                }
                char[] digits = new char[20];
                int count = 0;
                for (long rest = value; rest > 0; rest /= 10) {
                    digits[count++] = (char) ('0' + rest % 10);
                }
                while (count > 0) {
                    buffer.append(digits[--count]);
                }
            }

            private static long render(long seed) {
                Rng rng = new Rng(seed);
                StringBuilder buffer = new StringBuilder();
                for (int i = 0; i < {{m.Count}}; i++) {
                    appendDecimal(buffer, Long.remainderUnsigned(rng.next(), {{m.Size}}));
                    buffer.append(',');
                }
                long total = 0;
                long current = 0;
                long fields = 0;
                for (int i = 0; i < buffer.length(); i++) {
                    char c = buffer.charAt(i);
                    if (c == ',') {
                        total = total * {{m.Multiplier}} + current;
                        current = 0;
                        fields++;
                    } else {
                        current = current * 10 + (c - '0');
                    }
                }
                return total ^ (fields << 32) ^ buffer.length();
            }
            """,
        Kind.Matrix => $$"""
            private static final int SIZE = {{m.Size}};
            private static final int CELLS = SIZE * SIZE;

            private static final class Matrix {
                final long[] cells = new long[CELLS];

                static Matrix identity() {
                    Matrix result = new Matrix();
                    for (int i = 0; i < SIZE; i++) {
                        result.cells[i * SIZE + i] = 1;
                    }
                    return result;
                }

                Matrix multiply(Matrix other) {
                    Matrix result = new Matrix();
                    for (int row = 0; row < SIZE; row++) {
                        for (int column = 0; column < SIZE; column++) {
                            long sum = 0;
                            for (int k = 0; k < SIZE; k++) {
                                sum += cells[row * SIZE + k] * other.cells[k * SIZE + column];
                            }
                            result.cells[row * SIZE + column] = sum;
                        }
                    }
                    return result;
                }

                long trace() {
                    long sum = 0;
                    for (int i = 0; i < SIZE; i++) {
                        sum += cells[i * SIZE + i];
                    }
                    return sum;
                }
            }

            private static long power(long seed) {
                Rng rng = new Rng(seed);
                Matrix base = new Matrix();
                for (int i = 0; i < CELLS; i++) {
                    base.cells[i] = rng.next();
                }
                Matrix result = Matrix.identity();
                for (int step = 0; step < {{m.Count}}; step++) {
                    result = result.multiply(base);
                    base.cells[step % CELLS] ^= result.cells[0];
                }
                return result.trace() ^ result.cells[CELLS - 1];
            }
            """,
        Kind.Forest => $$"""
            private static final int NODES = {{m.Size}};

            private static final class Forest {
                private final int[] parent = new int[NODES];
                private final int[] rank = new int[NODES];

                Forest() {
                    for (int i = 0; i < NODES; i++) {
                        parent[i] = i;
                    }
                }

                int find(int node) {
                    int root = node;
                    while (parent[root] != root) {
                        parent[root] = parent[parent[root]];
                        root = parent[root];
                    }
                    return root;
                }

                boolean unite(int a, int b) {
                    int rootA = find(a);
                    int rootB = find(b);
                    if (rootA == rootB) {
                        return false;
                    }
                    if (rank[rootA] < rank[rootB]) {
                        int swap = rootA;
                        rootA = rootB;
                        rootB = swap;
                    }
                    parent[rootB] = rootA;
                    if (rank[rootA] == rank[rootB]) {
                        rank[rootA]++;
                    }
                    return true;
                }
            }

            private static long connect(long seed) {
                Rng rng = new Rng(seed);
                Forest forest = new Forest();
                long merges = 0;
                for (int i = 0; i < {{m.Inner}}; i++) {
                    int a = (int) Long.remainderUnsigned(rng.next(), NODES);
                    int b = (int) Long.remainderUnsigned(rng.next(), NODES);
                    if (forest.unite(a, b)) {
                        merges++;
                    }
                }
                long acc = 0;
                for (int i = 0; i < NODES; i++) {
                    acc = acc * 31 + forest.find(i);
                }
                return acc ^ merges;
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };
}
