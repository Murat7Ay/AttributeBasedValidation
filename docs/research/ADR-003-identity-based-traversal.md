# ADR-003: Traverse explicit edges only, visiting each instance once by reference identity

**Status:** Accepted

## Context
The legacy code recurses through `SmartChildClass` members using native recursion and does not
remember which objects it has visited.

- A self-referencing `Child` overflows the stack. This kills the process and cannot be caught.
- A shared instance that is reachable twice gets validated twice.
- If that shared instance were being mutated, it would be mutated twice.

## Decision
- Traversal follows **declared edges only** (`Descend()` / `[Descend]`). Undeclared members are
  never entered.
- Each object instance is evaluated **once**, at the first path that reaches it. That path is
  determined by a deterministic, depth-first traversal in declaration order.
- Visitation uses `ReferenceEqualityComparer`, so an overridden `Equals` cannot merge distinct
  objects.
- Traversal uses an explicit stack, not recursion.
- Enumerable values (except `string`) are descended element by element.
  - Nested enumerables are flattened with nested indices, e.g. `Grid[1][2]`.
  - A null element produces a `null-element` violation, unless null elements are allowed.

## Alternatives considered
- **Per-path (tree) semantics with an ancestor set to break cycles.** This gives complete per-path
  findings. But it duplicates findings for shared instances, produces duplicate or conflicting
  patches for the same member, and has exponential worst-case cost on DAGs.
- **A maximum depth limit only.** Any limit is arbitrary, and shared nodes are still visited
  repeatedly.
- **Implicitly traversing every reference-typed member.** This walks into framework objects,
  lazy-loading proxies and so on.

## Consequences
- Cost is O(objects + edges). Traversal terminates on any graph and cannot overflow the stack.
- A finding on a shared instance is reported once, at its first path. This is documented, not
  hidden.
- Value types are not identity-tracked. Structs cannot form cycles, so they are evaluated wherever
  they appear.

## Rejected alternatives
Tree semantics (see above).

## Evidence from the legacy repository
- `ValidationHelper.cs:201` and `ValidationHelper.cs:212` recurse without a visited set.
- `Child.NestedChild` makes the sample model self-similar.
- `[SmartChildClass(false)]` shows the author expected recursive structures.
