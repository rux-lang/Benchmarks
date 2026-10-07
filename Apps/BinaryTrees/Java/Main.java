// BinaryTrees: allocates and walks many complete binary trees (single-threaded variant of the
// Computer Language Benchmarks Game program). Nodes are garbage-collected objects.
public final class Main {
    static final int MIN_DEPTH = 4;

    public static void main(String[] args) {
        int maxDepthArgument = args.length > 0 ? Integer.parseInt(args[0]) : 18;

        int maxDepth = Math.max(MIN_DEPTH + 2, maxDepthArgument);
        StringBuilder output = new StringBuilder();

        int stretchDepth = maxDepth + 1;
        output.append("stretch tree of depth ").append(stretchDepth)
            .append("\t check: ").append(Node.build(stretchDepth).check()).append('\n');

        Node longLived = Node.build(maxDepth);
        for (int depth = MIN_DEPTH; depth <= maxDepth; depth += 2) {
            long iterations = 1L << (maxDepth - depth + MIN_DEPTH);
            long check = 0;
            for (long i = 0; i < iterations; i++)
                check += Node.build(depth).check();
            output.append(iterations).append("\t trees of depth ").append(depth)
                .append("\t check: ").append(check).append('\n');
        }
        output.append("long lived tree of depth ").append(maxDepth)
            .append("\t check: ").append(longLived.check()).append('\n');
        System.out.print(output);
        System.out.flush();
    }

    static final class Node {
        final Node left;
        final Node right;

        Node(Node left, Node right) {
            this.left = left;
            this.right = right;
        }

        static Node build(int depth) {
            return depth > 0 ? new Node(build(depth - 1), build(depth - 1)) : new Node(null, null);
        }

        long check() {
            return left == null ? 1 : 1 + left.check() + right.check();
        }
    }
}
