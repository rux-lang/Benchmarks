using System.Text;

namespace Runner.Generator;

/// <summary>
/// CMakeLists.txt and src/: main.cpp, common.h, and a header and a source file per module
/// (src/groupN/namennn.h/.cpp) in namespace groupN::namennn. Headers declare the module's types
/// and its public functions; sources define them, with the internals in an anonymous namespace.
/// </summary>
sealed class CppEmitter : Emitter
{
    public override string Folder => "Cpp";

    protected override string Literal(ulong value) => Number(value) + "ULL";
    protected override string Rotl(string value, int amount) => $"common::rotl({value}, {amount})";
    protected override string Declare(Declare d) => $"{(d.Mutable ? "" : "const ")}uint64_t {d.Name} = {Expression(d.Value)};";
    protected override string LoopHead(Loop loop, bool variableUsed) =>
        $"for (uint64_t {loop.Variable} = 0; {loop.Variable} < {loop.Count}; ++{loop.Variable})";

    static string Namespace(ModuleSpec m) => $"group{m.Group}::{m.Name.ToLowerInvariant()}";
    static string HeaderPath(ModuleSpec m) => $"group{m.Group}/{m.File.ToLowerInvariant()}.h";

    public override IEnumerable<SourceFile> Emit(ProjectSpec project)
    {
        var cmake = new StringBuilder();
        cmake.AppendLine($"# {project.Name}: {project.Modules.Count} modules in {project.Files.Count()} pairs of a header and a source file under src/.");
        cmake.AppendLine($"# {Banner}");
        cmake.AppendLine("# Compiler flags come from the runner (bench.json cppFlags), passed when it configures the build.");
        cmake.AppendLine("cmake_minimum_required(VERSION 3.28)");
        cmake.AppendLine($"project({project.Name} LANGUAGES CXX)");
        cmake.AppendLine();
        cmake.AppendLine($"add_executable({project.Name}");
        cmake.AppendLine("    src/main.cpp");
        foreach (int group in project.Groups)
        {
            cmake.AppendLine($"    src/group{group}/group.cpp");
            foreach (string file in project.InGroup(group).Select(m => m.File).Distinct())
                cmake.AppendLine($"    src/group{group}/{file.ToLowerInvariant()}.cpp");
        }
        cmake.AppendLine(")");
        cmake.AppendLine($"target_include_directories({project.Name} PRIVATE src)");
        yield return new SourceFile("CMakeLists.txt", cmake.ToString());

        yield return new SourceFile("src/common.h", $$"""
            // Common: helpers shared by every module: a SplitMix64 generator, rotation and FNV-1a mixing.
            // {{Banner}}
            #pragma once

            #include <bit>
            #include <cstdint>

            namespace common {

            class Rng {
            public:
                explicit Rng(uint64_t seed) : state_(seed) {}

                uint64_t next() {
                    state_ += 0x9E3779B97F4A7C15ULL;
                    uint64_t z = state_;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
                    return z ^ (z >> 31);
                }

            private:
                uint64_t state_;
            };

            inline uint64_t rotl(uint64_t value, int amount) {
                return std::rotl(value, amount);
            }

            inline uint64_t fnv(uint64_t hash, uint64_t value) {
                return (hash ^ value) * 0x100000001B3ULL;
            }

            }  // namespace common

            """);

        var main = new StringBuilder();
        main.AppendLine($"// {project.Name}: runs every module once and prints an FNV-1a hash of their results.");
        main.AppendLine($"// {Banner}");
        main.AppendLine("#include <cstdint>");
        main.AppendLine("#include <cstdio>");
        main.AppendLine();
        foreach (int group in project.Groups)
            main.AppendLine($"#include \"group{group}/group.h\"");
        main.AppendLine();
        main.AppendLine("int main() {");
        main.AppendLine("    uint64_t hash = 0xCBF29CE484222325ULL;");
        foreach (int group in project.Groups)
            main.AppendLine($"    hash = group{group}::runAll(hash);");
        main.AppendLine("    std::printf(\"%016llx\\n\", static_cast<unsigned long long>(hash));");
        main.AppendLine("    return 0;");
        main.AppendLine("}");
        yield return new SourceFile("src/main.cpp", main.ToString());

        foreach (int group in project.Groups)
        {
            yield return new SourceFile($"src/group{group}/group.h", $"""
                // Group {group}: a runner that runs the group's modules in order.
                // {Banner}
                #pragma once

                #include <cstdint>

                namespace group{group} {"{"}

                // Runs every module of the group in order and mixes their results into hash.
                uint64_t runAll(uint64_t hash);

                {"}"}  // namespace group{group}

                """);
            var runner = new StringBuilder();
            runner.AppendLine($"// Group {group}: a runner that runs the group's modules in order.");
            runner.AppendLine($"// {Banner}");
            runner.AppendLine($"#include \"group{group}/group.h\"");
            runner.AppendLine();
            runner.AppendLine("#include \"common.h\"");
            foreach (string file in project.InGroup(group).Select(m => m.File).Distinct())
                runner.AppendLine($"#include \"group{group}/{file.ToLowerInvariant()}.h\"");
            runner.AppendLine();
            runner.AppendLine($"namespace group{group} {{");
            runner.AppendLine();
            runner.AppendLine("uint64_t runAll(uint64_t hash) {");
            runner.AppendLine("    uint64_t result = hash;");
            foreach (var m in project.InGroup(group))
                runner.AppendLine($"    result = common::fnv(result, {m.Name.ToLowerInvariant()}::run({m.Index}));");
            runner.AppendLine("    return result;");
            runner.AppendLine("}");
            runner.AppendLine();
            runner.AppendLine($"}}  // namespace group{group}");
            yield return new SourceFile($"src/group{group}/group.cpp", runner.ToString());
        }

        foreach (var file in project.Files)
        {
            var modules = file.ToList();
            yield return new SourceFile($"src/{HeaderPath(modules[0])}", Header(modules));
            yield return new SourceFile(ModuleFile(modules[0]), Source(modules));
        }
    }

    public override string ModuleFile(ModuleSpec m) => $"src/group{m.Group}/{m.File.ToLowerInvariant()}.cpp";

    string Header(List<ModuleSpec> modules)
    {
        var file = new StringBuilder(FileComment("//", modules));
        file.AppendLine("#pragma once");
        file.AppendLine();
        foreach (string include in modules.SelectMany(m => HeaderParts(m).Includes).Append("cstdint").Distinct().Order(StringComparer.Ordinal))
            file.AppendLine($"#include <{include}>");
        foreach (var m in modules)
        {
            string declarations = HeaderParts(m).Declarations;
            file.AppendLine();
            file.Append(ModuleComment("//", m));
            file.AppendLine($"namespace {Namespace(m)} {{");
            file.AppendLine();
            if (declarations.Length > 0)
                file.AppendLine(declarations.TrimEnd()).AppendLine();
            file.AppendLine("// Mixes x through this module's kernels.");
            file.AppendLine("uint64_t mix(uint64_t x);");
            file.AppendLine();
            file.AppendLine("// Runs this module from seed and mixes in the modules it depends on.");
            file.AppendLine("uint64_t run(uint64_t seed);");
            file.AppendLine();
            file.AppendLine($"}}  // namespace {Namespace(m)}");
        }
        return file.ToString();
    }

    string Source(List<ModuleSpec> modules)
    {
        var file = new StringBuilder(FileComment("//", modules));
        file.AppendLine($"#include \"{HeaderPath(modules[0])}\"");
        file.AppendLine();
        var includes = modules.SelectMany(m => SourceParts(m).Includes).Distinct().Order(StringComparer.Ordinal).ToList();
        if (includes.Count > 0)
        {
            foreach (string include in includes)
                file.AppendLine($"#include <{include}>");
            file.AppendLine();
        }
        file.AppendLine("#include \"common.h\"");
        foreach (string header in modules.SelectMany(m => m.Dependencies).Select(d => HeaderPath(d.Module)).Distinct()
                     .Where(h => h != HeaderPath(modules[0])))
            file.AppendLine($"#include \"{header}\"");
        foreach (var m in modules)
        {
            var (_, members, internals) = SourceParts(m);
            file.AppendLine();
            file.Append(ModuleComment("//", m));
            file.AppendLine($"namespace {Namespace(m)} {{");
            file.AppendLine();
            if (members.Length > 0)
                file.AppendLine(members.TrimEnd()).AppendLine();
            file.AppendLine("namespace {");
            file.AppendLine();
            file.AppendLine(internals.TrimEnd());
            foreach (var kernel in m.Kernels)
            {
                file.AppendLine();
                file.AppendLine($"uint64_t kernel{kernel.Suffix}(uint64_t x, uint64_t y) {{");
                Statements(file, kernel.Body, 1);
                file.AppendLine("}");
            }
            file.AppendLine();
            file.AppendLine("}  // namespace");
            file.AppendLine();
            file.AppendLine("uint64_t mix(uint64_t x) {");
            file.AppendLine($"    return {MixBody(m, k => "kernel" + k, Literal)};");
            file.AppendLine("}");
            file.AppendLine();
            file.AppendLine("uint64_t run(uint64_t seed) {");
            file.AppendLine($"    uint64_t r = {Entry(m.Kind)}(seed ^ {Literal(m.Salt)});");
            foreach (var d in m.Dependencies)
                file.AppendLine($"    r ^= {Namespace(d.Module)}::mix(r + {Literal(d.Offset)});");
            if (m.Editable)
                file.AppendLine("    r ^= 0ULL;  // bench:edit");
            file.AppendLine("    return mix(r);");
            file.AppendLine("}");
            file.AppendLine();
            file.AppendLine($"}}  // namespace {Namespace(m)}");
        }
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

    /// <summary>The standard headers and the type declarations a module's header carries.</summary>
    static (string[] Includes, string Declarations) HeaderParts(ModuleSpec m) => m.Kind switch
    {
        Kind.Vector => ([], """
            struct Vec3 {
                uint64_t x = 0;
                uint64_t y = 0;
                uint64_t z = 0;

                Vec3 add(const Vec3& other) const;
                Vec3 scale(uint64_t factor) const;
                uint64_t dot(const Vec3& other) const;
                Vec3 cross(const Vec3& other) const;
            };
            """),
        Kind.Machine => ([], """
            enum class State { Idle, Number, Word, Space, Done };

            // The state after reading character c in state.
            State step(State state, uint64_t c);
            """),
        Kind.Dispatch => ([], """
            class Transform {
            public:
                virtual ~Transform() = default;
                virtual uint64_t apply(uint64_t value) const = 0;
                virtual uint64_t weight() const = 0;
            };

            class Rotate final : public Transform {
            public:
                Rotate(int amount, uint64_t bias) : amount_(amount), bias_(bias) {}
                uint64_t apply(uint64_t value) const override;
                uint64_t weight() const override;

            private:
                int amount_;
                uint64_t bias_;
            };

            class Affine final : public Transform {
            public:
                Affine(uint64_t multiplier, uint64_t offset) : multiplier_(multiplier), offset_(offset) {}
                uint64_t apply(uint64_t value) const override;
                uint64_t weight() const override;

            private:
                uint64_t multiplier_;
                uint64_t offset_;
            };

            class Fold final : public Transform {
            public:
                explicit Fold(uint64_t mask) : mask_(mask) {}
                uint64_t apply(uint64_t value) const override;
                uint64_t weight() const override;

            private:
                uint64_t mask_;
            };
            """),
        Kind.Ring => (["array", "cstddef"], $$"""
            constexpr std::size_t Capacity = {{m.Size}};

            class Ring {
            public:
                void push(uint64_t value);
                uint64_t pop();
                uint64_t weightedSum() const;
                std::size_t size() const { return count_; }

            private:
                std::array<uint64_t, Capacity> items_{};
                std::size_t head_ = 0;
                std::size_t count_ = 0;
            };
            """),
        Kind.Matrix => (["array", "cstddef"], $$"""
            constexpr std::size_t Size = {{m.Size}};
            constexpr std::size_t Cells = Size * Size;

            struct Matrix {
                std::array<uint64_t, Cells> cells{};

                static Matrix identity();
                Matrix multiply(const Matrix& other) const;
                uint64_t trace() const;
            };
            """),
        Kind.Forest => (["array", "cstddef"], $$"""
            constexpr std::size_t Nodes = {{m.Size}};

            class Forest {
            public:
                Forest();
                std::size_t find(std::size_t node);
                bool unite(std::size_t a, std::size_t b);

            private:
                std::array<std::size_t, Nodes> parent_{};
                std::array<uint32_t, Nodes> rank_{};
            };
            """),
        _ => ([], ""),
    };

    /// <summary>The standard headers a source needs, the definitions of its header's members, and its internal functions.</summary>
    (string[] Includes, string Members, string Internals) SourceParts(ModuleSpec m) => m.Kind switch
    {
        Kind.Vector => ([], """
            Vec3 Vec3::add(const Vec3& other) const {
                return {x + other.x, y + other.y, z + other.z};
            }

            Vec3 Vec3::scale(uint64_t factor) const {
                return {x * factor, y * factor, z * factor};
            }

            uint64_t Vec3::dot(const Vec3& other) const {
                return x * other.x + y * other.y + z * other.z;
            }

            Vec3 Vec3::cross(const Vec3& other) const {
                return {y * other.z - z * other.y, z * other.x - x * other.z, x * other.y - y * other.x};
            }
            """, $$"""
            uint64_t simulate(uint64_t seed) {
                common::Rng rng(seed);
                Vec3 sum;
                for (int i = 0; i < {{m.Count}}; ++i) {
                    const uint64_t x = rng.next();
                    const uint64_t y = rng.next();
                    const uint64_t z = rng.next();
                    const Vec3 v{x, y, z};
                    const Vec3 w = v.cross(sum).add(v.scale({{Literal(m.Multiplier)}}));
                    sum = sum.add(w);
                    if ((sum.dot(v) & 1) == 1) {
                        sum = sum.scale({{Literal(m.Constant)}});
                    }
                }
                return sum.dot(sum) ^ sum.x;
            }
            """),
        Kind.Machine => ([], """
            State step(State state, uint64_t c) {
                switch (state) {
                    case State::Idle:
                        return c < 48 ? State::Space : c < 58 ? State::Number : State::Word;
                    case State::Number:
                        return c >= 48 && c < 58 ? State::Number : c == 0 ? State::Done : State::Space;
                    case State::Word:
                        return c >= 58 ? State::Word : c < 48 ? State::Space : State::Number;
                    case State::Space:
                        return c < 48 ? State::Space : State::Idle;
                    case State::Done:
                        break;
                }
                return State::Done;
            }
            """, $$"""
            uint64_t scan(uint64_t seed) {
                common::Rng rng(seed);
                State state = State::Idle;
                uint64_t numbers = 0;
                uint64_t words = 0;
                uint64_t spaces = 0;
                uint64_t acc = {{Literal(m.Constant)}};
                for (uint64_t i = 0; i < {{m.Count}}; ++i) {
                    const uint64_t c = rng.next() >> 56;
                    state = step(state, c);
                    switch (state) {
                        case State::Number:
                            ++numbers;
                            acc = acc * 31 + c;
                            break;
                        case State::Word:
                            ++words;
                            acc = common::rotl(acc, 5) ^ c;
                            break;
                        case State::Space:
                            ++spaces;
                            break;
                        case State::Idle:
                            acc += i;
                            break;
                        case State::Done:
                            state = State::Idle;
                            acc ^= {{Literal(m.Constant2)}};
                            break;
                    }
                }
                return acc ^ (numbers << 40) ^ (words << 20) ^ spaces;
            }
            """),
        Kind.Dispatch => ([], """
            uint64_t Rotate::apply(uint64_t value) const {
                return common::rotl(value, amount_) ^ bias_;
            }

            uint64_t Rotate::weight() const {
                return static_cast<uint64_t>(amount_);
            }

            uint64_t Affine::apply(uint64_t value) const {
                return value * multiplier_ + offset_;
            }

            uint64_t Affine::weight() const {
                return multiplier_;
            }

            uint64_t Fold::apply(uint64_t value) const {
                return (value ^ (value >> 29)) + (value & mask_);
            }

            uint64_t Fold::weight() const {
                return mask_ >> 32;
            }
            """, $$"""
            uint64_t drive(const Transform& transform, uint64_t start, uint64_t steps) {
                uint64_t value = start;
                for (uint64_t i = 0; i < steps; ++i) {
                    value = transform.apply(value) + i;
                }
                return value ^ transform.weight();
            }

            uint64_t dispatch(uint64_t seed) {
                common::Rng rng(seed);
                const Rotate rotate({{m.Shift}}, {{Literal(m.Constant)}});
                const Affine affine({{Literal(m.Multiplier)}}, {{Literal(m.Constant2)}});
                const Fold fold({{Literal(m.Mask)}});
                uint64_t value = rng.next();
                for (int round = 0; round < {{m.Count}}; ++round) {
                    switch (rng.next() % 3) {
                        case 0:
                            value = drive(rotate, value, {{m.Inner}});
                            break;
                        case 1:
                            value = drive(affine, value, {{m.Inner}});
                            break;
                        default:
                            value = drive(fold, value, {{m.Inner}});
                            break;
                    }
                }
                return value;
            }
            """),
        Kind.Tally => (["unordered_map"], "", $$"""
            uint64_t tally(uint64_t seed) {
                common::Rng rng(seed);
                std::unordered_map<uint64_t, uint64_t> counts;
                for (uint64_t i = 0; i < {{m.Count}}; ++i) {
                    counts[rng.next() % {{m.Size}}] += i + 1;
                }
                uint64_t acc = {{Literal(m.Constant)}};
                for (uint64_t key = 0; key < {{m.Size}}; ++key) {
                    if (const auto found = counts.find(key); found != counts.end()) {
                        acc = common::fnv(acc, found->second) ^ key;
                    }
                }
                return acc ^ counts.size();
            }
            """),
        Kind.Sorter => (["array", "cstddef", "span"], "", $$"""
            void insertionSort(std::span<uint64_t> values) {
                for (std::size_t i = 1; i < values.size(); ++i) {
                    const uint64_t value = values[i];
                    std::size_t j = i;
                    while (j > 0 && values[j - 1] > value) {
                        values[j] = values[j - 1];
                        --j;
                    }
                    values[j] = value;
                }
            }

            std::size_t lowerBound(std::span<const uint64_t> values, uint64_t target) {
                std::size_t low = 0;
                std::size_t high = values.size();
                while (low < high) {
                    const std::size_t middle = low + (high - low) / 2;
                    if (values[middle] < target) {
                        low = middle + 1;
                    } else {
                        high = middle;
                    }
                }
                return low;
            }

            uint64_t sortAndSearch(uint64_t seed) {
                common::Rng rng(seed);
                std::array<uint64_t, {{m.Count}}> values{};
                for (uint64_t& value : values) {
                    value = rng.next() >> 2;
                }
                insertionSort(values);
                uint64_t acc = 0;
                for (int query = 0; query < {{m.Size}}; ++query) {
                    const uint64_t target = rng.next() >> 2;
                    acc = acc * {{Literal(m.Multiplier)}} + lowerBound(values, target);
                }
                return acc ^ values[{{m.Count / 2}}];
            }
            """),
        Kind.Ring => ([], """
            void Ring::push(uint64_t value) {
                items_[(head_ + count_) % Capacity] = value;
                if (count_ < Capacity) {
                    ++count_;
                } else {
                    head_ = (head_ + 1) % Capacity;
                }
            }

            uint64_t Ring::pop() {
                if (count_ == 0) {
                    return 0;
                }
                const uint64_t value = items_[head_];
                head_ = (head_ + 1) % Capacity;
                --count_;
                return value;
            }

            uint64_t Ring::weightedSum() const {
                uint64_t sum = 0;
                for (std::size_t i = 0; i < count_; ++i) {
                    sum += items_[(head_ + i) % Capacity] * (i + 1);
                }
                return sum;
            }
            """, $$"""
            uint64_t churn(uint64_t seed) {
                common::Rng rng(seed);
                Ring ring;
                uint64_t acc = 0;
                for (int i = 0; i < {{m.Count}}; ++i) {
                    const uint64_t value = rng.next();
                    if ((value & 3) == 0) {
                        acc ^= ring.pop();
                    } else {
                        ring.push(value ^ acc);
                    }
                }
                return acc ^ ring.weightedSum() ^ ring.size();
            }
            """),
        Kind.Checksum => (["array", "cstddef"], "", $$"""
            using Table = std::array<uint64_t, 256>;

            void fillTable(Table& table) {
                for (std::size_t i = 0; i < table.size(); ++i) {
                    uint64_t crc = i;
                    for (int bit = 0; bit < 8; ++bit) {
                        crc = (crc & 1) == 1 ? (crc >> 1) ^ {{Literal(m.Constant2)}} : crc >> 1;
                    }
                    table[i] = crc;
                }
            }

            uint64_t update(const Table& table, uint64_t crc, uint64_t value) {
                return table[(crc ^ value) & 255] ^ (crc >> 8);
            }

            uint64_t checksum(uint64_t seed) {
                common::Rng rng(seed);
                Table table{};
                fillTable(table);
                uint64_t crc = ~0ULL;
                for (int i = 0; i < {{m.Count}}; ++i) {
                    crc = update(table, crc, rng.next() >> 56);
                }
                return ~crc ^ common::rotl(crc, {{m.Shift}});
            }
            """),
        Kind.Text => (["string"], "", $$"""
            void appendDecimal(std::string& buffer, uint64_t value) {
                if (value == 0) {
                    buffer.push_back('0');
                    return;
                }
                char digits[20];
                int count = 0;
                for (uint64_t rest = value; rest > 0; rest /= 10) {
                    digits[count++] = static_cast<char>('0' + rest % 10);
                }
                while (count > 0) {
                    buffer.push_back(digits[--count]);
                }
            }

            uint64_t render(uint64_t seed) {
                common::Rng rng(seed);
                std::string buffer;
                for (int i = 0; i < {{m.Count}}; ++i) {
                    appendDecimal(buffer, rng.next() % {{m.Size}});
                    buffer.push_back(',');
                }
                uint64_t total = 0;
                uint64_t current = 0;
                uint64_t fields = 0;
                for (const char c : buffer) {
                    if (c == ',') {
                        total = total * {{m.Multiplier}} + current;
                        current = 0;
                        ++fields;
                    } else {
                        current = current * 10 + static_cast<uint64_t>(c - '0');
                    }
                }
                return total ^ (fields << 32) ^ buffer.size();
            }
            """),
        Kind.Matrix => ([], """
            Matrix Matrix::identity() {
                Matrix result;
                for (std::size_t i = 0; i < Size; ++i) {
                    result.cells[i * Size + i] = 1;
                }
                return result;
            }

            Matrix Matrix::multiply(const Matrix& other) const {
                Matrix result;
                for (std::size_t row = 0; row < Size; ++row) {
                    for (std::size_t column = 0; column < Size; ++column) {
                        uint64_t sum = 0;
                        for (std::size_t k = 0; k < Size; ++k) {
                            sum += cells[row * Size + k] * other.cells[k * Size + column];
                        }
                        result.cells[row * Size + column] = sum;
                    }
                }
                return result;
            }

            uint64_t Matrix::trace() const {
                uint64_t sum = 0;
                for (std::size_t i = 0; i < Size; ++i) {
                    sum += cells[i * Size + i];
                }
                return sum;
            }
            """, $$"""
            uint64_t power(uint64_t seed) {
                common::Rng rng(seed);
                Matrix base;
                for (uint64_t& cell : base.cells) {
                    cell = rng.next();
                }
                Matrix result = Matrix::identity();
                for (std::size_t step = 0; step < {{m.Count}}; ++step) {
                    result = result.multiply(base);
                    base.cells[step % Cells] ^= result.cells[0];
                }
                return result.trace() ^ result.cells[Cells - 1];
            }
            """),
        Kind.Forest => (["utility"], """
            Forest::Forest() {
                for (std::size_t i = 0; i < Nodes; ++i) {
                    parent_[i] = i;
                }
            }

            std::size_t Forest::find(std::size_t node) {
                std::size_t root = node;
                while (parent_[root] != root) {
                    parent_[root] = parent_[parent_[root]];
                    root = parent_[root];
                }
                return root;
            }

            bool Forest::unite(std::size_t a, std::size_t b) {
                std::size_t rootA = find(a);
                std::size_t rootB = find(b);
                if (rootA == rootB) {
                    return false;
                }
                if (rank_[rootA] < rank_[rootB]) {
                    std::swap(rootA, rootB);
                }
                parent_[rootB] = rootA;
                if (rank_[rootA] == rank_[rootB]) {
                    ++rank_[rootA];
                }
                return true;
            }
            """, $$"""
            uint64_t connect(uint64_t seed) {
                common::Rng rng(seed);
                Forest forest;
                uint64_t merges = 0;
                for (int i = 0; i < {{m.Inner}}; ++i) {
                    const std::size_t a = rng.next() % Nodes;
                    const std::size_t b = rng.next() % Nodes;
                    if (forest.unite(a, b)) {
                        ++merges;
                    }
                }
                uint64_t acc = 0;
                for (std::size_t i = 0; i < Nodes; ++i) {
                    acc = acc * 31 + forest.find(i);
                }
                return acc ^ merges;
            }
            """),
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };
}
