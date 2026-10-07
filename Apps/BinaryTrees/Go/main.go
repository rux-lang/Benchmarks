// BinaryTrees: allocates and walks many complete binary trees (single-threaded variant of the
// Computer Language Benchmarks Game program). Nodes are garbage-collected objects.
package main

import (
	"bufio"
	"fmt"
	"os"
	"strconv"
)

const minDepth = 4

type node struct {
	left  *node
	right *node
}

func build(depth int) *node {
	if depth > 0 {
		return &node{build(depth - 1), build(depth - 1)}
	}
	return &node{nil, nil}
}

func (n *node) check() int64 {
	if n.left == nil {
		return 1
	}
	return 1 + n.left.check() + n.right.check()
}

func argument(index int, fallback int) int {
	if len(os.Args) <= index {
		return fallback
	}
	value, err := strconv.Atoi(os.Args[index])
	if err != nil {
		panic(err)
	}
	return value
}

func main() {
	maxDepthArgument := argument(1, 18)

	maxDepth := max(minDepth+2, maxDepthArgument)
	out := bufio.NewWriter(os.Stdout)

	stretchDepth := maxDepth + 1
	fmt.Fprintf(out, "stretch tree of depth %d\t check: %d\n", stretchDepth, build(stretchDepth).check())

	longLived := build(maxDepth)
	for depth := minDepth; depth <= maxDepth; depth += 2 {
		iterations := int64(1) << (maxDepth - depth + minDepth)
		check := int64(0)
		for i := int64(0); i < iterations; i++ {
			check += build(depth).check()
		}
		fmt.Fprintf(out, "%d\t trees of depth %d\t check: %d\n", iterations, depth, check)
	}
	fmt.Fprintf(out, "long lived tree of depth %d\t check: %d\n", maxDepth, longLived.check())
	out.Flush()
}
