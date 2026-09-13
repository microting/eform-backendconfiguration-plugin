/**
 * Calendar (board) name rules — pure, so they can be unit-tested without a
 * TestBed and shared between the create/edit modal and the duplicate action.
 *
 * The uniqueness rule is a **UI-level guard only** (#1210). `CalendarBoard` has
 * no unique index on (PropertyId, Name) and `CalendarController`'s POST/PUT do
 * not check either, so a concurrent save from another tab still lands. Adding a
 * real constraint would need an `-base` migration, which is out of scope for
 * the epic — so this blocks the mistake a user can actually make, and nothing
 * here should be read as an invariant the data honours.
 */

/** One board, reduced to what the name rules need. */
export interface NamedBoard {
  id: number;
  name: string;
}

/**
 * The comparison form of a calendar name: trimmed, inner whitespace collapsed,
 * lower-cased. Collapsing runs matters because the name comes from a free-text
 * input — "Drift  A" and "Drift A" are the same name to a human, and letting
 * both exist makes the calendars dropdown unreadable.
 *
 * `toLocaleLowerCase()` rather than `toLowerCase()`: the platform ships Turkish
 * neither here nor in the locale list, but Danish/German names routinely carry
 * Æ/Ø/Å/ẞ and the locale-aware form is correct for those without costing
 * anything.
 */
export function normalizeBoardName(name: string | null | undefined): string {
  return (name ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase();
}

/**
 * True when `name` collides with another calendar of the same property.
 *
 * `excludeId` is the board being edited: re-saving a calendar without touching
 * its name (only its colour, say) must not trip the guard against itself.
 * A blank name never collides — that is `Validators.required`'s job, and
 * reporting "name already exists" for an empty field would be a lie.
 */
export function isDuplicateBoardName(
  name: string | null | undefined,
  boards: readonly NamedBoard[],
  excludeId?: number | null,
): boolean {
  const candidate = normalizeBoardName(name);
  if (!candidate) {
    return false;
  }
  return boards.some(b => b.id !== excludeId && normalizeBoardName(b.name) === candidate);
}

/**
 * The name a "Duplicate" gives the new calendar.
 *
 * There is no duplicate endpoint (#1210): the client just POSTs a second board
 * with the source's colour and no events, so it has to pick the name itself.
 * `label(name, 1)` is the plain "<name> (copy)" form; if that is already taken
 * the index climbs until a free name appears, because the very next thing the
 * user would hit is the duplicate-name guard on their own generated name —
 * duplicating twice is a normal thing to do, and failing the second one would
 * be gratuitous.
 *
 * `label` is injected rather than hard-coded so the suffix is translated
 * ("(kopi)" in Danish) without dragging TranslateService into a pure function.
 * The loop is bounded by `boards.length + 1`: with N existing names at most N
 * candidates can collide, so a free one is always found inside it.
 */
export function buildDuplicateBoardName(
  sourceName: string,
  boards: readonly NamedBoard[],
  label: (name: string, index: number) => string,
): string {
  const base = (sourceName ?? '').trim();
  for (let index = 1; index <= boards.length + 1; index++) {
    const candidate = label(base, index);
    if (!isDuplicateBoardName(candidate, boards)) {
      return candidate;
    }
  }
  // Unreachable for the reason above; kept so the function is total rather than
  // returning undefined on a future change to the loop bound.
  return label(base, boards.length + 2);
}
