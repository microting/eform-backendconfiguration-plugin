import {TailBiteLocationNode, TailBiteLocationTree, TailBiteOccupancySource, TailBiteRule} from '../../../models';
import {
  ancestorsAndSelf,
  buildTreeRows,
  countingOccupancy,
  levelOptions,
  locationTitle,
  moveTargets,
  nearestRule,
  nodeMap,
  orderedNodes,
  pathOptions,
} from './tail-bite-tree';

// Ejendom > Stald A > Sektion 4 > Sti 10, Sti 9 ; Ejendom > Stald B ; a removed "Gammel stald".
const node = (id: number, parentId: number | null, name: string, depth: number, sortOrder = 0, removed = false): TailBiteLocationNode =>
  ({id, parentId, name, depth, sortOrder, removed, qrCode: `qr${id}`});
const nodes = [
  node(1, null, 'Ejendom', 0),
  node(2, 1, 'Stald A', 1, 0),
  node(3, 2, 'Sektion 4', 2),
  node(5, 3, 'Sti 10', 3),
  node(4, 3, 'Sti 9', 3),
  node(6, 1, 'Stald B', 1, 1),
  node(7, 1, 'Gammel stald', 1, 2, true),
];
const tree: TailBiteLocationTree = {propertyId: 1, treeVersion: 1, locations: nodes, actionTypes: []};
const rule = (id: number, locationId: number, countDepth: number): TailBiteRule =>
  ({id, locationId, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth, version: 1, updatedAt: null});

describe('tail-bite tree helpers', () => {
  it('orders depth-first with numeric sibling order', () => {
    expect(orderedNodes(nodes.filter((n) => !n.removed)).map((n) => n.name))
      .toEqual(['Ejendom', 'Stald A', 'Sektion 4', 'Sti 9', 'Sti 10', 'Stald B']);
  });

  it('builds a title below the root, or the root name for the root', () => {
    const byId = nodeMap(nodes);
    expect(locationTitle(4, byId)).toBe('Stald A › Sektion 4 › Sti 9');
    expect(locationTitle(1, byId)).toBe('Ejendom');
    expect(ancestorsAndSelf(4, byId).map((n) => n.id)).toEqual([1, 2, 3, 4]);
  });

  it('finds the nearest rule walking up', () => {
    const byId = nodeMap(nodes);
    const rules = [rule(10, 1, 1), rule(11, 2, 2)];
    expect(nearestRule(4, byId, rules)!.rule.id).toBe(11);
    expect(nearestRule(6, byId, rules)!.rule.id).toBe(10);
    expect(nearestRule(6, byId, [])).toBeNull();
  });

  it('shows own pig counts, sums for parents without one, and drops removed nodes', () => {
    const occupancy = [
      {locationId: 4, pigCount: 30, source: TailBiteOccupancySource.Manual, validFrom: '2026-09-15T00:00:00Z'},
      {locationId: 5, pigCount: 25, source: TailBiteOccupancySource.Manual, validFrom: '2026-09-15T00:00:00Z'},
      {locationId: 2, pigCount: 400, source: TailBiteOccupancySource.Manual, validFrom: '2026-09-15T00:00:00Z'},
    ];
    const rows = buildTreeRows(tree, [rule(10, 1, 1), rule(11, 2, 2)], occupancy);
    const by = (name: string) => rows.find((r) => r.node.name === name)!;
    expect(rows.some((r) => r.node.name === 'Gammel stald')).toBe(false);
    expect([by('Sektion 4').pigs, by('Sektion 4').pigsSource]).toEqual([55, 'sum']);
    expect([by('Stald A').pigs, by('Stald A').pigsSource]).toEqual([400, 'own']);
    expect([by('Ejendom').pigs, by('Ejendom').pigsSource]).toEqual([400, 'sum']);
    expect([by('Stald B').pigs, by('Stald B').pigsSource]).toEqual([null, null]);
    expect(by('Stald A').ownRule!.id).toBe(11);
    expect(by('Sti 9').ownRule).toBeNull();
    expect(by('Sti 9').governingRuleLocation!.name).toBe('Stald A');
  });

  it('never offers a move into the own subtree or to the current parent, and never moves the root', () => {
    const live = nodes.filter((n) => !n.removed);
    expect(moveTargets(live, 3).map((n) => n.name)).toEqual(['Ejendom', 'Stald B']);
    expect(moveTargets(live, 1)).toEqual([]);
  });

  it('labels each depth with its first location, from the minimum depth down', () => {
    const live = nodes.filter((n) => !n.removed);
    expect(levelOptions(live)).toEqual([
      {depth: 0, example: 'Ejendom'}, {depth: 1, example: 'Stald A'}, {depth: 2, example: 'Sektion 4'}, {depth: 3, example: 'Sti 9'},
    ]);
    expect(levelOptions(live, 2).map((o) => o.depth)).toEqual([2, 3]);
  });

  it('labels select options with their path, so equal names under different parents can be told apart', () => {
    const twins = [...nodes, node(8, 6, 'Sektion 4', 2)];
    expect(pathOptions([twins[0], twins[2], twins[7]], twins)).toEqual([
      {id: 1, path: 'Ejendom'},
      {id: 3, path: 'Stald A › Sektion 4'},
      {id: 8, path: 'Stald B › Sektion 4'},
    ]);
  });

  it('returns the pig-count records a node\'s count is made of, leaving out counts an own count overrides', () => {
    const count = (locationId: number) =>
      ({locationId, pigCount: 10, source: TailBiteOccupancySource.Manual, validFrom: '2026-09-01T00:00:00Z'});
    // Sektion 4 has its own count, so Sti 9's below it does not count; the removed stable is not in the tree.
    const occupancy = [count(3), count(4), count(6), count(7)];
    expect(countingOccupancy(tree, occupancy, 1).map((o) => o.locationId)).toEqual([3, 6]);
    expect(countingOccupancy(tree, occupancy, 5)).toEqual([]);
    expect(countingOccupancy(tree, occupancy, 7)).toEqual([]);
  });
});
