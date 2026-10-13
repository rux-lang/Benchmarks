namespace Runner.Generator;

// A tiny language-neutral syntax tree for the generated kernel functions. Every value is an
// unsigned 64-bit integer with wrapping arithmetic, so every language computes the same bits.

abstract record Expr;
sealed record Var(string Name) : Expr;
sealed record Lit(ulong Value) : Expr;
/// <summary>Op is one of + - * ^ &amp; |.</summary>
sealed record Binary(char Op, Expr Left, Expr Right) : Expr;
sealed record ShiftBy(bool Left, Expr Value, int Amount) : Expr;
/// <summary>A call of the shared Rotl helper.</summary>
sealed record Rotate(Expr Value, int Amount) : Expr;
sealed record Modulo(Expr Value, ulong Divisor) : Expr;

/// <summary>Compares (Value &amp; Mask) with Limit. Both sides are below 2^16, so signed and unsigned comparisons agree.</summary>
sealed record Compare(string Op, Expr Value, ulong Mask, ulong Limit);

abstract record Stmt;
sealed record Declare(string Name, Expr Value, bool Mutable) : Stmt;
sealed record Assign(string Name, Expr Value) : Stmt;
sealed record If(Compare Condition, List<Stmt> Then, List<Stmt> Else) : Stmt;
sealed record Loop(string Variable, int Count, List<Stmt> Body) : Stmt;
sealed record Return(Expr Value) : Stmt;

/// <summary>A generated function of two parameters, x and y.</summary>
sealed record Kernel(string Suffix, List<Stmt> Body)
{
    public bool UsesRotl => Body.Any(UsesRotlIn);

    static bool UsesRotlIn(Stmt s) => s switch
    {
        Declare d => Uses(d.Value),
        Assign a => Uses(a.Value),
        If i => Uses(i.Condition.Value) || i.Then.Any(UsesRotlIn) || i.Else.Any(UsesRotlIn),
        Loop l => l.Body.Any(UsesRotlIn),
        Return r => Uses(r.Value),
        _ => false,
    };

    static bool Uses(Expr e) => e switch
    {
        Rotate => true,
        Binary b => Uses(b.Left) || Uses(b.Right),
        ShiftBy s => Uses(s.Value),
        Modulo m => Uses(m.Value),
        _ => false,
    };

    public static bool Mentions(IEnumerable<Stmt> body, string name) => body.Any(s => Mentions(s, name));

    static bool Mentions(Stmt s, string name) => s switch
    {
        Declare d => Mentions(d.Value, name),
        Assign a => a.Name == name || Mentions(a.Value, name),
        If i => Mentions(i.Condition.Value, name) || Mentions(i.Then, name) || Mentions(i.Else, name),
        Loop l => Mentions(l.Body, name),
        Return r => Mentions(r.Value, name),
        _ => false,
    };

    static bool Mentions(Expr e, string name) => e switch
    {
        Var v => v.Name == name,
        Binary b => Mentions(b.Left, name) || Mentions(b.Right, name),
        ShiftBy s => Mentions(s.Value, name),
        Rotate r => Mentions(r.Value, name),
        Modulo m => Mentions(m.Value, name),
        _ => false,
    };
}

/// <summary>
/// Builds a kernel: two mutable locals seeded from the parameters, a loop that updates them (with
/// a branch half of the time), one or two derived locals, and a return that uses every local, so
/// no language reports an unused variable.
/// </summary>
static class KernelBuilder
{
    static readonly char[] Ops = ['+', '-', '*', '^', '^', '&', '|', '+', '*'];

    public static Kernel Build(string suffix, Random64 random)
    {
        var body = new List<Stmt>
        {
            new Declare("a", Using(random, "x", ["x", "y"], 1), Mutable: true),
            new Declare("b", Using(random, "y", ["y", "a"], 1), Mutable: true),
        };
        var loopBody = new List<Stmt>
        {
            Update(random, "a", ["a", "b", "i", "x"], 2),
            Update(random, "b", ["a", "b", "i", "y"], 2),
        };
        if (random.Chance(60))
            loopBody.Add(new If(Condition(random, ["a", "b", "i"]),
                [Update(random, "a", ["a", "b", "i"], 2)],
                [Update(random, "b", ["a", "b", "i"], 2)]));
        if (random.Chance(40))
        {
            string name = random.Chance(50) ? "a" : "b";
            loopBody.Add(Update(random, name, ["a", "b", "x", "y"], 1));
        }
        body.Add(new Loop("i", random.Range(3, 9), loopBody));
        if (random.Chance(50))
            body.Add(new If(Condition(random, ["a", "b", "x"]),
                [Update(random, "a", ["a", "b", "x", "y"], 2)],
                [Update(random, "b", ["a", "b", "x", "y"], 1)]));
        body.Add(new Declare("c", Node(random, ["a", "b", "x", "y"], 2), Mutable: false));
        Expr result = new Binary('^', new Var("c"), new Binary(random.Pick(['+', '^', '-']), new Var("a"), new Var("b")));
        if (random.Chance(50))
        {
            body.Add(new Declare("d", Node(random, ["a", "b", "c"], 2), Mutable: false));
            result = new Binary('+', result, new Var("d"));
        }
        body.Add(new Return(result));
        return new Kernel(suffix, body);
    }

    /// <summary>An assignment that reads the variable it writes, so no earlier value goes unread.</summary>
    static Assign Update(Random64 random, string name, string[] names, int depth) =>
        new(name, Using(random, name, names, depth));

    /// <summary>An expression that reads <paramref name="name"/>.</summary>
    static Expr Using(Random64 random, string name, string[] names, int depth)
    {
        Expr e = Node(random, names, depth);
        return Kernel.Mentions([new Return(e)], name) ? e : new Binary(random.Pick(Ops), new Var(name), e);
    }

    static Compare Condition(Random64 random, string[] names)
    {
        ulong mask = random.Pick([0xFFUL, 0x3FFUL, 0xFFFFUL, 0xFUL]);
        return new Compare(random.Pick(["<", ">", "==", "!="]), Node(random, names, 1), mask, (ulong)random.Range(1, (int)mask - 1));
    }

    /// <summary>An operation (never a bare variable) that always reads at least one variable, so nothing folds to a literal-only expression.</summary>
    static Expr Node(Random64 random, string[] names, int depth)
    {
        int choice = random.Range(1, 10);
        Expr operand = Gen(random, names, depth - 1);
        return choice switch
        {
            <= 6 => random.Chance(35) || operand is Var v && names.All(n => n == v.Name)
                ? new Binary(random.Pick(Ops), operand, Literal(random))
                : new Binary(random.Pick(Ops), operand, Gen(random, Others(names, operand), depth - 1)),
            7 => new ShiftBy(random.Chance(50), operand, random.Range(1, 63)),
            8 or 9 => new Rotate(operand, random.Range(1, 63)),
            _ => new Modulo(operand, (ulong)random.Range(3, 1000)),
        };
    }

    /// <summary>The names a right operand may use: not a bare left operand's variable, since a ^ a, a - a and a &amp; a fold away.</summary>
    static string[] Others(string[] names, Expr left) =>
        left is Var v ? names.Where(n => n != v.Name).ToArray() : names;

    static Expr Gen(Random64 random, string[] names, int depth) =>
        depth <= 0 || random.Chance(20) ? new Var(random.Pick(names)) : Node(random, names, depth);

    static Lit Literal(Random64 random) =>
        new(random.Chance(50) ? (ulong)random.Range(1, 255) : random.Constant());
}
