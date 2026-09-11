import {
  buildDuplicateBoardName,
  isDuplicateBoardName,
  normalizeBoardName,
} from './calendar-board-name.helper';

function board(id: number, name: string) {
  return {id, name};
}

/** The Danish suffix, as CalendarContainerComponent supplies it. */
const daLabel = (name: string, index: number) =>
  index === 1 ? `${name} (kopi)` : `${name} (kopi ${index})`;

describe('normalizeBoardName', () => {
  it('trims, collapses inner runs of whitespace and lower-cases', () => {
    expect(normalizeBoardName('  Drift   A  ')).toBe('drift a');
  });

  it('treats null and undefined as an empty name', () => {
    expect(normalizeBoardName(null)).toBe('');
    expect(normalizeBoardName(undefined)).toBe('');
  });

  it('lower-cases the Danish letters a calendar name actually carries', () => {
    expect(normalizeBoardName('MILJØTILSYN ÅRLIG ÆNDRING')).toBe('miljøtilsyn årlig ændring');
  });
});

describe('isDuplicateBoardName', () => {
  const boards = [board(1, 'Default'), board(2, 'Drift'), board(3, 'Miljøtilsyn')];

  it('matches an existing name exactly', () => {
    expect(isDuplicateBoardName('Drift', boards)).toBe(true);
  });

  it('matches regardless of case — the guard is case-insensitive', () => {
    expect(isDuplicateBoardName('drift', boards)).toBe(true);
    expect(isDuplicateBoardName('DRIFT', boards)).toBe(true);
    expect(isDuplicateBoardName('DrIfT', boards)).toBe(true);
  });

  it('matches regardless of surrounding whitespace', () => {
    expect(isDuplicateBoardName('   Drift ', boards)).toBe(true);
  });

  it('matches a name that differs only in how its inner spaces run', () => {
    expect(isDuplicateBoardName('Drift  A', [board(1, 'Drift A')])).toBe(true);
  });

  // Collapsing runs must not collapse the words themselves: a space inserted
  // mid-word makes a different name, not the same one.
  it('does not match a name broken by an extra word boundary', () => {
    expect(isDuplicateBoardName('Mil jøtilsyn', boards)).toBe(false);
  });

  it('lets a genuinely new name through', () => {
    expect(isDuplicateBoardName('Rengøring', boards)).toBe(false);
  });

  // Editing a calendar without touching its name (recolouring it, say) must not
  // report the calendar as a duplicate of itself.
  it('ignores the calendar being edited', () => {
    expect(isDuplicateBoardName('Drift', boards, 2)).toBe(false);
  });

  it('still blocks an edit that renames onto ANOTHER calendar', () => {
    expect(isDuplicateBoardName('Default', boards, 2)).toBe(true);
  });

  // Blank is Validators.required's job. Reporting "already exists" for an empty
  // field would name the wrong problem.
  it('never reports a blank name as a duplicate', () => {
    expect(isDuplicateBoardName('', boards)).toBe(false);
    expect(isDuplicateBoardName('   ', boards)).toBe(false);
    expect(isDuplicateBoardName(null, boards)).toBe(false);
  });

  it('finds nothing in an empty property', () => {
    expect(isDuplicateBoardName('Drift', [])).toBe(false);
  });
});

describe('buildDuplicateBoardName', () => {
  it('uses the plain "(kopi)" form when it is free', () => {
    const boards = [board(1, 'Drift')];

    expect(buildDuplicateBoardName('Drift', boards, daLabel)).toBe('Drift (kopi)');
  });

  // Duplicating twice is ordinary. Handing back a name the modal's own guard
  // would reject is not.
  it('climbs past a "(kopi)" that already exists', () => {
    const boards = [board(1, 'Drift'), board(2, 'Drift (kopi)')];

    expect(buildDuplicateBoardName('Drift', boards, daLabel)).toBe('Drift (kopi 2)');
  });

  it('keeps climbing past every taken index', () => {
    const boards = [
      board(1, 'Drift'),
      board(2, 'Drift (kopi)'),
      board(3, 'Drift (kopi 2)'),
      board(4, 'Drift (kopi 3)'),
    ];

    expect(buildDuplicateBoardName('Drift', boards, daLabel)).toBe('Drift (kopi 4)');
  });

  // The collision check runs through the same normalizer as the guard, so a
  // casing-only match still counts as taken.
  it('treats a differently-cased existing copy as taken', () => {
    const boards = [board(1, 'Drift'), board(2, 'DRIFT (KOPI)')];

    expect(buildDuplicateBoardName('Drift', boards, daLabel)).toBe('Drift (kopi 2)');
  });

  it('trims the source name before suffixing it', () => {
    expect(buildDuplicateBoardName('  Drift  ', [], daLabel)).toBe('Drift (kopi)');
  });

  it('produces the English suffix when the English label is supplied', () => {
    const enLabel = (name: string, index: number) =>
      index === 1 ? `${name} (copy)` : `${name} (copy ${index})`;

    expect(buildDuplicateBoardName('Drift', [board(1, 'Drift')], enLabel)).toBe('Drift (copy)');
  });
});
