// BinaryTrees: allocates and walks many complete binary trees (single-threaded variant of the
// Computer Language Benchmarks Game program). Nodes are garbage-collected objects.
int maxDepthArgument = args.Length > 0 ? int.Parse(args[0]) : 18;

const int MinDepth = 4;
int maxDepth = Math.Max(MinDepth + 2, maxDepthArgument);
var output = new System.Text.StringBuilder();

int stretchDepth = maxDepth + 1;
output.Append($"stretch tree of depth {stretchDepth}\t check: {Node.Build(stretchDepth).Check()}\n");

Node longLived = Node.Build(maxDepth);
for (int depth = MinDepth; depth <= maxDepth; depth += 2)
{
    long iterations = 1L << (maxDepth - depth + MinDepth);
    long check = 0;
    for (long i = 0; i < iterations; i++)
        check += Node.Build(depth).Check();
    output.Append($"{iterations}\t trees of depth {depth}\t check: {check}\n");
}
output.Append($"long lived tree of depth {maxDepth}\t check: {longLived.Check()}\n");
Console.Write(output.ToString());

sealed class Node(Node? left, Node? right)
{
    readonly Node? left = left;
    readonly Node? right = right;

    public static Node Build(int depth) =>
        depth > 0 ? new Node(Build(depth - 1), Build(depth - 1)) : new Node(null, null);

    public long Check() => left is null ? 1 : 1 + left.Check() + right!.Check();
}
