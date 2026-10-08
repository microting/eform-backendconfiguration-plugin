import {TailBiteLocationNode, TailBiteLocationTree, TailBiteOccupancy, TailBiteRule} from '../../../models';

/** One row of the locations table, in tree order. */
export interface TailBiteTreeRow {
  node: TailBiteLocationNode;
  /** The node's own pig count, else the sum of its children's counts (each child's own count first), else null. */
  pigs: number | null;
  pigsSource: 'own' | 'sum' | null;
  ownRule: TailBiteRule | null;
  /** The nearest rule on the node or above it, and the node that rule is attached to. */
  governingRule: TailBiteRule | null;
  governingRuleLocation: TailBiteLocationNode | null;
}

export function liveNodes(tree: TailBiteLocationTree | null): TailBiteLocationNode[] {
  return (tree?.locations ?? []).filter((n) => !n.removed);
}

function byOrder(a: TailBiteLocationNode, b: TailBiteLocationNode): number {
  return a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, undefined, {numeric: true});
}

function childrenByParent(nodes: TailBiteLocationNode[]): Map<number | null, TailBiteLocationNode[]> {
  const children = new Map<number | null, TailBiteLocationNode[]>();
  for (const n of nodes) {
    const list = children.get(n.parentId) ?? [];
    list.push(n);
    children.set(n.parentId, list);
  }
  return children;
}

/** Depth-first, siblings by sort order then name (numeric, so "Sti 9" comes before "Sti 10"). */
export function orderedNodes(nodes: TailBiteLocationNode[]): TailBiteLocationNode[] {
  const children = childrenByParent(nodes);
  const ids = new Set(nodes.map((n) => n.id));
  const result: TailBiteLocationNode[] = [];
  const visit = (n: TailBiteLocationNode) => {
    result.push(n);
    (children.get(n.id) ?? []).sort(byOrder).forEach(visit);
  };
  // Roots: no parent, or a parent that is not in the list (cannot happen for live trees, kept defensive).
  nodes.filter((n) => n.parentId === null || !ids.has(n.parentId)).sort(byOrder).forEach(visit);
  return result;
}

export function nodeMap(nodes: TailBiteLocationNode[]): Map<number, TailBiteLocationNode> {
  return new Map(nodes.map((n) => [n.id, n]));
}

/** The node and its ancestors, root first. */
export function ancestorsAndSelf(id: number, byId: Map<number, TailBiteLocationNode>): TailBiteLocationNode[] {
  const chain: TailBiteLocationNode[] = [];
  let current = byId.get(id);
  while (current && !chain.includes(current)) {
    chain.unshift(current);
    current = current.parentId === null ? undefined : byId.get(current.parentId);
  }
  return chain;
}

/** Names below the property root down to the node ("Stald A › Sektion 4"); the root itself when it is the node. */
export function locationTitle(id: number, byId: Map<number, TailBiteLocationNode>): string {
  const chain = ancestorsAndSelf(id, byId);
  const belowRoot = chain.filter((n) => n.parentId !== null);
  return (belowRoot.length ? belowRoot : chain).map((n) => n.name).join(' › ');
}

export function isDescendantOrSelf(id: number, ancestorId: number, byId: Map<number, TailBiteLocationNode>): boolean {
  return ancestorsAndSelf(id, byId).some((n) => n.id === ancestorId);
}

export function nearestRule(
  id: number,
  byId: Map<number, TailBiteLocationNode>,
  rules: TailBiteRule[],
): {rule: TailBiteRule; location: TailBiteLocationNode} | null {
  const byLocation = new Map(rules.map((r) => [r.locationId, r]));
  const chain = ancestorsAndSelf(id, byId);
  for (let i = chain.length - 1; i >= 0; i--) {
    const rule = byLocation.get(chain[i].id);
    if (rule) {
      return {rule, location: chain[i]};
    }
  }
  return null;
}

export function buildTreeRows(
  tree: TailBiteLocationTree | null,
  rules: TailBiteRule[],
  occupancy: TailBiteOccupancy[],
): TailBiteTreeRow[] {
  const nodes = orderedNodes(liveNodes(tree));
  const byId = nodeMap(nodes);
  const own = new Map(occupancy.map((o) => [o.locationId, o.pigCount]));
  const ruleOf = new Map(rules.map((r) => [r.locationId, r]));
  const children = childrenByParent(nodes);
  // A node's own count stands for everything below it; without one, the counts of its children add up.
  const effective = new Map<number, number | null>();
  const pigsOf = (n: TailBiteLocationNode): number | null => {
    if (!effective.has(n.id)) {
      const counts = (children.get(n.id) ?? []).map(pigsOf).filter((c): c is number => c !== null);
      effective.set(n.id, own.get(n.id) ?? (counts.length ? counts.reduce((a, b) => a + b, 0) : null));
    }
    return effective.get(n.id)!;
  };
  return nodes.map((node) => {
    const governing = nearestRule(node.id, byId, rules);
    const pigs = pigsOf(node);
    let pigsSource: TailBiteTreeRow['pigsSource'];
    if (own.has(node.id)) {
      pigsSource = 'own';
    } else if (pigs === null) {
      pigsSource = null;
    } else {
      pigsSource = 'sum';
    }
    return {
      node,
      pigs,
      pigsSource,
      ownRule: ruleOf.get(node.id) ?? null,
      governingRule: governing?.rule ?? null,
      governingRuleLocation: governing?.location ?? null,
    };
  });
}

/** Where a node may move: anywhere except into its own subtree, and not to its current parent. */
export function moveTargets(nodes: TailBiteLocationNode[], id: number): TailBiteLocationNode[] {
  const byId = nodeMap(nodes);
  const node = byId.get(id);
  if (!node || node.parentId === null) {
    return [];
  }
  return orderedNodes(nodes).filter((n) => n.id !== node.parentId && !isDescendantOrSelf(n.id, id, byId));
}

/** One option per depth from `minDepth` down, labelled with the first location at that depth. */
export function levelOptions(nodes: TailBiteLocationNode[], minDepth = 0): {depth: number; example: string}[] {
  const options: {depth: number; example: string}[] = [];
  for (const n of orderedNodes(nodes)) {
    if (n.depth >= minDepth && !options.some((o) => o.depth === n.depth)) {
      options.push({depth: n.depth, example: n.name});
    }
  }
  return options.sort((a, b) => a.depth - b.depth);
}
