/**
 * Wraps `compute` so it returns the same result while every dependency is identical (===). A list bound to an
 * mtx-select must keep its reference between change-detection passes: a fresh array each pass makes the select
 * rebuild its options and drop the click that was meant to pick one.
 */
export function memoize<D extends readonly unknown[], R>(compute: (...deps: D) => R): (...deps: D) => R {
  let last: D | null = null;
  let value!: R;
  return (...deps: D): R => {
    const changed = last === null || deps.length !== last.length || deps.some((dep, i) => dep !== last![i]);
    if (changed) {
      last = deps;
      value = compute(...deps);
    }
    return value;
  };
}
