using System.Text;

namespace Runner.Generator;

/// <summary>One project: Program.cs, Common.cs, and GroupN/NameNNN.cs, one static class per module in namespace &lt;Project&gt;.GroupN.</summary>
sealed class CSharpEmitter : Emitter
{
    public override string Folder => "CSharp";

    protected override string Literal(ulong value) => Number(value) + "UL";
    protected override string Rotl(string value, int amount) => $"Common.Rotl({value}, {amount})";
    protected override string Declare(Declare d) => $"ulong {d.Name} = {Expression(d.Value)};";
    protected override string LoopHead(Loop loop, bool variableUsed) =>
        $"for (ulong {loop.Variable} = 0; {loop.Variable} < {loop.Count}; {loop.Variable}++)";
    protected override bool AllmanBraces => true;

    static string Call(ModuleSpec from, ModuleSpec to) => (from.Group == to.Group ? "" : $"Group{to.Group}.") + to.Name + ".Mix";

    public override IEnumerable<SourceFile> Emit(ProjectSpec project)
    {
        yield return new SourceFile($"{project.Name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        yield return new SourceFile("Common.cs", $$"""
            // Common: helpers shared by every module: a SplitMix64 generator, rotation and FNV-1a mixing.
            // {{Banner}}
            using System.Numerics;

            namespace {{project.Name}};

            public sealed class Rng(ulong seed)
            {
                private ulong state = seed;

                public ulong Next()
                {
                    state += 0x9E3779B97F4A7C15;
                    ulong z = state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
                    return z ^ (z >> 31);
                }
            }

            public static class Common
            {
                public static ulong Rotl(ulong value, int amount) => BitOperations.RotateLeft(value, amount);

                public static ulong Fnv(ulong hash, ulong value) => (hash ^ value) * 0x100000001B3;
            }

            """);
        var main = new StringBuilder();
        main.AppendLine($"// {project.Name}: runs every module once and prints an FNV-1a hash of their results.");
        main.AppendLine($"// {Banner}");
        main.AppendLine("ulong hash = 0xCBF29CE484222325;");
        foreach (int group in project.Groups)
            main.AppendLine($"hash = {project.Name}.Group{group}.GroupRunner.RunAll(hash);");
        main.AppendLine("Console.WriteLine(hash.ToString(\"x16\"));");
        yield return new SourceFile("Program.cs", main.ToString());
        foreach (int group in project.Groups)
        {
            var runner = new StringBuilder();
            runner.AppendLine($"// Group {group}: a runner that runs the group's modules in order.");
            runner.AppendLine($"// {Banner}");
            runner.AppendLine($"namespace {project.Name}.Group{group};");
            runner.AppendLine();
            runner.AppendLine("public static class GroupRunner");
            runner.AppendLine("{");
            runner.AppendLine("    public static ulong RunAll(ulong hash)");
            runner.AppendLine("    {");
            runner.AppendLine("        ulong result = hash;");
            foreach (var m in project.InGroup(group))
                runner.AppendLine($"        result = Common.Fnv(result, {m.Name}.Run({m.Index}));");
            runner.AppendLine("        return result;");
            runner.AppendLine("    }");
            runner.AppendLine("}");
            yield return new SourceFile($"Group{group}/GroupRunner.cs", runner.ToString());
        }
        foreach (var file in project.Files)
        {
            var modules = file.ToList();
            var text = new StringBuilder(FileComment("//", modules));
            if (modules.Any(m => m.Kind == Kind.Text))
                text.AppendLine("using System.Runtime.InteropServices;").AppendLine();
            text.AppendLine($"namespace {project.Name}.Group{modules[0].Group};");
            foreach (var m in modules)
                text.AppendLine().Append(ModuleComment("//", m)).Append(Module(m));
            yield return new SourceFile(ModuleFile(modules[0]), text.ToString());
        }
    }

    public override string ModuleFile(ModuleSpec m) => $"Group{m.Group}/{m.File}.cs";

    /// <summary>One module: a static class holding its types and functions.</summary>
    string Module(ModuleSpec m)
    {
        var body = new StringBuilder();
        body.AppendLine(Template(m).TrimEnd());
        foreach (var kernel in m.Kernels)
        {
            body.AppendLine();
            body.AppendLine($"private static ulong Kernel{kernel.Suffix}(ulong x, ulong y)");
            body.AppendLine("{");
            Statements(body, kernel.Body, 1);
            body.AppendLine("}");
        }
        body.AppendLine();
        body.AppendLine($"public static ulong Mix(ulong x) => {MixBody(m, k => "Kernel" + k, Literal)};");
        body.AppendLine();
        body.AppendLine("public static ulong Run(ulong seed)");
        body.AppendLine("{");
        body.AppendLine($"    ulong r = {Entry(m.Kind)}(seed ^ {Literal(m.Salt)});");
        foreach (var d in m.Dependencies)
            body.AppendLine($"    r ^= {Call(m, d.Module)}(r + {Literal(d.Offset)});");
        if (m.Editable)
            body.AppendLine("    r ^= 0UL; // bench:edit");
        body.AppendLine("    return Mix(r);");
        body.AppendLine("}");

        var file = new StringBuilder();
        file.AppendLine($"public static class {m.Name}");
        file.AppendLine("{");
        file.AppendLine(Indent(body.ToString().TrimEnd(), 1));
        file.AppendLine("}");
        return file.ToString();
    }

    static string Entry(Kind kind) => kind switch
    {
        Kind.Vector => "Simulate",
        Kind.Machine => "Scan",
        Kind.Dispatch => "Dispatch",
        Kind.Tally => "Tally",
        Kind.Sorter => "SortAndSearch",
        Kind.Ring => "Churn",
        Kind.Checksum => "Checksum",
        Kind.Text => "Render",
        Kind.Matrix => "Power",
        Kind.Forest => "Connect",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    string Template(ModuleSpec m) => m.Kind switch
    {
        Kind.Vector => $$"""
            private readonly record struct Vec3(ulong X, ulong Y, ulong Z)
            {
                public Vec3 Add(Vec3 other) => new(X + other.X, Y + other.Y, Z + other.Z);

                public Vec3 Scale(ulong factor) => new(X * factor, Y * factor, Z * factor);

                public ulong Dot(Vec3 other) => X * other.X + Y * other.Y + Z * other.Z;

                public Vec3 Cross(Vec3 other) =>
                    new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
            }

            private static ulong Simulate(ulong seed)
            {
                var rng = new Rng(seed);
                var sum = new Vec3(0, 0, 0);
                for (int i = 0; i < {{m.Count}}; i++)
                {
                    ulong x = rng.Next();
                    ulong y = rng.Next();
                    ulong z = rng.Next();
                    var v = new Vec3(x, y, z);
                    var w = v.Cross(sum).Add(v.Scale({{Literal(m.Multiplier)}}));
                    sum = sum.Add(w);
                    if ((sum.Dot(v) & 1) == 1)
                        sum = sum.Scale({{Literal(m.Constant)}});
                }
                return sum.Dot(sum) ^ sum.X;
            }
            """,
        Kind.Machine => $$"""
            private enum State { Idle, Number, Word, Space, Done }

            private static State Step(State state, ulong c) => state switch
            {
                State.Idle => c < 48 ? State.Space : c < 58 ? State.Number : State.Word,
                State.Number => c is >= 48 and < 58 ? State.Number : c == 0 ? State.Done : State.Space,
                State.Word => c >= 58 ? State.Word : c < 48 ? State.Space : State.Number,
                State.Space => c < 48 ? State.Space : State.Idle,
                _ => State.Done,
            };

            private static ulong Scan(ulong seed)
            {
                var rng = new Rng(seed);
                var state = State.Idle;
                ulong numbers = 0, words = 0, spaces = 0;
                ulong acc = {{Literal(m.Constant)}};
                for (ulong i = 0; i < {{m.Count}}; i++)
                {
                    ulong c = rng.Next() >> 56;
                    state = Step(state, c);
                    switch (state)
                    {
                        case State.Number:
                            numbers++;
                            acc = acc * 31 + c;
                            break;
                        case State.Word:
                            words++;
                            acc = Common.Rotl(acc, 5) ^ c;
                            break;
                        case State.Space:
                            spaces++;
                            break;
                        case State.Idle:
                            acc += i;
                            break;
                        case State.Done:
                            state = State.Idle;
                            acc ^= {{Literal(m.Constant2)}};
                            break;
                    }
                }
                return acc ^ (numbers << 40) ^ (words << 20) ^ spaces;
            }
            """,
        Kind.Dispatch => $$"""
            private interface ITransform
            {
                ulong Apply(ulong value);
                ulong Weight { get; }
            }

            private sealed class Rotate(int amount, ulong bias) : ITransform
            {
                public ulong Apply(ulong value) => Common.Rotl(value, amount) ^ bias;
                public ulong Weight => (ulong)amount;
            }

            private sealed class Affine(ulong multiplier, ulong offset) : ITransform
            {
                public ulong Apply(ulong value) => value * multiplier + offset;
                public ulong Weight => multiplier;
            }

            private sealed class Fold(ulong mask) : ITransform
            {
                public ulong Apply(ulong value) => (value ^ (value >> 29)) + (value & mask);
                public ulong Weight => mask >> 32;
            }

            private static ulong Drive(ITransform transform, ulong start, ulong steps)
            {
                ulong value = start;
                for (ulong i = 0; i < steps; i++)
                    value = transform.Apply(value) + i;
                return value ^ transform.Weight;
            }

            private static ulong Dispatch(ulong seed)
            {
                var rng = new Rng(seed);
                var rotate = new Rotate({{m.Shift}}, {{Literal(m.Constant)}});
                var affine = new Affine({{Literal(m.Multiplier)}}, {{Literal(m.Constant2)}});
                var fold = new Fold({{Literal(m.Mask)}});
                ulong value = rng.Next();
                for (int round = 0; round < {{m.Count}}; round++)
                {
                    value = (rng.Next() % 3) switch
                    {
                        0 => Drive(rotate, value, {{m.Inner}}),
                        1 => Drive(affine, value, {{m.Inner}}),
                        _ => Drive(fold, value, {{m.Inner}}),
                    };
                }
                return value;
            }
            """,
        Kind.Tally => $$"""
            private static ulong Tally(ulong seed)
            {
                var rng = new Rng(seed);
                var counts = new Dictionary<ulong, ulong>();
                for (ulong i = 0; i < {{m.Count}}; i++)
                {
                    ulong key = rng.Next() % {{m.Size}};
                    counts[key] = counts.GetValueOrDefault(key) + i + 1;
                }
                ulong acc = {{Literal(m.Constant)}};
                for (ulong key = 0; key < {{m.Size}}; key++)
                {
                    if (counts.TryGetValue(key, out ulong count))
                        acc = Common.Fnv(acc, count) ^ key;
                }
                return acc ^ (ulong)counts.Count;
            }
            """,
        Kind.Sorter => $$"""
            private static void InsertionSort(Span<ulong> values)
            {
                for (int i = 1; i < values.Length; i++)
                {
                    ulong value = values[i];
                    int j = i;
                    while (j > 0 && values[j - 1] > value)
                    {
                        values[j] = values[j - 1];
                        j--;
                    }
                    values[j] = value;
                }
            }

            private static int LowerBound(ReadOnlySpan<ulong> values, ulong target)
            {
                int low = 0, high = values.Length;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (values[middle] < target)
                        low = middle + 1;
                    else
                        high = middle;
                }
                return low;
            }

            private static ulong SortAndSearch(ulong seed)
            {
                var rng = new Rng(seed);
                var values = new ulong[{{m.Count}}];
                for (int i = 0; i < values.Length; i++)
                    values[i] = rng.Next() >> 2;
                InsertionSort(values);
                ulong acc = 0;
                for (int query = 0; query < {{m.Size}}; query++)
                {
                    ulong target = rng.Next() >> 2;
                    acc = acc * {{Literal(m.Multiplier)}} + (ulong)LowerBound(values, target);
                }
                return acc ^ values[{{m.Count / 2}}];
            }
            """,
        Kind.Ring => $$"""
            private const int Capacity = {{m.Size}};

            private sealed class Ring
            {
                private readonly ulong[] items = new ulong[Capacity];
                private int head;

                public int Count { get; private set; }

                public void Push(ulong value)
                {
                    items[(head + Count) % Capacity] = value;
                    if (Count < Capacity)
                        Count++;
                    else
                        head = (head + 1) % Capacity;
                }

                public ulong Pop()
                {
                    if (Count == 0)
                        return 0;
                    ulong value = items[head];
                    head = (head + 1) % Capacity;
                    Count--;
                    return value;
                }

                public ulong WeightedSum()
                {
                    ulong sum = 0;
                    for (int i = 0; i < Count; i++)
                        sum += items[(head + i) % Capacity] * (ulong)(i + 1);
                    return sum;
                }
            }

            private static ulong Churn(ulong seed)
            {
                var rng = new Rng(seed);
                var ring = new Ring();
                ulong acc = 0;
                for (int i = 0; i < {{m.Count}}; i++)
                {
                    ulong value = rng.Next();
                    if ((value & 3) == 0)
                        acc ^= ring.Pop();
                    else
                        ring.Push(value ^ acc);
                }
                return acc ^ ring.WeightedSum() ^ (ulong)ring.Count;
            }
            """,
        Kind.Checksum => $$"""
            private static void FillTable(Span<ulong> table)
            {
                for (int i = 0; i < table.Length; i++)
                {
                    ulong crc = (ulong)i;
                    for (int bit = 0; bit < 8; bit++)
                        crc = (crc & 1) == 1 ? (crc >> 1) ^ {{Literal(m.Constant2)}} : crc >> 1;
                    table[i] = crc;
                }
            }

            private static ulong Update(ReadOnlySpan<ulong> table, ulong crc, ulong value) =>
                table[(int)((crc ^ value) & 255)] ^ (crc >> 8);

            private static ulong Checksum(ulong seed)
            {
                var rng = new Rng(seed);
                var table = new ulong[256];
                FillTable(table);
                ulong crc = ulong.MaxValue;
                for (int i = 0; i < {{m.Count}}; i++)
                    crc = Update(table, crc, rng.Next() >> 56);
                return ~crc ^ Common.Rotl(crc, {{m.Shift}});
            }
            """,
        Kind.Text => $$"""
            private static void AppendDecimal(List<byte> buffer, ulong value)
            {
                if (value == 0)
                {
                    buffer.Add((byte)'0');
                    return;
                }
                Span<byte> digits = stackalloc byte[20];
                int count = 0;
                for (ulong rest = value; rest > 0; rest /= 10)
                    digits[count++] = (byte)('0' + rest % 10);
                while (count > 0)
                    buffer.Add(digits[--count]);
            }

            private static ulong Render(ulong seed)
            {
                var rng = new Rng(seed);
                var buffer = new List<byte>();
                for (int i = 0; i < {{m.Count}}; i++)
                {
                    AppendDecimal(buffer, rng.Next() % {{m.Size}});
                    buffer.Add((byte)',');
                }
                ulong total = 0, current = 0, fields = 0;
                foreach (byte b in CollectionsMarshal.AsSpan(buffer))
                {
                    if (b == ',')
                    {
                        total = total * {{m.Multiplier}} + current;
                        current = 0;
                        fields++;
                    }
                    else
                    {
                        current = current * 10 + (ulong)(b - '0');
                    }
                }
                return total ^ (fields << 32) ^ (ulong)buffer.Count;
            }
            """,
        Kind.Matrix => $$"""
            private const int Size = {{m.Size}};
            private const int CellCount = Size * Size;

            private sealed class Matrix
            {
                public readonly ulong[] Cells = new ulong[CellCount];

                public static Matrix Identity()
                {
                    var result = new Matrix();
                    for (int i = 0; i < Size; i++)
                        result.Cells[i * Size + i] = 1;
                    return result;
                }

                public Matrix Multiply(Matrix other)
                {
                    var result = new Matrix();
                    for (int row = 0; row < Size; row++)
                    {
                        for (int column = 0; column < Size; column++)
                        {
                            ulong sum = 0;
                            for (int k = 0; k < Size; k++)
                                sum += Cells[row * Size + k] * other.Cells[k * Size + column];
                            result.Cells[row * Size + column] = sum;
                        }
                    }
                    return result;
                }

                public ulong Trace()
                {
                    ulong sum = 0;
                    for (int i = 0; i < Size; i++)
                        sum += Cells[i * Size + i];
                    return sum;
                }
            }

            private static ulong Power(ulong seed)
            {
                var rng = new Rng(seed);
                var @base = new Matrix();
                for (int i = 0; i < CellCount; i++)
                    @base.Cells[i] = rng.Next();
                var result = Matrix.Identity();
                for (int step = 0; step < {{m.Count}}; step++)
                {
                    result = result.Multiply(@base);
                    @base.Cells[step % CellCount] ^= result.Cells[0];
                }
                return result.Trace() ^ result.Cells[CellCount - 1];
            }
            """,
        Kind.Forest => $$"""
            private const int Nodes = {{m.Size}};

            private sealed class Forest
            {
                private readonly int[] parent = new int[Nodes];
                private readonly uint[] rank = new uint[Nodes];

                public Forest()
                {
                    for (int i = 0; i < Nodes; i++)
                        parent[i] = i;
                }

                public int Find(int node)
                {
                    int root = node;
                    while (parent[root] != root)
                    {
                        parent[root] = parent[parent[root]];
                        root = parent[root];
                    }
                    return root;
                }

                public bool Unite(int a, int b)
                {
                    int rootA = Find(a), rootB = Find(b);
                    if (rootA == rootB)
                        return false;
                    if (rank[rootA] < rank[rootB])
                        (rootA, rootB) = (rootB, rootA);
                    parent[rootB] = rootA;
                    if (rank[rootA] == rank[rootB])
                        rank[rootA]++;
                    return true;
                }
            }

            private static ulong Connect(ulong seed)
            {
                var rng = new Rng(seed);
                var forest = new Forest();
                ulong merges = 0;
                for (int i = 0; i < {{m.Inner}}; i++)
                {
                    int a = (int)(rng.Next() % Nodes);
                    int b = (int)(rng.Next() % Nodes);
                    if (forest.Unite(a, b))
                        merges++;
                }
                ulong acc = 0;
                for (int i = 0; i < Nodes; i++)
                    acc = acc * 31 + (ulong)forest.Find(i);
                return acc ^ merges;
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };
}
