import {TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {EventEmitter} from '@angular/core';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, NEVER, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteRulesPageComponent} from './tail-bite-rules-page.component';

describe('TailBiteRulesPageComponent', () => {
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [], locations: [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'qa', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'qb', removed: false},
    {id: 3, parentId: 2, name: 'Sektion 4', depth: 2, sortOrder: 0, qrCode: 'qc', removed: false},
  ]};
  const rootRule = {id: 9, locationId: 1, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1, version: 1,
    updatedAt: '2026-10-04T05:00:00Z'};
  const stableRule = {id: 10, locationId: 2, minBittenPigs: 3, minSevere: null, windowDays: 7, countDepth: 2, version: 2, updatedAt: null};
  let service: Record<string, jest.Mock>;
  let dialogOpen: jest.Mock;
  let component: TailBiteRulesPageComponent;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  /** Makes the next dialog a confirm dialog whose Delete button is the returned emitter. */
  function mockConfirmDialog(): EventEmitter<unknown> {
    const confirmed = new EventEmitter<unknown>();
    dialogOpen.mockReturnValue({componentInstance: {delete: confirmed}, close: jest.fn(), afterClosed: () => NEVER});
    return confirmed;
  }

  beforeEach(() => {
    jest.useFakeTimers();
    service = {
      getTree: jest.fn().mockReturnValue(of({success: true, model: tree})),
      getRules: jest.fn().mockReturnValue(of({success: true, model: [stableRule, rootRule]})),
      getRuleHistory: jest.fn().mockReturnValue(of({success: true, model: [
        {version: 2, locationId: 2, minBittenPigs: 3, minSevere: null, windowDays: 7, countDepth: 2, changedAt: null}]})),
      previewRule: jest.fn().mockReturnValue(of({success: true, model: {outbreaksWouldOpen: 4, perSummingLocation: {'3': 3, '2': 1}}})),
      createRule: jest.fn().mockReturnValue(of({success: true, model: 11})),
      updateRule: jest.fn().mockReturnValue(of({success: true})),
      deleteRule: jest.fn().mockReturnValue(of({success: true})),
    };
    dialogOpen = jest.fn();
    params = new BehaviorSubject(convertToParamMap({propertyId: '3'}));
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (k: string, p?: Record<string, unknown>) =>
          k.replace(/{{(\w+)}}/g, (_m, n) => String(p?.[n]))}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteRulesPageComponent());
    component.ngOnInit();
  });

  afterEach(() => {
    component.ngOnDestroy();
    jest.useRealTimers();
  });

  it('lists rules in tree order with the root rule marked', () => {
    expect(component.items.map((i) => [i.location, i.isRoot])).toEqual([['Ejendom', true], ['Stald A', false]]);
    expect(component.items[1].text).toBe('3 bitten pigs within 7 days, counted per level 2');
  });

  it('previews an edited rule after a pause, once', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    component.requestPreview();
    jest.advanceTimersByTime(400);
    expect(service.previewRule).toHaveBeenCalledTimes(1);
    expect(service.previewRule).toHaveBeenCalledWith({locationId: 2, minBittenPigs: 4, minSevere: null, windowDays: 7, countDepth: 2});
    expect(component.preview!.outbreaksWouldOpen).toBe(4);
    expect(component.previewLines).toEqual([{location: 'Stald A › Sektion 4', count: 3}, {location: 'Stald A', count: 1}]);
    expect(service.getRuleHistory).toHaveBeenCalledWith(10);
  });

  it('does not preview or save a rule without any threshold', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = null;
    component.requestPreview();
    jest.advanceTimersByTime(400);
    expect(service.previewRule).not.toHaveBeenCalled();
    expect(component.canSave).toBe(false);
  });

  it('saves an edit as the next version', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    component.save();
    expect(service.updateRule).toHaveBeenCalledWith(10, {locationId: 2, minBittenPigs: 4, minSevere: null, windowDays: 7, countDepth: 2});
  });

  it('creates a rule on a free location, never summing above it', () => {
    component.newRule();
    expect(component.freeLocations.map((n) => n.name)).toEqual(['Sektion 4']);
    component.chooseLocation(3);
    expect(component.draft!.countDepth).toBe(2);
    expect(component.levels.map((l) => l.depth)).toEqual([2]);
    // Bound to mtx-selects: the same list until an input changes, or a click on an option is lost.
    expect(component.freeLocations).toBe(component.freeLocations);
    expect(component.levels).toBe(component.levels);
    component.save();
    expect(service.createRule).toHaveBeenCalledWith({locationId: 3, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 2});
  });

  it('deletes a rule after confirmation but never the root rule', () => {
    component.remove(component.items[0]);
    expect(dialogOpen).not.toHaveBeenCalled();
    const confirmed = mockConfirmDialog();
    component.edit(stableRule);
    expect(component.draft).not.toBeNull();
    component.remove(component.items[1]);
    confirmed.emit({});
    expect(service.deleteRule).toHaveBeenCalledWith(10);
    expect(component.draft).toBeNull();
  });

  it('does not save twice while a save is in flight', () => {
    const pending = new Subject<{success: boolean}>();
    service.updateRule.mockReturnValue(pending);
    component.edit(stableRule);
    component.save();
    component.save();
    expect(service.updateRule).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(true);
    expect(component.canSave).toBe(false);
    pending.next({success: true});
    pending.complete();
    expect(component.busy).toBe(false);
  });

  it('keeps the edits and refreshes the list after a refused save', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    service.updateRule.mockReturnValueOnce(of({success: false, message: 'no'}));
    const loads = service.getRules.mock.calls.length;
    component.save();
    expect(service.getRules.mock.calls.length).toBe(loads + 1);
    expect(component.draft!.minBittenPigs).toBe(4);
    expect(component.draft!.version).toBe(2);
    expect(component.busy).toBe(false);
  });

  it('keeps the edits and refreshes the list after a failed save', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    service.updateRule.mockReturnValueOnce(throwError(() => new Error('boom')));
    const loads = service.getRules.mock.calls.length;
    component.save();
    expect(service.getRules.mock.calls.length).toBe(loads + 1);
    expect(component.draft!.minBittenPigs).toBe(4);
    expect(component.busy).toBe(false);
  });

  it('closes the editor when the edited rule no longer exists after a refresh', () => {
    component.edit(stableRule);
    service.getRules.mockReturnValue(of({success: true, model: [rootRule]}));
    component.load();
    expect(component.draft).toBeNull();
  });

  it('explains why a draft cannot be saved', () => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = null;
    component.draft!.minSevere = null;
    expect(component.saveHint).toBe('Set at least one threshold.');
    component.draft!.minBittenPigs = 1.5;
    expect(component.saveHint).toBe('A threshold must be a whole number of at least 1.');
    component.draft!.minBittenPigs = 0;
    expect(component.saveHint).toBe('A threshold must be a whole number of at least 1.');
    component.draft!.minBittenPigs = 3;
    component.draft!.windowDays = 91;
    expect(component.saveHint).toBe('The number of days must be a whole number from 1 to 90.');
    component.draft!.windowDays = 7;
    expect(component.saveHint).toBe('');
  });

  it('shows an error when the preview request fails', () => {
    service.previewRule.mockReturnValue(throwError(() => new Error('boom')));
    component.edit(stableRule);
    jest.advanceTimersByTime(400);
    expect(component.preview).toBeNull();
    expect(component.previewError).toBe('Could not calculate the preview');
  });

  it.each([
    ['is refused', () => of({success: false, message: 'no'})],
    ['fails', () => throwError(() => new Error('boom'))],
  ])('keeps the page and the editor when a refresh %s', (_name, answer) => {
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    const items = component.items;
    service.getRules.mockReturnValue(answer());
    component.load();
    expect(component.items).toBe(items);
    expect(component.draft!.minBittenPigs).toBe(4);
    expect(component.editedItem).not.toBeNull();
  });

  it('asks for a location before a new rule can be saved', () => {
    component.newRule();
    expect(component.saveHint).toBe('Choose a location.');
    expect(component.canSave).toBe(false);
  });

  it('shows a generic message when a refused preview has none', () => {
    service.previewRule.mockReturnValue(of({success: false}));
    component.edit(stableRule);
    jest.advanceTimersByTime(400);
    expect(component.previewError).toBe('Could not calculate the preview');
  });

  it('clears the page on a property switch and drops late responses for the old property', () => {
    const lateRules = new Subject<{success: boolean; model: unknown[]}>();
    service.getRules.mockReturnValueOnce(lateRules).mockReturnValue(NEVER);
    component.edit(stableRule);
    component.load();
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.items).toEqual([]);
    expect(component.draft).toBeNull();
    lateRules.next({success: true, model: [stableRule, rootRule]});
    lateRules.complete();
    expect(component.items).toEqual([]);
  });

  it('drops a preview that answers after the property changed', () => {
    const latePreview = new Subject<{success: boolean; model: unknown}>();
    service.previewRule.mockReturnValue(latePreview);
    component.edit(stableRule);
    jest.advanceTimersByTime(400);
    params.next(convertToParamMap({propertyId: '4'}));
    latePreview.next({success: true, model: {outbreaksWouldOpen: 9, perSummingLocation: {}}});
    expect(component.preview).toBeNull();
  });

  it('keeps only the newest preview', () => {
    const first = new Subject<{success: boolean; model: unknown}>();
    const second = new Subject<{success: boolean; model: unknown}>();
    service.previewRule.mockReturnValueOnce(first).mockReturnValueOnce(second);
    component.edit(stableRule);
    jest.advanceTimersByTime(400);
    component.draft!.minBittenPigs = 4;
    component.requestPreview();
    jest.advanceTimersByTime(400);
    first.next({success: true, model: {outbreaksWouldOpen: 1, perSummingLocation: {}}});
    expect(component.preview).toBeNull();
    second.next({success: true, model: {outbreaksWouldOpen: 2, perSummingLocation: {}}});
    expect(component.preview!.outbreaksWouldOpen).toBe(2);
  });

  it('shows the server message when the preview is refused', () => {
    service.previewRule.mockReturnValue(of({success: false, message: 'too wide'}));
    component.edit(stableRule);
    jest.advanceTimersByTime(400);
    expect(component.preview).toBeNull();
    expect(component.previewError).toBe('too wide');
  });

  it('keeps the draft and reloads when the delete is refused', () => {
    service.deleteRule.mockReturnValue(of({success: false, message: 'no'}));
    const confirmed = mockConfirmDialog();
    component.edit(stableRule);
    component.draft!.minBittenPigs = 4;
    const loads = service.getRules.mock.calls.length;
    component.remove(component.items[1]);
    confirmed.emit({});
    expect(service.getRules.mock.calls.length).toBe(loads + 1);
    expect(component.draft!.minBittenPigs).toBe(4);
    expect(component.busy).toBe(false);
  });

  it('keeps the newest answer when an older load answers last', () => {
    const olderRules = new Subject<unknown>();
    service.getRules.mockReturnValueOnce(olderRules);
    component.load();
    service.getRules.mockReturnValueOnce(of({success: true, model: [rootRule]}));
    component.load();
    expect(component.items.length).toBe(1);
    olderRules.next({success: true, model: [stableRule, rootRule]});
    olderRules.complete();
    expect(component.items.length).toBe(1);
  });
});
