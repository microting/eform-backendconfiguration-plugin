import {memoize} from './tail-bite-memo';

describe('memoize', () => {
  it('returns the same result while the dependencies are identical, and recomputes when one changes', () => {
    const compute = jest.fn((items: number[], id: number | null) => items.filter((n) => n !== id));
    const memo = memoize(compute);
    const items = [1, 2, 3];

    const first = memo(items, 2);
    expect(memo(items, 2)).toBe(first);
    expect(compute).toHaveBeenCalledTimes(1);

    expect(memo(items, 3)).toEqual([1, 2]);
    expect(memo([1, 2, 3], 3)).not.toBe(first);
    expect(compute).toHaveBeenCalledTimes(3);
  });
});
