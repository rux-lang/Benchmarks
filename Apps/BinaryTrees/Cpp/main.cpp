// BinaryTrees: allocates and walks many complete binary trees (single-threaded variant of the
// Computer Language Benchmarks Game program). Nodes use new and delete.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstdlib>

struct Node {
    Node* left;
    Node* right;
};

static Node* Build(int depth) {
    if (depth > 0) return new Node{Build(depth - 1), Build(depth - 1)};
    return new Node{nullptr, nullptr};
}

static int64_t Check(const Node* node) {
    return node->left == nullptr ? 1 : 1 + Check(node->left) + Check(node->right);
}

static void Free(Node* node) {
    if (node->left != nullptr) {
        Free(node->left);
        Free(node->right);
    }
    delete node;
}

int main(int argc, char** argv) {
    int maxDepthArgument = argc > 1 ? std::atoi(argv[1]) : 18;

    const int minDepth = 4;
    int maxDepth = std::max(minDepth + 2, maxDepthArgument);

    int stretchDepth = maxDepth + 1;
    Node* stretch = Build(stretchDepth);
    std::printf("stretch tree of depth %d\t check: %lld\n", stretchDepth, static_cast<long long>(Check(stretch)));
    Free(stretch);

    Node* longLived = Build(maxDepth);
    for (int depth = minDepth; depth <= maxDepth; depth += 2) {
        int64_t iterations = int64_t{1} << (maxDepth - depth + minDepth);
        int64_t check = 0;
        for (int64_t i = 0; i < iterations; i++) {
            Node* tree = Build(depth);
            check += Check(tree);
            Free(tree);
        }
        std::printf("%lld\t trees of depth %d\t check: %lld\n", static_cast<long long>(iterations), depth,
                    static_cast<long long>(check));
    }
    std::printf("long lived tree of depth %d\t check: %lld\n", maxDepth, static_cast<long long>(Check(longLived)));
    Free(longLived);
    return 0;
}
