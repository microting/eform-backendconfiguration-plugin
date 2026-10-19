import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {Overlay} from '@angular/cdk/overlay';
import {DestroyRef, EventEmitter, Injector, runInInjectionContext} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, EMPTY, Subject, of, throwError} from 'rxjs';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalSettingsPageComponent} from './chemical-settings-page.component';

describe('ChemicalSettingsPageComponent', () => {
  let property$: BehaviorSubject<number | null>;
  let chemicals: {getSettings: Mock; saveSettings: Mock; getDigestPreview: Mock};
  let dialogOpen: Mock;
  let component: ChemicalSettingsPageComponent;
  const item = (name: string, deadline: string | null = null) => ({
    placementId: 1, chemicalId: 2, productName: name, registrationNo: '1-1', locationName: 'Lade', placementNote: '',
    deadlineKind: null, deadline, threshold: null, status: 5, statusText: '', registeredByName: '', registeredAt: null,
  });
  const preview = {
    propertyId: 3, propertyName: 'Hovedgården', isoWeek: 40, generatedAt: '2026-09-29T05:00:00Z',
    passedDeadlines: [item('Karate', '2026-09-01T00:00:00Z')], upcomingDeadlines: [], deregisteredOrExpired: [item('Hussar')],
    newRegistrations: [], recipients: [], hasActionItems: true,
  };

  /** Makes the next dialog a confirm whose Delete button is `confirmed` and whose closing is `closed`. */
  function mockConfirmDialog() {
    const confirmed = new EventEmitter<unknown>();
    const closed = new Subject<unknown>();
    const ref = {componentInstance: {delete: confirmed}, close: vi.fn(), afterClosed: () => closed};
    dialogOpen.mockReturnValue(ref);
    return {confirmed, closed, ref};
  }

  /** A fresh page over the current mocks (a test that needs a different answer sets the mock first). */
  function build(): ChemicalSettingsPageComponent {
    const injector = Injector.create({
      providers: [
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(property$)}},
        {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
        {provide: DestroyRef, useValue: {onDestroy: vi.fn()}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (key: string) => key}},
      ],
    });
    const page = runInInjectionContext(injector, () => new ChemicalSettingsPageComponent());
    page.ngOnInit();
    return page;
  }

  beforeEach(() => {
    property$ = new BehaviorSubject<number | null>(3);
    dialogOpen = vi.fn();
    chemicals = {
      getSettings: vi.fn().mockReturnValue(of({success: true, model: {propertyId: 3, stockEnabled: false, digestRecipients: ['first@example.com']}})),
      saveSettings: vi.fn().mockReturnValue(of({success: true, model: {propertyId: 3, stockEnabled: true, digestRecipients: ['first@example.com', 'second@example.com']}})),
      getDigestPreview: vi.fn().mockReturnValue(of({success: true, model: preview})),
    };
    component = build();
  });

  it('loads settings and the preview for the selected property', () => {
    expect(chemicals.getSettings).toHaveBeenCalledWith(3);
    expect(chemicals.getDigestPreview).toHaveBeenCalledWith(3);
    expect(component.stockEnabled).toBe(false);
    expect(component.recipients).toEqual(['first@example.com']);
    expect(component.dirty).toBe(false);
  });

  it('adds valid recipients, reports invalid ones and marks the form dirty', () => {
    component.recipientInput = 'Second@example.com, nope';
    component.addRecipientInput();

    expect(component.recipients).toEqual(['first@example.com', 'second@example.com']);
    expect(component.invalidRecipients).toEqual(['nope']);
    expect(component.recipientInput).toBe('nope');
    expect(component.dirty).toBe(true);
  });

  it('saves stock toggle and recipients, then refreshes the preview', () => {
    component.onStockToggle(true);
    component.recipientInput = 'second@example.com';
    component.addRecipientInput();

    component.save();

    expect(chemicals.saveSettings).toHaveBeenCalledWith(3, {stockEnabled: true, digestRecipients: ['first@example.com', 'second@example.com']});
    expect(chemicals.getDigestPreview).toHaveBeenCalledTimes(2);
    expect(component.dirty).toBe(false);
    expect(component.busy).toBe(false);
  });

  it('keeps the edits and says so when the save is refused or fails', () => {
    component.removeRecipient('first@example.com');
    chemicals.saveSettings.mockReturnValue(of({success: false, message: 'Nope'}));
    component.save();
    expect(component.saveFailed).toBe(true);
    expect(component.dirty).toBe(true);
    expect(component.recipients).toEqual([]);

    chemicals.saveSettings.mockReturnValue(throwError(() => new Error('offline')));
    component.save();
    expect(component.saveFailed).toBe(true);
    expect(component.busy).toBe(false);
    expect(component.dirty).toBe(true);
  });

  it('removes a recipient', () => {
    component.removeRecipient('first@example.com');

    expect(component.recipients).toEqual([]);
    expect(component.dirty).toBe(true);
  });

  it('shows the fixed thresholds read-only (spec section 9)', () => {
    expect(component.thresholds.map((t) => t.key)).toEqual([
      'Chemicals: Threshold six months', 'Chemicals: Threshold one month',
      'Chemicals: Threshold passed', 'Chemicals: Threshold status changed',
    ]);
  });

  it('lists only the non-empty digest sections, in the order of the e-mail', () => {
    expect(component.previewSections.map((s) => s.id)).toEqual(['passed', 'status']);
    expect(component.previewSections[0].items.map((i) => i.productName)).toEqual(['Karate']);
  });

  it('is busy only while a save is in flight (the container locks the property picker)', () => {
    expect(component.busy).toBe(false);
    const pending = new Subject<unknown>();
    chemicals.saveSettings.mockReturnValue(pending);
    component.onStockToggle(true);

    component.save();
    expect(component.busy).toBe(true);
    pending.next({success: true, model: {propertyId: 3, stockEnabled: true, digestRecipients: []}});
    pending.complete();

    expect(component.busy).toBe(false);
  });

  it('ignores a save answer for a property that is no longer selected', () => {
    const pending = new Subject<unknown>();
    chemicals.saveSettings.mockReturnValue(pending);
    component.onStockToggle(true);
    component.save();
    chemicals.getSettings.mockReturnValue(of({success: true, model: {propertyId: 4, stockEnabled: false, digestRecipients: []}}));
    property$.next(4);

    pending.next({success: true, model: {propertyId: 3, stockEnabled: true, digestRecipients: ['late@example.com']}});

    expect(component.stockEnabled).toBe(false);
    expect(component.recipients).toEqual([]);
  });

  describe('loading the stored settings (never offer Save from the defaults)', () => {
    const loaded = (stockEnabled: boolean, digestRecipients: string[]) =>
      ({success: true, model: {propertyId: 3, stockEnabled, digestRecipients}});

    it('is loading, not editable and cannot save until getSettings answers', () => {
      const pending = new Subject<unknown>();
      chemicals.getSettings.mockReturnValue(pending);
      component = build();

      expect(component.load.loading).toBe(true);
      expect(component.ready).toBe(false);
      component.onStockToggle(true);
      component.recipientInput = 'second@example.com';
      component.addRecipientInput();
      component.save();
      expect(component.stockEnabled).toBe(false);
      expect(component.recipients).toEqual([]);
      expect(component.dirty).toBe(false);
      expect(chemicals.saveSettings).not.toHaveBeenCalled();

      pending.next(loaded(true, ['first@example.com']));

      expect(component.load.loading).toBe(false);
      expect(component.ready).toBe(true);
      expect(component.stockEnabled).toBe(true);
      expect(component.recipients).toEqual(['first@example.com']);
    });

    it.each([
      ['a refused answer', () => of({success: false, message: 'Nope'})],
      ['an HTTP error', () => throwError(() => new Error('offline'))],
      // The host interceptor gives up on a 5xx or a network failure with EMPTY (Task 14 M2).
      ['no answer at all', () => EMPTY],
    ])('shows a load failure for %s and offers no Save from the defaults', (_name, answer) => {
      chemicals.getSettings.mockReturnValue(answer());
      component = build();

      expect(component.load.failed).toBe(true);
      expect(component.load.loading).toBe(false);
      expect(component.ready).toBe(false);
      component.onStockToggle(true);
      component.save();
      expect(chemicals.saveSettings).not.toHaveBeenCalled();
      expect(component.dirty).toBe(false);
    });

    it('starts loading again for the next property and ignores a late answer for the previous one', () => {
      const first = new Subject<unknown>();
      chemicals.getSettings.mockReturnValueOnce(first);
      property$ = new BehaviorSubject<number | null>(3); // the page from beforeEach must not also react
      component = build();
      chemicals.getSettings.mockReturnValueOnce(of({success: true, model: {propertyId: 4, stockEnabled: false, digestRecipients: ['second@example.com']}}));

      property$.next(4);
      first.next(loaded(true, ['late@example.com']));

      expect(component.recipients).toEqual(['second@example.com']);
      expect(component.stockEnabled).toBe(false);
      expect(component.load.loading).toBe(false);
    });
  });

  describe('the digest preview has a failure state', () => {
    it.each([
      ['a refused answer', () => of({success: false, message: 'Nope'})],
      ['an HTTP error', () => throwError(() => new Error('offline'))],
    ])('shows a failed preview for %s', (_name, answer) => {
      chemicals.getDigestPreview.mockReturnValue(answer());
      component = build();

      expect(component.previewFailed).toBe(true);
      expect(component.preview).toBeNull();
      expect(component.previewSections).toEqual([]);
    });

    it('drops the old preview when the refresh after a save fails', () => {
      expect(component.preview).not.toBeNull();
      chemicals.getDigestPreview.mockReturnValue(throwError(() => new Error('offline')));
      component.onStockToggle(true);

      component.save();

      expect(component.previewFailed).toBe(true);
      expect(component.preview).toBeNull();
    });

    it('clears the failure once a preview arrives', () => {
      chemicals.getDigestPreview.mockReturnValueOnce(throwError(() => new Error('offline')));
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      expect(component.previewFailed).toBe(true);

      property$.next(4);

      expect(component.previewFailed).toBe(false);
      expect(component.preview).not.toBeNull();
    });
  });

  describe('switching property resets the load state', () => {
    it('clears the failed and loading state when the next property loads', () => {
      chemicals.getSettings.mockReturnValueOnce(throwError(() => new Error('offline')));
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      expect(component.load.failed).toBe(true);

      property$.next(4);

      expect(component.load.failed).toBe(false);
      expect(component.load.loading).toBe(false);
      expect(component.ready).toBe(true);
    });

    it('is loading again (not failed) while the next property is pending', () => {
      chemicals.getSettings.mockReturnValueOnce(throwError(() => new Error('offline')));
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      chemicals.getSettings.mockReturnValueOnce(new Subject());

      property$.next(4);

      expect(component.load.failed).toBe(false);
      expect(component.load.loading).toBe(true);
      expect(component.ready).toBe(false);
    });
  });

  describe('dirty means "differs from the loaded settings"', () => {
    it('is clean again after undoing a stock toggle', () => {
      component.onStockToggle(true);
      expect(component.dirty).toBe(true);

      component.onStockToggle(false);

      expect(component.dirty).toBe(false);
    });

    it('is clean again after adding and removing the same recipient', () => {
      component.recipientInput = 'second@example.com';
      component.addRecipientInput();
      expect(component.dirty).toBe(true);

      component.removeRecipient('second@example.com');

      expect(component.dirty).toBe(false);
    });
  });

  describe('unsaved edits are never dropped silently', () => {
    it('lets the user leave at once when nothing is unsaved', () => {
      const answers: boolean[] = [];

      component.confirmDiscardChanges().subscribe((ok) => answers.push(ok));

      expect(answers).toEqual([true]);
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('asks with the platform confirm dialog and lets go after "discard"', () => {
      const {confirmed, ref} = mockConfirmDialog();
      component.onStockToggle(true);
      const answers: boolean[] = [];

      component.confirmDiscardChanges().subscribe((ok) => answers.push(ok));
      expect(dialogOpen).toHaveBeenCalledWith(DeleteModalComponent, expect.objectContaining({
        data: expect.objectContaining({settings: expect.objectContaining({
          headerText: 'Chemicals: Discard unsaved settings?',
          deleteButtonId: 'chemicalSettingsDiscardConfirm',
          cancelButtonId: 'chemicalSettingsDiscardConfirmCancel',
        })}),
      }));
      confirmed.emit({});

      expect(answers).toEqual([true]);
      expect(ref.close).toHaveBeenCalled();
    });

    it('stays when the dialog is cancelled', () => {
      const {closed} = mockConfirmDialog();
      component.onStockToggle(true);
      const answers: boolean[] = [];

      component.confirmDiscardChanges().subscribe((ok) => answers.push(ok));
      closed.next(false);

      expect(answers).toEqual([false]);
      expect(component.dirty).toBe(true);
    });

    it('keeps the user on the page while a save is in flight, without a dialog', () => {
      chemicals.saveSettings.mockReturnValue(new Subject());
      component.onStockToggle(true);
      component.save();
      const answers: boolean[] = [];

      component.confirmDiscardChanges().subscribe((ok) => answers.push(ok));

      expect(answers).toEqual([false]);
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('asks the browser to confirm a reload or tab close while edits are unsaved', () => {
      const clean = {preventDefault: vi.fn(), returnValue: undefined as unknown};
      component.onBeforeUnload(clean as unknown as BeforeUnloadEvent);
      expect(clean.preventDefault).not.toHaveBeenCalled();

      component.onStockToggle(true);
      const dirty = {preventDefault: vi.fn(), returnValue: undefined as unknown};
      component.onBeforeUnload(dirty as unknown as BeforeUnloadEvent);

      expect(dirty.preventDefault).toHaveBeenCalled();
      expect(dirty.returnValue).toBe('');
    });
  });

  describe('Copilot review: pending input and superseded responses', () => {
    it('keeps a rejected recipient draft and its error after saving the valid settings', () => {
      component.recipientInput = 'second@example.com, nope';
      component.addRecipientInput();

      component.save();

      expect(component.recipients).toEqual(['first@example.com', 'second@example.com']);
      expect(component.recipientInput).toBe('nope');
      expect(component.invalidRecipients).toEqual(['nope']);
      expect(component.dirty).toBe(true);
    });

    it('counts recipient text typed but not yet added as unsaved', () => {
      component.recipientInput = 'second@exa';
      expect(component.dirty).toBe(true);

      const event = {preventDefault: vi.fn(), returnValue: undefined as unknown};
      component.onBeforeUnload(event as unknown as BeforeUnloadEvent);
      expect(event.preventDefault).toHaveBeenCalled();

      component.recipientInput = '   ';
      expect(component.dirty).toBe(false);
    });

    it('ignores a superseded settings answer after an A to B to A switch', () => {
      const firstA = new Subject<unknown>();
      chemicals.getSettings.mockReturnValueOnce(firstA);
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      chemicals.getSettings.mockReturnValueOnce(of({success: true, model: {propertyId: 4, stockEnabled: false, digestRecipients: []}}));
      property$.next(4);
      chemicals.getSettings.mockReturnValueOnce(of({success: true, model: {propertyId: 3, stockEnabled: false, digestRecipients: ['newer@example.com']}}));
      property$.next(3);
      component.onStockToggle(true);

      firstA.next({success: true, model: {propertyId: 3, stockEnabled: false, digestRecipients: ['older@example.com']}});

      expect(component.stockEnabled).toBe(true);
      expect(component.recipients).toEqual(['newer@example.com']);
      expect(component.dirty).toBe(true);
    });

    it('ignores a superseded settings error after an A to B to A switch', () => {
      const firstA = new Subject<unknown>();
      chemicals.getSettings.mockReturnValueOnce(firstA);
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      chemicals.getSettings.mockReturnValueOnce(of({success: true, model: {propertyId: 4, stockEnabled: false, digestRecipients: []}}));
      property$.next(4);
      property$.next(3);

      firstA.error(new Error('offline'));

      expect(component.load.failed).toBe(false);
      expect(component.ready).toBe(true);
    });

    it('ignores a superseded preview failure that arrives after the refresh from a save', () => {
      const initial = new Subject<unknown>();
      chemicals.getDigestPreview.mockReturnValueOnce(initial);
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      component.onStockToggle(true);

      component.save();
      expect(component.preview).not.toBeNull();
      initial.error(new Error('offline'));

      expect(component.preview).not.toBeNull();
      expect(component.previewFailed).toBe(false);
    });

    it('ignores a superseded preview answer after an A to B to A switch', () => {
      const firstA = new Subject<unknown>();
      chemicals.getDigestPreview.mockReturnValueOnce(firstA);
      property$ = new BehaviorSubject<number | null>(3);
      component = build();
      property$.next(4);
      property$.next(3);

      firstA.next({success: false, message: 'late'});

      expect(component.previewFailed).toBe(false);
      expect(component.preview).not.toBeNull();
    });
  });
});
