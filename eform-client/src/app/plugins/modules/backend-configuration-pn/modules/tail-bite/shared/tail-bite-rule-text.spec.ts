import {describeRule, ruleInputFromDraft, TailBiteRuleDraft, TailBiteTranslator} from './tail-bite-rule-text';

// Echoes the key with its parameters filled in, like ngx-translate with the English file.
const t: TailBiteTranslator = {
  instant: (key, params) => key.replace(/{{(\w+)}}/g, (_m, name) => String(params?.[name])),
};

describe('describeRule', () => {
  it('names both thresholds when both are set', () => {
    expect(describeRule(t, {minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1}))
      .toBe('5 bitten pigs or 1 severe within 7 days, counted per level 1');
  });

  it('names only the threshold that is set', () => {
    expect(describeRule(t, {minBittenPigs: 8, minSevere: null, windowDays: 14, countDepth: 2}))
      .toBe('8 bitten pigs within 14 days, counted per level 2');
    expect(describeRule(t, {minBittenPigs: null, minSevere: 2, windowDays: 7, countDepth: 0}))
      .toBe('2 severe within 7 days, counted per level 0');
  });
});

describe('ruleInputFromDraft', () => {
  const draft = (over: Partial<TailBiteRuleDraft> = {}): TailBiteRuleDraft =>
    ({ruleId: null, locationId: 2, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1, version: 0, ...over});

  it('builds the request from a valid draft', () => {
    expect(ruleInputFromDraft(draft())).toEqual({locationId: 2, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1});
    expect(ruleInputFromDraft(draft({minSevere: null}))!.minSevere).toBeNull();
  });

  it.each([
    ['no location', {locationId: null}],
    ['no threshold', {minBittenPigs: null, minSevere: null}],
    ['a zero threshold', {minBittenPigs: 0}],
    ['a fractional threshold', {minSevere: 1.5}],
    ['an empty window', {windowDays: null}],
    ['a window above 90 days', {windowDays: 91}],
  ])('refuses %s', (_name, over) => {
    expect(ruleInputFromDraft(draft(over as Partial<TailBiteRuleDraft>))).toBeNull();
  });
});
