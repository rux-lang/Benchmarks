namespace Runner.Generator;

/// <summary>The ten kinds of module. Each module is one kind, rotating, so every group of ten has one of each.</summary>
enum Kind { Vector, Machine, Dispatch, Tally, Sorter, Ring, Checksum, Text, Matrix, Forest }

/// <summary>SplitMix64, the generator the apps use, here for the generator's own choices.</summary>
sealed class Random64(ulong seed)
{
    ulong state = seed;

    public ulong Next()
    {
        state += 0x9E3779B97F4A7C15;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [low, high].</summary>
    public int Range(int low, int high) => low + (int)(Next() % (ulong)(high - low + 1));

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count - 1)];

    public bool Chance(int percent) => Range(1, 100) <= percent;

    /// <summary>A full-width constant, odd so it is also a usable multiplier.</summary>
    public ulong Constant() => Next() | 1;
}

/// <summary>A call from one module into another: Run mixes the dependency's Mix of its running value plus Offset.</summary>
sealed record Dependency(ModuleSpec Module, ulong Offset);

/// <summary>
/// One module: a kind-specific piece of code (a template filled in with this module's constants),
/// two or three generated kernel functions, and the public Mix and Run functions.
/// </summary>
sealed class ModuleSpec
{
    public required int Index { get; init; }
    public required Kind Kind { get; init; }
    /// <summary>The group (folder, package or namespace) the module belongs to.</summary>
    public int Group { get; set; }
    /// <summary>The source file's name without extension: the module's own name, or PartNNNN when it shares a file.</summary>
    public string File { get; set; } = "";
    /// <summary>True when the file holds several modules, each in its own namespace.</summary>
    public bool SharesFile { get; set; }
    /// <summary>Kind and index, such as Ring016: the file, class and module name.</summary>
    public string Name => $"{Kind}{Index:000}";
    /// <summary>Main loop count of the template.</summary>
    public required int Count { get; init; }
    /// <summary>Kind-specific size: keys, capacity, matrix size, nodes, rounds, queries or number range.</summary>
    public required int Size { get; init; }
    /// <summary>Kind-specific second count: inner steps or edges.</summary>
    public required int Inner { get; init; }
    public required int Shift { get; init; }
    public required ulong Salt { get; init; }
    public required ulong Multiplier { get; init; }
    public required ulong Constant { get; init; }
    public required ulong Constant2 { get; init; }
    /// <summary>The Fold transform's mask (Dispatch).</summary>
    public ulong Mask => Salt ^ Constant;
    public required ulong MixKey1 { get; init; }
    public required ulong MixKey2 { get; init; }
    public required List<Kernel> Kernels { get; init; }
    public required List<Dependency> Dependencies { get; init; }
    /// <summary>The module the incremental-rebuild benchmark edits (a leaf: only Main calls it).</summary>
    public bool Editable { get; set; }

    public bool UsesRotl => Kind is Kind.Machine or Kind.Dispatch or Kind.Checksum || Kernels.Any(k => k.UsesRotl);
    public bool UsesFnv => Kind == Kind.Tally;

    public string Description => Kind switch
    {
        Kind.Vector => "fixed-point 3D vector arithmetic: sums, cross products and dot products of random vectors.",
        Kind.Machine => "a character-class state machine that scans random bytes and counts numbers, words and spaces.",
        Kind.Dispatch => "three transforms behind one interface, picked at random and applied through dynamic dispatch.",
        Kind.Tally => "counts random keys in a hash map and folds the counts in key order.",
        Kind.Sorter => "insertion sort of random values, then binary searches for random targets.",
        Kind.Ring => "a fixed-capacity ring buffer of recent values with a weighted sum over its contents.",
        Kind.Checksum => "a table-driven 64-bit CRC over random bytes.",
        Kind.Text => "writes random numbers as decimal text, then parses the text back.",
        Kind.Matrix => "repeated products of small square matrices.",
        Kind.Forest => "union-find with path halving and union by rank over random edges.",
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>A module's code depends only on its index, so module N is the same in every project that has it.</summary>
    public static ModuleSpec Create(int index, IReadOnlyList<ModuleSpec> earlier)
    {
        var random = new Random64(0x5EED_0000_0000_0000UL + (ulong)index);
        var kind = (Kind)((index - 1) % 10);
        (int count, int size, int inner) = kind switch
        {
            Kind.Vector => (random.Range(16, 48), 0, 0),
            Kind.Machine => (random.Range(120, 320), 0, 0),
            Kind.Dispatch => (random.Range(4, 9), 0, random.Range(8, 24)),
            Kind.Tally => (random.Range(150, 400), random.Range(13, 61), 0),
            Kind.Sorter => (random.Range(48, 128), random.Range(12, 32), 0),
            Kind.Ring => (random.Range(60, 160), random.Pick([8, 16, 32]), 0),
            Kind.Checksum => (random.Range(64, 192), 0, 0),
            Kind.Text => (random.Range(16, 48), random.Pick([1000, 10000, 100000, 1000000]), 0),
            Kind.Matrix => (random.Range(4, 10), random.Range(3, 5), 0),
            Kind.Forest => (0, random.Range(32, 96), 0),
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (kind == Kind.Forest)
            inner = size + random.Range(0, size);
        var module = new ModuleSpec
        {
            Index = index,
            Kind = kind,
            Count = count,
            Size = size,
            Inner = inner,
            Shift = random.Range(1, 63),
            Salt = random.Constant(),
            Multiplier = kind == Kind.Text ? (ulong)random.Pick([31, 131, 1031]) : random.Constant(),
            Constant = random.Constant(),
            // A CRC polynomial needs its top bit set; elsewhere any constant will do.
            Constant2 = random.Constant() | (kind == Kind.Checksum ? 1UL << 63 : 0),
            MixKey1 = random.Constant(),
            MixKey2 = random.Constant(),
            Kernels = [],
            Dependencies = [],
        };
        int kernels = random.Range(2, 3);
        for (int k = 0; k < kernels; k++)
            module.Kernels.Add(KernelBuilder.Build("ABC"[k].ToString(), random));
        // Up to three distinct modules among the twenty before this one: a DAG, like an import graph.
        var candidates = earlier.Where(m => m.Index >= index - 20).ToList();
        int dependencies = Math.Min(candidates.Count, random.Range(1, 3));
        for (int d = 0; d < dependencies; d++)
        {
            var target = random.Pick(candidates);
            candidates.Remove(target);
            module.Dependencies.Add(new Dependency(target, random.Constant()));
        }
        module.Dependencies.Sort((a, b) => a.Module.Index.CompareTo(b.Module.Index));
        return module;
    }
}

/// <summary>
/// A generated project: Main, the shared helpers, and the modules, in source files, in groups.
/// A file holds one module, or several in a large project (each in its own namespace), and Main
/// calls one runner per group, which runs the group's modules in order (a Java method could not
/// hold ten thousand calls). Committed projects are in the repository; the others are generated
/// on demand.
/// </summary>
sealed record ProjectSpec(string Name, string Description, IReadOnlyList<ModuleSpec> Modules, bool Committed, int? TimeoutSeconds)
{
    public IEnumerable<int> Groups => Modules.Select(m => m.Group).Distinct().Order();

    public IEnumerable<ModuleSpec> InGroup(int group) => Modules.Where(m => m.Group == group);

    /// <summary>The source files in order, each with its modules.</summary>
    public IEnumerable<IGrouping<string, ModuleSpec>> Files => Modules.GroupBy(m => m.File);

    public static ProjectSpec Create(string name, int moduleCount, int modulesPerFile, int filesPerGroup, bool committed = true, int? timeoutSeconds = null)
    {
        var modules = new List<ModuleSpec>();
        for (int i = 1; i <= moduleCount; i++)
        {
            var module = ModuleSpec.Create(i, modules);
            int file = (i - 1) / modulesPerFile;
            module.Group = file / filesPerGroup;
            module.File = modulesPerFile == 1 ? module.Name : $"Part{file + 1:0000}";
            module.SharesFile = modulesPerFile > 1;
            modules.Add(module);
        }
        // The last module is a leaf, since dependencies only point backwards.
        if (moduleCount > 1)
            modules[^1].Editable = true;
        int files = (moduleCount + modulesPerFile - 1) / modulesPerFile;
        int groups = (files + filesPerGroup - 1) / filesPerGroup;
        string description = moduleCount == 1
            ? "The smallest project with the same layout, dependencies and build setup as the others: Main, the shared helpers and one module."
            : $"{moduleCount} generated modules{(modulesPerFile > 1 ? $" in {files} files of {modulesPerFile}" : "")}, in {groups} groups; each module calls up to three earlier ones, and Main runs every module and prints an FNV-1a hash of the results.";
        return new ProjectSpec(name, description, modules, committed, timeoutSeconds);
    }

    /// <summary>
    /// Large, about a million lines per language, has a thousand files of ten modules each. It is
    /// not committed: `generate` and `compile` write it when it is missing or out of date, and its
    /// builds may take far longer than an app's.
    /// </summary>
    public static readonly IReadOnlyList<ProjectSpec> All =
    [
        Create("Baseline", 1, modulesPerFile: 1, filesPerGroup: 1),
        Create("Large", 10000, modulesPerFile: 10, filesPerGroup: 100, committed: false, timeoutSeconds: 4 * 3600),
    ];

    public static ProjectSpec? Find(string name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
