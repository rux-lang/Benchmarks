// BinaryTrees: allocates and walks many complete binary trees (single-threaded variant of the
// Computer Language Benchmarks Game program). Nodes are boxed and freed when dropped.
use std::env;

struct Node {
    left: Option<Box<Node>>,
    right: Option<Box<Node>>,
}

fn build(depth: i32) -> Box<Node> {
    if depth > 0 {
        Box::new(Node { left: Some(build(depth - 1)), right: Some(build(depth - 1)) })
    } else {
        Box::new(Node { left: None, right: None })
    }
}

fn check(node: &Node) -> i64 {
    match (&node.left, &node.right) {
        (Some(left), Some(right)) => 1 + check(left) + check(right),
        _ => 1,
    }
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let max_depth_argument: i32 = args.get(1).map_or(18, |a| a.parse().unwrap());

    const MIN_DEPTH: i32 = 4;
    let max_depth = (MIN_DEPTH + 2).max(max_depth_argument);

    let stretch_depth = max_depth + 1;
    println!("stretch tree of depth {stretch_depth}\t check: {}", check(&build(stretch_depth)));

    let long_lived = build(max_depth);
    let mut depth = MIN_DEPTH;
    while depth <= max_depth {
        let iterations = 1i64 << (max_depth - depth + MIN_DEPTH);
        let mut sum = 0i64;
        for _ in 0..iterations {
            sum += check(&build(depth));
        }
        println!("{iterations}\t trees of depth {depth}\t check: {sum}");
        depth += 2;
    }
    println!("long lived tree of depth {max_depth}\t check: {}", check(&long_lived));
}
