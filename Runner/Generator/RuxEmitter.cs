using System.Text;

namespace Runner.Generator;

/// <summary>
/// One package. Each module is a `pub module GroupN::NameNNN` in Src/GroupN/NameNNN.rux; the shared
/// helpers sit at the package root (Src/Common.rux), where every module sees them.
/// </summary>
sealed class RuxEmitter : Emitter
{
    public override string Folder => "Rux";

    protected override string Literal(ulong value) => Number(value) + "u64";
    protected override string Rotl(string value, int amount) => $"Rotl({value}, {amount}u32)";
    protected override string Declare(Declare d) => $"{(d.Mutable ? "var" : "let")} {d.Name}: uint64 = {Expression(d.Value)};";
    protected override string IfHead(Compare c) => $"if {Condition(c)}";
    protected override string LoopHead(Loop loop, bool variableUsed) => $"for {loop.Variable} in 0u64..{loop.Count}u64";

    static string Path(ModuleSpec m) => $"Group{m.Group}::{m.Name}";

    public override IEnumerable<SourceFile> Emit(ProjectSpec project)
    {
        yield return new SourceFile("Rux.toml", $"""
            [Manifest]
            Version = 1
            MinRux = "0.4.0"

            [Package]
            Name = "{project.Name}"
            Version = "0.1.0"
            Type = "Executable"

            [Dependencies]
            Allocator = {"{"} Namespace = "Rux", Version = "*" {"}"}
            Collections = {"{"} Namespace = "Rux", Version = "*" {"}"}
            Core = {"{"} Namespace = "Rux", Version = "*" {"}"}
            Io = {"{"} Namespace = "Rux", Version = "*" {"}"}

            """);
        yield return new SourceFile("Src/Common.rux", $$"""
            // Common: helpers shared by every module: a SplitMix64 generator, rotation and FNV-1a mixing.
            // {{Banner}}

            pub struct Rng {
                state: uint64;
            }

            extend Rng {
                pub func Rng(seed: uint64) -> Rng {
                    return Rng { state: seed };
                }

                pub func Next(self: &var Rng) -> uint64 {
                    self.state += 0x9E3779B97F4A7C15u64;
                    var z = self.state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9u64;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBu64;
                    return z ^ (z >> 31);
                }
            }

            pub func Rotl(value: uint64, amount: uint32) -> uint64 {
                return (value << amount) | (value >> (64u32 - amount));
            }

            pub func Fnv(hash: uint64, value: uint64) -> uint64 {
                return (hash ^ value) * 0x100000001B3u64;
            }

            """);
        var main = new StringBuilder();
        main.AppendLine($"// {project.Name}: runs every module once and prints an FNV-1a hash of their results.");
        main.AppendLine($"// {Banner}");
        main.AppendLine("import Io::PrintLine;");
        main.AppendLine();
        main.AppendLine("func Main() -> int {");
        main.AppendLine("    var hash: uint64 = 0xCBF29CE484222325u64;");
        foreach (int group in project.Groups)
            main.AppendLine($"    hash = Group{group}::RunAll(hash);");
        main.AppendLine("    PrintLine(\"{:016x}\", hash);");
        main.AppendLine("    return 0;");
        main.AppendLine("}");
        yield return new SourceFile("Src/Main.rux", main.ToString());
        foreach (int group in project.Groups)
        {
            var runner = new StringBuilder();
            runner.AppendLine($"// Group {group}: runs the group's modules in order and mixes their results into the hash.");
            runner.AppendLine($"// {Banner}");
            runner.AppendLine($"pub module Group{group} {{");
            runner.AppendLine("    pub func RunAll(hash: uint64) -> uint64 {");
            runner.AppendLine("        var result = hash;");
            foreach (var m in project.InGroup(group))
                runner.AppendLine($"        result = Fnv(result, {Path(m)}::Run({m.Index}u64));");
            runner.AppendLine("        return result;");
            runner.AppendLine("    }");
            runner.AppendLine("}");
            yield return new SourceFile($"Src/Group{group}/Group.rux", runner.ToString());
        }
        foreach (var file in project.Files)
        {
            var modules = file.ToList();
            yield return new SourceFile(ModuleFile(modules[0]),
                FileComment("//", modules) + string.Join('\n', modules.Select(m => ModuleComment("//", m) + Module(m))));
        }
    }

    public override string ModuleFile(ModuleSpec m) => $"Src/Group{m.Group}/{m.File}.rux";

    /// <summary>One module: a `pub module GroupN::NameNNN` block.</summary>
    string Module(ModuleSpec m)
    {
        var body = new StringBuilder();
        body.AppendLine(UniqueTypeNames(m, Template(m)).TrimEnd());
        foreach (var kernel in m.Kernels)
        {
            body.AppendLine();
            body.AppendLine($"func Kernel{kernel.Suffix}(x: uint64, y: uint64) -> uint64 {{");
            Statements(body, kernel.Body, 1);
            body.AppendLine("}");
        }
        body.AppendLine();
        body.AppendLine("pub func Mix(x: uint64) -> uint64 {");
        body.AppendLine($"    return {MixBody(m, k => "Kernel" + k, Literal)};");
        body.AppendLine("}");
        body.AppendLine();
        body.AppendLine("pub func Run(seed: uint64) -> uint64 {");
        bool mutable = m.Dependencies.Count > 0 || m.Editable;
        body.AppendLine($"    {(mutable ? "var" : "let")} r = {Entry(m.Kind)}(seed ^ {Literal(m.Salt)});");
        foreach (var d in m.Dependencies)
            body.AppendLine($"    r = r ^ {Path(d.Module)}::Mix(r + {Literal(d.Offset)});");
        if (m.Editable)
            body.AppendLine("    r = r ^ 0u64; // bench:edit");
        body.AppendLine("    return Mix(r);");
        body.AppendLine("}");

        var file = new StringBuilder();
        file.AppendLine($"pub module {Path(m)} {{");
        string imports = Imports(m.Kind);
        if (imports.Length > 0)
            file.AppendLine(Indent(imports, 1)).AppendLine();
        file.AppendLine(Indent(body.ToString().TrimEnd(), 1));
        file.AppendLine("}");
        return file.ToString();
    }

    /// <summary>
    /// Rux 0.4.0 attaches an `extend` block to a struct by its simple name, so two modules that each
    /// declare and extend a `Ring` lose their methods. Prefixing the module name keeps every type unique.
    /// </summary>
    static string UniqueTypeNames(ModuleSpec m, string code)
    {
        string[] types = m.Kind switch
        {
            Kind.Vector => ["Vec3"],
            Kind.Machine => ["State"],
            Kind.Dispatch => ["Transform", "Rotate", "Affine", "Fold"],
            Kind.Ring => ["Ring"],
            Kind.Matrix => ["Matrix"],
            Kind.Forest => ["Forest"],
            _ => [],
        };
        foreach (string type in types)
            code = System.Text.RegularExpressions.Regex.Replace(code, $@"\b{type}\b", m.Name + type);
        return code;
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

    static string Imports(Kind kind) => kind switch
    {
        Kind.Tally => "import Allocator::{ Allocator, SystemAllocator };\nimport Collections::{ EqualsUint64, HashMap, HashUint64 };",
        Kind.Text => "import Allocator::{ Allocator, SystemAllocator };\nimport Collections::Vector;",
        _ => "",
    };

    string Template(ModuleSpec m) => m.Kind switch
    {
        Kind.Vector => $$"""
            struct Vec3 {
                x: uint64;
                y: uint64;
                z: uint64;
            }

            extend Vec3 {
                func Add(self: &Vec3, other: &Vec3) -> Vec3 {
                    return Vec3 { x: self.x + other.x, y: self.y + other.y, z: self.z + other.z };
                }

                func Scale(self: &Vec3, factor: uint64) -> Vec3 {
                    return Vec3 { x: self.x * factor, y: self.y * factor, z: self.z * factor };
                }

                func Dot(self: &Vec3, other: &Vec3) -> uint64 {
                    return self.x * other.x + self.y * other.y + self.z * other.z;
                }

                func Cross(self: &Vec3, other: &Vec3) -> Vec3 {
                    return Vec3 {
                        x: self.y * other.z - self.z * other.y,
                        y: self.z * other.x - self.x * other.z,
                        z: self.x * other.y - self.y * other.x
                    };
                }
            }

            func Simulate(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var sum = Vec3 { x: 0u64, y: 0u64, z: 0u64 };
                for i in 0u..{{m.Count}}u {
                    let x = rng.Next();
                    let y = rng.Next();
                    let z = rng.Next();
                    let v = Vec3 { x: x, y: y, z: z };
                    let scaled = v.Scale({{Literal(m.Multiplier)}});
                    let w = v.Cross(sum).Add(scaled);
                    sum = sum.Add(w);
                    if (sum.Dot(v) & 1u64) == 1u64 {
                        sum = sum.Scale({{Literal(m.Constant)}});
                    }
                }
                return sum.Dot(sum) ^ sum.x;
            }
            """,
        Kind.Machine => $$"""
            enum State: uint8 {
                Idle,
                Number,
                Word,
                Space,
                Done
            }

            func Step(state: State, c: uint64) -> State {
                return match state {
                    .Idle => c < 48u64 ? State::Space : (c < 58u64 ? State::Number : State::Word),
                    .Number => c >= 48u64 && c < 58u64 ? State::Number : (c == 0u64 ? State::Done : State::Space),
                    .Word => c >= 58u64 ? State::Word : (c < 48u64 ? State::Space : State::Number),
                    .Space => c < 48u64 ? State::Space : State::Idle,
                    .Done => State::Done
                };
            }

            func Scan(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var state = State::Idle;
                var numbers: uint64 = 0;
                var words: uint64 = 0;
                var spaces: uint64 = 0;
                var acc: uint64 = {{Literal(m.Constant)}};
                for i in 0u64..{{m.Count}}u64 {
                    let c = rng.Next() >> 56;
                    state = Step(state, c);
                    match state {
                        .Number => {
                            numbers += 1;
                            acc = acc * 31u64 + c;
                        },
                        .Word => {
                            words += 1;
                            acc = Rotl(acc, 5u32) ^ c;
                        },
                        .Space => {
                            spaces += 1;
                        },
                        .Idle => {
                            acc = acc + i;
                        },
                        .Done => {
                            state = State::Idle;
                            acc = acc ^ {{Literal(m.Constant2)}};
                        }
                    }
                }
                return acc ^ (numbers << 40) ^ (words << 20) ^ spaces;
            }
            """,
        Kind.Dispatch => $$"""
            interface Transform {
                func Apply(self: &Self, value: uint64) -> uint64;
                func Weight(self: &Self) -> uint64;
            }

            struct Rotate {
                amount: uint32;
                bias: uint64;
            }

            extend Rotate : Transform {
                func Apply(self: &Rotate, value: uint64) -> uint64 {
                    return Rotl(value, self.amount) ^ self.bias;
                }

                func Weight(self: &Rotate) -> uint64 {
                    return self.amount as uint64;
                }
            }

            struct Affine {
                multiplier: uint64;
                offset: uint64;
            }

            extend Affine : Transform {
                func Apply(self: &Affine, value: uint64) -> uint64 {
                    return value * self.multiplier + self.offset;
                }

                func Weight(self: &Affine) -> uint64 {
                    return self.multiplier;
                }
            }

            struct Fold {
                mask: uint64;
            }

            extend Fold : Transform {
                func Apply(self: &Fold, value: uint64) -> uint64 {
                    return (value ^ (value >> 29)) + (value & self.mask);
                }

                func Weight(self: &Fold) -> uint64 {
                    return self.mask >> 32;
                }
            }

            func Drive(transform: &Transform, start: uint64, steps: uint64) -> uint64 {
                var value = start;
                for i in 0u64..steps {
                    value = transform.Apply(value) + i;
                }
                return value ^ transform.Weight();
            }

            func Dispatch(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                let rotate = Rotate { amount: {{m.Shift}}u32, bias: {{Literal(m.Constant)}} };
                let affine = Affine { multiplier: {{Literal(m.Multiplier)}}, offset: {{Literal(m.Constant2)}} };
                let fold = Fold { mask: {{Literal(m.Mask)}} };
                var value = rng.Next();
                for round in 0u..{{m.Count}}u {
                    let pick = rng.Next() % 3u64;
                    if pick == 0u64 {
                        value = Drive(rotate, value, {{m.Inner}}u64);
                    } else if pick == 1u64 {
                        value = Drive(affine, value, {{m.Inner}}u64);
                    } else {
                        value = Drive(fold, value, {{m.Inner}}u64);
                    }
                }
                return value;
            }
            """,
        Kind.Tally => $$"""
            func Tally(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var system = SystemAllocator();
                let allocator: Allocator = system;
                var counts = HashMap<uint64, uint64>(allocator, HashUint64, EqualsUint64);
                for i in 0u64..{{m.Count}}u64 {
                    let key = rng.Next() % {{m.Size}}u64;
                    var count: *var uint64 = null;
                    counts.GetOrInsert(key, 0u64, @count, null) catch { else => return 0u64 };
                    *count = *count + i + 1u64;
                }
                var acc: uint64 = {{Literal(m.Constant)}};
                for key in 0u64..{{m.Size}}u64 {
                    let count = counts.Get(key) ?? 0u64;
                    if count != 0u64 {
                        acc = Fnv(acc, count) ^ key;
                    }
                }
                return acc ^ (counts.Length() as uint64);
            }
            """,
        Kind.Sorter => $$"""
            func InsertionSort(values: var uint64[..]) {
                for i in 1u..values.length {
                    let value = values[i];
                    var j = i;
                    while j > 0u && values[j - 1u] > value {
                        values[j] = values[j - 1u];
                        j -= 1u;
                    }
                    values[j] = value;
                }
            }

            func LowerBound(values: uint64[..], target: uint64) -> uint {
                var low: uint = 0;
                var high = values.length;
                while low < high {
                    let middle = low + (high - low) / 2u;
                    if values[middle] < target {
                        low = middle + 1u;
                    } else {
                        high = middle;
                    }
                }
                return low;
            }

            func SortAndSearch(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var values: uint64[{{m.Count}}] = [0u64; {{m.Count}}];
                for i in 0u..{{m.Count}}u {
                    values[i] = rng.Next() >> 2;
                }
                InsertionSort(values[..]);
                var acc: uint64 = 0;
                for query in 0u..{{m.Size}}u {
                    let target = rng.Next() >> 2;
                    acc = acc * {{Literal(m.Multiplier)}} + (LowerBound(values[..], target) as uint64);
                }
                return acc ^ values[{{m.Count / 2}}u];
            }
            """,
        Kind.Ring => $$"""
            const Capacity: uint = {{m.Size}};

            struct Ring {
                items: uint64[Capacity];
                head: uint;
                count: uint;
            }

            extend Ring {
                func Ring() -> Ring {
                    return Ring { items: [0u64; Capacity], head: 0, count: 0 };
                }

                func Push(self: &var Ring, value: uint64) {
                    self.items[(self.head + self.count) % Capacity] = value;
                    if self.count < Capacity {
                        self.count += 1;
                    } else {
                        self.head = (self.head + 1) % Capacity;
                    }
                }

                func Pop(self: &var Ring) -> uint64 {
                    if self.count == 0 {
                        return 0u64;
                    }
                    let value = self.items[self.head];
                    self.head = (self.head + 1) % Capacity;
                    self.count -= 1;
                    return value;
                }

                func WeightedSum(self: &Ring) -> uint64 {
                    var sum: uint64 = 0;
                    for i in 0u..self.count {
                        sum += self.items[(self.head + i) % Capacity] * ((i + 1u) as uint64);
                    }
                    return sum;
                }
            }

            func Churn(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var ring = Ring();
                var acc: uint64 = 0;
                for i in 0u..{{m.Count}}u {
                    let value = rng.Next();
                    if (value & 3u64) == 0u64 {
                        acc = acc ^ ring.Pop();
                    } else {
                        ring.Push(value ^ acc);
                    }
                }
                return acc ^ ring.WeightedSum() ^ (ring.count as uint64);
            }
            """,
        Kind.Checksum => $$"""
            func FillTable(table: var uint64[..]) {
                for i in 0u..256u {
                    var crc = i as uint64;
                    for bit in 0u..8u {
                        if (crc & 1u64) == 1u64 {
                            crc = (crc >> 1) ^ {{Literal(m.Constant2)}};
                        } else {
                            crc = crc >> 1;
                        }
                    }
                    table[i] = crc;
                }
            }

            func Update(table: uint64[..], crc: uint64, value: uint64) -> uint64 {
                return table[((crc ^ value) & 255u64) as uint] ^ (crc >> 8);
            }

            func Checksum(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var table: uint64[256] = [0u64; 256];
                FillTable(table[..]);
                var crc: uint64 = 0xFFFFFFFFFFFFFFFFu64;
                for i in 0u..{{m.Count}}u {
                    crc = Update(table[..], crc, rng.Next() >> 56);
                }
                return (crc ^ 0xFFFFFFFFFFFFFFFFu64) ^ Rotl(crc, {{m.Shift}}u32);
            }
            """,
        Kind.Text => $$"""
            func AppendDecimal(buffer: &var Vector<uint8>, value: uint64) {
                if value == 0u64 {
                    buffer.Push(48u8) catch { else => {} };
                    return;
                }
                var digits: uint8[20] = [0u8; 20];
                var count: uint = 0;
                var rest = value;
                while rest > 0u64 {
                    digits[count] = (rest % 10u64) as uint8 + 48u8;
                    rest = rest / 10u64;
                    count += 1;
                }
                while count > 0u {
                    count -= 1;
                    buffer.Push(digits[count]) catch { else => {} };
                }
            }

            func Render(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var system = SystemAllocator();
                let allocator: Allocator = system;
                var buffer = Vector<uint8>(allocator);
                for i in 0u..{{m.Count}}u {
                    AppendDecimal(buffer, rng.Next() % {{m.Size}}u64);
                    buffer.Push(44u8) catch { else => {} };
                }
                var total: uint64 = 0;
                var current: uint64 = 0;
                var fields: uint64 = 0;
                let bytes = buffer.AsSlice();
                for i in 0u..bytes.length {
                    let b = bytes[i];
                    if b == 44u8 {
                        total = total * {{Literal(m.Multiplier)}} + current;
                        current = 0;
                        fields += 1;
                    } else {
                        current = current * 10u64 + ((b - 48u8) as uint64);
                    }
                }
                return total ^ (fields << 32) ^ (bytes.length as uint64);
            }
            """,
        Kind.Matrix => $$"""
            const Size: uint = {{m.Size}};
            const Cells: uint = {{m.Size * m.Size}};

            struct Matrix {
                cells: uint64[Cells];
            }

            extend Matrix {
                func Zero() -> Matrix {
                    return Matrix { cells: [0u64; Cells] };
                }

                func Identity() -> Matrix {
                    var result = Matrix::Zero();
                    for i in 0u..Size {
                        result.cells[i * Size + i] = 1u64;
                    }
                    return result;
                }

                func Multiply(self: &Matrix, other: &Matrix) -> Matrix {
                    var result = Matrix::Zero();
                    for row in 0u..Size {
                        for column in 0u..Size {
                            var sum: uint64 = 0;
                            for k in 0u..Size {
                                sum += self.cells[row * Size + k] * other.cells[k * Size + column];
                            }
                            result.cells[row * Size + column] = sum;
                        }
                    }
                    return result;
                }

                func Trace(self: &Matrix) -> uint64 {
                    var sum: uint64 = 0;
                    for i in 0u..Size {
                        sum += self.cells[i * Size + i];
                    }
                    return sum;
                }
            }

            func Power(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var base = Matrix::Zero();
                for i in 0u..Cells {
                    base.cells[i] = rng.Next();
                }
                var result = Matrix::Identity();
                for step in 0u..{{m.Count}}u {
                    result = result.Multiply(base);
                    base.cells[step % Cells] = base.cells[step % Cells] ^ result.cells[0];
                }
                return result.Trace() ^ result.cells[Cells - 1u];
            }
            """,
        Kind.Forest => $$"""
            const Nodes: uint = {{m.Size}};

            struct Forest {
                parent: uint[Nodes];
                rank: uint32[Nodes];
            }

            extend Forest {
                func Forest() -> Forest {
                    var forest = Forest { parent: [0u; Nodes], rank: [0u32; Nodes] };
                    for i in 0u..Nodes {
                        forest.parent[i] = i;
                    }
                    return forest;
                }

                func Find(self: &var Forest, node: uint) -> uint {
                    var root = node;
                    while self.parent[root] != root {
                        self.parent[root] = self.parent[self.parent[root]];
                        root = self.parent[root];
                    }
                    return root;
                }

                func Unite(self: &var Forest, a: uint, b: uint) -> bool {
                    var rootA = self.Find(a);
                    var rootB = self.Find(b);
                    if rootA == rootB {
                        return false;
                    }
                    if self.rank[rootA] < self.rank[rootB] {
                        let swap = rootA;
                        rootA = rootB;
                        rootB = swap;
                    }
                    self.parent[rootB] = rootA;
                    if self.rank[rootA] == self.rank[rootB] {
                        self.rank[rootA] += 1u32;
                    }
                    return true;
                }
            }

            func Connect(seed: uint64) -> uint64 {
                var rng = Rng(seed);
                var forest = Forest();
                var merges: uint64 = 0;
                for i in 0u..{{m.Inner}}u {
                    let a = (rng.Next() % {{m.Size}}u64) as uint;
                    let b = (rng.Next() % {{m.Size}}u64) as uint;
                    if forest.Unite(a, b) {
                        merges += 1;
                    }
                }
                var acc: uint64 = 0;
                for i in 0u..Nodes {
                    acc = acc * 31u64 + (forest.Find(i) as uint64);
                }
                return acc ^ merges;
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };
}
