using System.Text;

namespace Runner.Generator;

/// <summary>
/// One Go module with a package per group (groupN/) and a common package. A package shares one
/// namespace, so a module's names start with its own name: Ring016Run, ring016Buffer.
/// </summary>
sealed class GoEmitter : Emitter
{
    public override string Folder => "Go";

    protected override string IndentUnit => "\t";
    protected override string Literal(ulong value) => Number(value);
    protected override string Rotl(string value, int amount) => $"common.Rotl({value}, {amount})";
    protected override string Declare(Declare d) => $"{d.Name} := {Expression(d.Value)}";
    protected override string Assign(Assign a) => $"{a.Name} = {Expression(a.Value)}";
    protected override string IfHead(Compare c) => $"if {Condition(c)}";
    protected override string LoopHead(Loop loop, bool variableUsed) =>
        variableUsed ? $"for {loop.Variable} := uint64(0); {loop.Variable} < {loop.Count}; {loop.Variable}++" : $"for range {loop.Count}";
    protected override string ReturnLine(Return r, bool last) => $"return {Expression(r.Value)}";

    /// <summary>A literal-only start would make Go infer int, so declarations always mention a variable (see KernelBuilder).</summary>
    static string Call(ModuleSpec from, ModuleSpec to, string function) =>
        (from.Group == to.Group ? "" : $"group{to.Group}.") + to.Name + function;

    public override IEnumerable<SourceFile> Emit(ProjectSpec project)
    {
        string module = project.Name.ToLowerInvariant();
        yield return new SourceFile("go.mod", $"module {module}\n\ngo 1.25\n");
        yield return new SourceFile("common/common.go", $$"""
            // Package common holds helpers shared by every module: a SplitMix64 generator, rotation and FNV-1a mixing.
            // {{Banner}}
            package common

            import "math/bits"

            // Rng is a SplitMix64 generator.
            type Rng struct {
            	state uint64
            }

            // NewRng returns a generator that starts from seed.
            func NewRng(seed uint64) Rng {
            	return Rng{state: seed}
            }

            // Next returns the next pseudo-random value.
            func (r *Rng) Next() uint64 {
            	r.state += 0x9E3779B97F4A7C15
            	z := r.state
            	z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
            	z = (z ^ (z >> 27)) * 0x94D049BB133111EB
            	return z ^ (z >> 31)
            }

            // Rotl rotates value left by amount bits.
            func Rotl(value uint64, amount int) uint64 {
            	return bits.RotateLeft64(value, amount)
            }

            // Fnv mixes value into an FNV-1a hash.
            func Fnv(hash uint64, value uint64) uint64 {
            	return (hash ^ value) * 0x100000001B3
            }

            """);
        var main = new StringBuilder();
        main.AppendLine($"// {project.Name} runs every module once and prints an FNV-1a hash of their results.");
        main.AppendLine($"// {Banner}");
        main.AppendLine("package main");
        main.AppendLine();
        main.AppendLine("import (");
        main.AppendLine("\t\"fmt\"");
        main.AppendLine();
        foreach (int group in project.Groups)
            main.AppendLine($"\t\"{module}/group{group}\"");
        main.AppendLine(")");
        main.AppendLine();
        main.AppendLine("func main() {");
        main.AppendLine("\thash := uint64(0xCBF29CE484222325)");
        foreach (int group in project.Groups)
            main.AppendLine($"\thash = group{group}.RunAll(hash)");
        main.AppendLine("\tfmt.Printf(\"%016x\\n\", hash)");
        main.AppendLine("}");
        yield return new SourceFile("main.go", main.ToString());
        foreach (int group in project.Groups)
        {
            var runner = new StringBuilder();
            runner.AppendLine($"// Group {group}: a runner that runs the group's modules in order.");
            runner.AppendLine($"// {Banner}");
            runner.AppendLine($"package group{group}");
            runner.AppendLine();
            runner.AppendLine($"import \"{module}/common\"");
            runner.AppendLine();
            runner.AppendLine("// RunAll runs every module of the group in order and mixes their results into hash.");
            runner.AppendLine("func RunAll(hash uint64) uint64 {");
            runner.AppendLine("\tresult := hash");
            foreach (var m in project.InGroup(group))
                runner.AppendLine($"\tresult = common.Fnv(result, {m.Name}Run({m.Index}))");
            runner.AppendLine("\treturn result");
            runner.AppendLine("}");
            yield return new SourceFile($"group{group}/group.go", runner.ToString());
        }
        foreach (var file in project.Files)
        {
            var modules = file.ToList();
            var text = new StringBuilder(FileComment("//", modules));
            text.AppendLine($"package group{modules[0].Group}");
            text.AppendLine();
            var imports = modules.SelectMany(m => m.Dependencies).Where(d => d.Module.Group != modules[0].Group)
                .Select(d => $"{module}/group{d.Module.Group}")
                .Prepend($"{module}/common").Distinct().Order(StringComparer.Ordinal).ToList();
            if (imports.Count == 1)
                text.AppendLine($"import \"{imports[0]}\"");
            else
                text.AppendLine("import (\n" + string.Concat(imports.Select(i => $"\t\"{i}\"\n")) + ")");
            foreach (var m in modules)
                text.AppendLine().Append(ModuleComment("//", m)).Append(Module(m));
            yield return new SourceFile(ModuleFile(modules[0]), text.ToString());
        }
    }

    public override string ModuleFile(ModuleSpec m) => $"group{m.Group}/{m.File.ToLowerInvariant()}.go";

    /// <summary>One module's declarations; its names start with its own name, since a package shares one namespace.</summary>
    string Module(ModuleSpec m)
    {
        string p = Lower(m.Name);
        var file = new StringBuilder();
        file.AppendLine(Template(m, p).TrimEnd());
        foreach (var kernel in m.Kernels)
        {
            file.AppendLine();
            file.AppendLine($"func {p}Kernel{kernel.Suffix}(x, y uint64) uint64 {{");
            Statements(file, kernel.Body, 1);
            file.AppendLine("}");
        }
        file.AppendLine();
        file.AppendLine($"// {m.Name}Mix mixes x through this module's kernels.");
        file.AppendLine($"func {m.Name}Mix(x uint64) uint64 {{");
        file.AppendLine($"\treturn {MixBody(m, k => p + "Kernel" + k, Literal)}");
        file.AppendLine("}");
        file.AppendLine();
        file.AppendLine($"// {m.Name}Run runs this module from seed and mixes in the modules it depends on.");
        file.AppendLine($"func {m.Name}Run(seed uint64) uint64 {{");
        file.AppendLine($"\tr := {p}{Entry(m.Kind)}(seed ^ {Literal(m.Salt)})");
        foreach (var d in m.Dependencies)
            file.AppendLine($"\tr ^= {Call(m, d.Module, "Mix")}(r + {Literal(d.Offset)})");
        if (m.Editable)
            file.AppendLine("\tr ^= 0 // bench:edit");
        file.AppendLine($"\treturn {m.Name}Mix(r)");
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

    /// <summary>Templates are written with four-space indentation and converted to tabs.</summary>
    string Template(ModuleSpec m, string p) => Tabs(m.Kind switch
    {
        Kind.Vector => $$"""
            type {{p}}Vec3 struct {
                x, y, z uint64
            }

            func (v {{p}}Vec3) add(other {{p}}Vec3) {{p}}Vec3 {
                return {{p}}Vec3{v.x + other.x, v.y + other.y, v.z + other.z}
            }

            func (v {{p}}Vec3) scale(factor uint64) {{p}}Vec3 {
                return {{p}}Vec3{v.x * factor, v.y * factor, v.z * factor}
            }

            func (v {{p}}Vec3) dot(other {{p}}Vec3) uint64 {
                return v.x*other.x + v.y*other.y + v.z*other.z
            }

            func (v {{p}}Vec3) cross(other {{p}}Vec3) {{p}}Vec3 {
                return {{p}}Vec3{
                    v.y*other.z - v.z*other.y,
                    v.z*other.x - v.x*other.z,
                    v.x*other.y - v.y*other.x,
                }
            }

            func {{p}}Simulate(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var sum {{p}}Vec3
                for range {{m.Count}} {
                    x := rng.Next()
                    y := rng.Next()
                    z := rng.Next()
                    v := {{p}}Vec3{x, y, z}
                    w := v.cross(sum).add(v.scale({{Literal(m.Multiplier)}}))
                    sum = sum.add(w)
                    if sum.dot(v)&1 == 1 {
                        sum = sum.scale({{Literal(m.Constant)}})
                    }
                }
                return sum.dot(sum) ^ sum.x
            }
            """,
        Kind.Machine => $$"""
            type {{p}}State int

            const (
                {{p}}Idle {{p}}State = iota
                {{p}}Number
                {{p}}Word
                {{p}}Space
                {{p}}Done
            )

            func {{p}}Step(state {{p}}State, c uint64) {{p}}State {
                switch state {
                case {{p}}Idle:
                    if c < 48 {
                        return {{p}}Space
                    } else if c < 58 {
                        return {{p}}Number
                    }
                    return {{p}}Word
                case {{p}}Number:
                    if c >= 48 && c < 58 {
                        return {{p}}Number
                    } else if c == 0 {
                        return {{p}}Done
                    }
                    return {{p}}Space
                case {{p}}Word:
                    if c >= 58 {
                        return {{p}}Word
                    } else if c < 48 {
                        return {{p}}Space
                    }
                    return {{p}}Number
                case {{p}}Space:
                    if c < 48 {
                        return {{p}}Space
                    }
                    return {{p}}Idle
                default:
                    return {{p}}Done
                }
            }

            func {{p}}Scan(seed uint64) uint64 {
                rng := common.NewRng(seed)
                state := {{p}}Idle
                var numbers, words, spaces uint64
                acc := uint64({{Literal(m.Constant)}})
                for i := uint64(0); i < {{m.Count}}; i++ {
                    c := rng.Next() >> 56
                    state = {{p}}Step(state, c)
                    switch state {
                    case {{p}}Number:
                        numbers++
                        acc = acc*31 + c
                    case {{p}}Word:
                        words++
                        acc = common.Rotl(acc, 5) ^ c
                    case {{p}}Space:
                        spaces++
                    case {{p}}Idle:
                        acc += i
                    case {{p}}Done:
                        state = {{p}}Idle
                        acc ^= {{Literal(m.Constant2)}}
                    }
                }
                return acc ^ (numbers << 40) ^ (words << 20) ^ spaces
            }
            """,
        Kind.Dispatch => $$"""
            type {{p}}Transform interface {
                apply(value uint64) uint64
                weight() uint64
            }

            type {{p}}Rotate struct {
                amount int
                bias   uint64
            }

            func (t {{p}}Rotate) apply(value uint64) uint64 {
                return common.Rotl(value, t.amount) ^ t.bias
            }

            func (t {{p}}Rotate) weight() uint64 {
                return uint64(t.amount)
            }

            type {{p}}Affine struct {
                multiplier, offset uint64
            }

            func (t {{p}}Affine) apply(value uint64) uint64 {
                return value*t.multiplier + t.offset
            }

            func (t {{p}}Affine) weight() uint64 {
                return t.multiplier
            }

            type {{p}}Fold struct {
                mask uint64
            }

            func (t {{p}}Fold) apply(value uint64) uint64 {
                return (value ^ (value >> 29)) + (value & t.mask)
            }

            func (t {{p}}Fold) weight() uint64 {
                return t.mask >> 32
            }

            func {{p}}Drive(transform {{p}}Transform, start, steps uint64) uint64 {
                value := start
                for i := uint64(0); i < steps; i++ {
                    value = transform.apply(value) + i
                }
                return value ^ transform.weight()
            }

            func {{p}}Dispatch(seed uint64) uint64 {
                rng := common.NewRng(seed)
                rotate := {{p}}Rotate{amount: {{m.Shift}}, bias: {{Literal(m.Constant)}}}
                affine := {{p}}Affine{multiplier: {{Literal(m.Multiplier)}}, offset: {{Literal(m.Constant2)}}}
                fold := {{p}}Fold{mask: {{Literal(m.Mask)}}}
                value := rng.Next()
                for range {{m.Count}} {
                    switch rng.Next() % 3 {
                    case 0:
                        value = {{p}}Drive(rotate, value, {{m.Inner}})
                    case 1:
                        value = {{p}}Drive(affine, value, {{m.Inner}})
                    default:
                        value = {{p}}Drive(fold, value, {{m.Inner}})
                    }
                }
                return value
            }
            """,
        Kind.Tally => $$"""
            func {{p}}Tally(seed uint64) uint64 {
                rng := common.NewRng(seed)
                counts := make(map[uint64]uint64)
                for i := uint64(0); i < {{m.Count}}; i++ {
                    counts[rng.Next()%{{m.Size}}] += i + 1
                }
                acc := uint64({{Literal(m.Constant)}})
                for key := uint64(0); key < {{m.Size}}; key++ {
                    if count, ok := counts[key]; ok {
                        acc = common.Fnv(acc, count) ^ key
                    }
                }
                return acc ^ uint64(len(counts))
            }
            """,
        Kind.Sorter => $$"""
            func {{p}}InsertionSort(values []uint64) {
                for i := 1; i < len(values); i++ {
                    value := values[i]
                    j := i
                    for j > 0 && values[j-1] > value {
                        values[j] = values[j-1]
                        j--
                    }
                    values[j] = value
                }
            }

            func {{p}}LowerBound(values []uint64, target uint64) int {
                low, high := 0, len(values)
                for low < high {
                    middle := low + (high-low)/2
                    if values[middle] < target {
                        low = middle + 1
                    } else {
                        high = middle
                    }
                }
                return low
            }

            func {{p}}SortAndSearch(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var values [{{m.Count}}]uint64
                for i := range values {
                    values[i] = rng.Next() >> 2
                }
                {{p}}InsertionSort(values[:])
                var acc uint64
                for range {{m.Size}} {
                    target := rng.Next() >> 2
                    acc = acc*{{Literal(m.Multiplier)}} + uint64({{p}}LowerBound(values[:], target))
                }
                return acc ^ values[{{m.Count / 2}}]
            }
            """,
        Kind.Ring => $$"""
            const {{p}}Capacity = {{m.Size}}

            type {{p}}Ring struct {
                items [{{p}}Capacity]uint64
                head  int
                count int
            }

            func (r *{{p}}Ring) push(value uint64) {
                r.items[(r.head+r.count)%{{p}}Capacity] = value
                if r.count < {{p}}Capacity {
                    r.count++
                } else {
                    r.head = (r.head + 1) % {{p}}Capacity
                }
            }

            func (r *{{p}}Ring) pop() uint64 {
                if r.count == 0 {
                    return 0
                }
                value := r.items[r.head]
                r.head = (r.head + 1) % {{p}}Capacity
                r.count--
                return value
            }

            func (r *{{p}}Ring) weightedSum() uint64 {
                var sum uint64
                for i := 0; i < r.count; i++ {
                    sum += r.items[(r.head+i)%{{p}}Capacity] * uint64(i+1)
                }
                return sum
            }

            func {{p}}Churn(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var ring {{p}}Ring
                var acc uint64
                for range {{m.Count}} {
                    value := rng.Next()
                    if value&3 == 0 {
                        acc ^= ring.pop()
                    } else {
                        ring.push(value ^ acc)
                    }
                }
                return acc ^ ring.weightedSum() ^ uint64(ring.count)
            }
            """,
        Kind.Checksum => $$"""
            func {{p}}FillTable(table *[256]uint64) {
                for i := range table {
                    crc := uint64(i)
                    for range 8 {
                        if crc&1 == 1 {
                            crc = (crc >> 1) ^ {{Literal(m.Constant2)}}
                        } else {
                            crc >>= 1
                        }
                    }
                    table[i] = crc
                }
            }

            func {{p}}Update(table *[256]uint64, crc, value uint64) uint64 {
                return table[(crc^value)&255] ^ (crc >> 8)
            }

            func {{p}}Checksum(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var table [256]uint64
                {{p}}FillTable(&table)
                crc := ^uint64(0)
                for range {{m.Count}} {
                    crc = {{p}}Update(&table, crc, rng.Next()>>56)
                }
                return ^crc ^ common.Rotl(crc, {{m.Shift}})
            }
            """,
        Kind.Text => $$"""
            func {{p}}AppendDecimal(buffer []byte, value uint64) []byte {
                if value == 0 {
                    return append(buffer, '0')
                }
                var digits [20]byte
                count := 0
                for rest := value; rest > 0; rest /= 10 {
                    digits[count] = '0' + byte(rest%10)
                    count++
                }
                for count > 0 {
                    count--
                    buffer = append(buffer, digits[count])
                }
                return buffer
            }

            func {{p}}Render(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var buffer []byte
                for range {{m.Count}} {
                    buffer = {{p}}AppendDecimal(buffer, rng.Next()%{{m.Size}})
                    buffer = append(buffer, ',')
                }
                var total, current, fields uint64
                for _, b := range buffer {
                    if b == ',' {
                        total = total*{{m.Multiplier}} + current
                        current = 0
                        fields++
                    } else {
                        current = current*10 + uint64(b-'0')
                    }
                }
                return total ^ (fields << 32) ^ uint64(len(buffer))
            }
            """,
        Kind.Matrix => $$"""
            const (
                {{p}}Size  = {{m.Size}}
                {{p}}Cells = {{p}}Size * {{p}}Size
            )

            type {{p}}Matrix struct {
                cells [{{p}}Cells]uint64
            }

            func {{p}}Identity() {{p}}Matrix {
                var result {{p}}Matrix
                for i := 0; i < {{p}}Size; i++ {
                    result.cells[i*{{p}}Size+i] = 1
                }
                return result
            }

            func (a *{{p}}Matrix) multiply(b *{{p}}Matrix) {{p}}Matrix {
                var result {{p}}Matrix
                for row := 0; row < {{p}}Size; row++ {
                    for column := 0; column < {{p}}Size; column++ {
                        var sum uint64
                        for k := 0; k < {{p}}Size; k++ {
                            sum += a.cells[row*{{p}}Size+k] * b.cells[k*{{p}}Size+column]
                        }
                        result.cells[row*{{p}}Size+column] = sum
                    }
                }
                return result
            }

            func (a *{{p}}Matrix) trace() uint64 {
                var sum uint64
                for i := 0; i < {{p}}Size; i++ {
                    sum += a.cells[i*{{p}}Size+i]
                }
                return sum
            }

            func {{p}}Power(seed uint64) uint64 {
                rng := common.NewRng(seed)
                var base {{p}}Matrix
                for i := range base.cells {
                    base.cells[i] = rng.Next()
                }
                result := {{p}}Identity()
                for step := 0; step < {{m.Count}}; step++ {
                    result = result.multiply(&base)
                    base.cells[step%{{p}}Cells] ^= result.cells[0]
                }
                return result.trace() ^ result.cells[{{p}}Cells-1]
            }
            """,
        Kind.Forest => $$"""
            const {{p}}Nodes = {{m.Size}}

            type {{p}}Forest struct {
                parent [{{p}}Nodes]int
                rank   [{{p}}Nodes]uint32
            }

            func {{p}}NewForest() *{{p}}Forest {
                forest := &{{p}}Forest{}
                for i := range forest.parent {
                    forest.parent[i] = i
                }
                return forest
            }

            func (f *{{p}}Forest) find(node int) int {
                root := node
                for f.parent[root] != root {
                    f.parent[root] = f.parent[f.parent[root]]
                    root = f.parent[root]
                }
                return root
            }

            func (f *{{p}}Forest) unite(a, b int) bool {
                rootA, rootB := f.find(a), f.find(b)
                if rootA == rootB {
                    return false
                }
                if f.rank[rootA] < f.rank[rootB] {
                    rootA, rootB = rootB, rootA
                }
                f.parent[rootB] = rootA
                if f.rank[rootA] == f.rank[rootB] {
                    f.rank[rootA]++
                }
                return true
            }

            func {{p}}Connect(seed uint64) uint64 {
                rng := common.NewRng(seed)
                forest := {{p}}NewForest()
                var merges uint64
                for range {{m.Inner}} {
                    a := int(rng.Next() % {{p}}Nodes)
                    b := int(rng.Next() % {{p}}Nodes)
                    if forest.unite(a, b) {
                        merges++
                    }
                }
                var acc uint64
                for i := 0; i < {{p}}Nodes; i++ {
                    acc = acc*31 + uint64(forest.find(i))
                }
                return acc ^ merges
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    });

    static string Tabs(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            int spaces = line.TakeWhile(c => c == ' ').Count();
            return new string('\t', spaces / 4) + line[(spaces / 4 * 4)..];
        }));
}
