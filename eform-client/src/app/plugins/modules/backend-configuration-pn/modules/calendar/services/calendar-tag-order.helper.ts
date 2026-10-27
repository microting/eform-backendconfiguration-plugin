/**
 * Danish, case-insensitive tag name order (Æ, Ø, Å after Z) — the same order
 * the server's TagNameComparer.Danish returns the planning tags in, so a tag
 * created in the modal lands where a reload would put it. 'accent', not
 * 'base': the server ignores case only, so 'Cafe' still sorts before 'Café'.
 */
export function compareTagNames(a: string, b: string): number {
  return a.localeCompare(b, 'da', {sensitivity: 'accent'});
}
