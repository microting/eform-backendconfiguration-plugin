import {TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {EventEmitter} from '@angular/core';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, NEVER, Subject, of, throwError} from 'rxjs';
import {ToastrService} from 'ngx-toastr';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteLocationsPageComponent} from './tail-bite-locations-page.component';
import {buildQrSheetPdf} from './tail-bite-qr-pdf';

jest.mock('./tail-bite-qr-pdf', () => ({
  ...jest.requireActual('./tail-bite-qr-pdf'),
  buildQrSheetPdf: jest.fn(),
}));

describe('TailBiteLocationsPageComponent', () => {
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [], locations: [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'qa', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'qb', removed: false},
    {id: 3, parentId: 2, name: 'Sektion 4', depth: 2, sortOrder: 0, qrCode: 'qc', removed: false},
  ]};
  let service: Record<string, jest.Mock>;
  let dialogOpen: jest.Mock;
  let component: TailBiteLocationsPageComponent;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let toastr: {error: jest.Mock};
  const ok = () => jest.fn().mockReturnValue(of({success: true}));

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({propertyId: '3'}));
    toastr = {error: jest.fn()};
    service = {
      getTree: jest.fn().mockReturnValue(of({success: true, model: tree})),
      getRules: jest.fn().mockReturnValue(of({success: true, model: [
        {id: 9, locationId: 1, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1, version: 1, updatedAt: null}]})),
      getOccupancy: jest.fn().mockReturnValue(of({success: true, model: [
        {locationId: 3, pigCount: 360, source: 0, validFrom: '2026-09-15T00:00:00Z'}]})),
      createLocation: ok(), renameLocation: ok(), moveLocation: ok(), deleteLocation: ok(), createPenRange: ok(), setOccupancy: ok(),
    };
    dialogOpen = jest.fn();
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
        {provide: ToastrService, useValue: toastr},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (k: string, p?: Record<string, unknown>) =>
          k.replace(/{{(\w+)}}/g, (_m, n) => String(p?.[n]))}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteLocationsPageComponent());
    component.ngOnInit();
  });

  afterEach(() => component.ngOnDestroy());

  // The text dialog emits `saved` and is closed by askText on success; the delete modal emits `delete` on confirm.
  /** The next text dialog: `save(text)` presses Save with that text, `cancel()` closes it; `close` is the ref's close. */
  const textDialog = () => {
    const saved = new Subject<string>();
    const closed = new Subject<void>();
    const close = jest.fn(() => closed.next());
    dialogOpen.mockReturnValueOnce({componentInstance: {saved}, close, afterClosed: () => closed});
    return {save: (text: string) => saved.next(text), cancel: () => closed.next(), close};
  };
  const confirmDialog = () => {
    const clicks = new EventEmitter<unknown>();
    dialogOpen.mockReturnValueOnce({componentInstance: {delete: clicks}, close: jest.fn(), afterClosed: () => NEVER});
    return () => clicks.emit({});
  };

  it('loads tree, rules and pig counts, selects the root and defaults the pen prefix', () => {
    expect(component.rows.map((r) => r.node.name)).toEqual(['Ejendom', 'Stald A', 'Sektion 4']);
    expect(component.selectedId).toBe(1);
    expect(component.penPrefix).toBe('Pen');
    expect(component.ruleLabel(component.rows[0])).toBe('Own rule: 5 bitten pigs or 1 severe within 7 days, counted per level 1');
    expect(component.ruleLabel(component.rows[2])).toBe('Inherited from Ejendom');
  });

  it('prefills the pig count only from the own count of the selected location', () => {
    component.select(component.rows[2]);
    expect(component.pigCount).toBe(360);
    component.select(component.rows[1]);
    expect(component.pigCount).toBeNull();
  });

  it('adds a child under the selected location with the typed name', () => {
    component.select(component.rows[2]);
    const dialog = textDialog();
    component.addChild();
    dialog.save('Sti 309');
    expect(service.createLocation).toHaveBeenCalledWith(3, 'Sti 309');
    expect(service.getTree).toHaveBeenCalledTimes(2);
  });

  it('renames and moves the selected location', () => {
    component.select(component.rows[2]);
    const dialog = textDialog();
    component.rename();
    dialog.save('Sektion 5');
    expect(service.renameLocation).toHaveBeenCalledWith(3, 'Sektion 5');
    expect(dialog.close).toHaveBeenCalled();
    expect(component.moveOptions.map((n) => n.path)).toEqual(['Ejendom']);
    component.move(1);
    expect(service.moveLocation).toHaveBeenCalledWith(3, 1);
  });

  it('deletes only after confirmation, then selects the parent', () => {
    component.select(component.rows[2]);
    confirmDialog();
    component.remove();
    expect(service.deleteLocation).not.toHaveBeenCalled();
    expect(component.selectedId).toBe(3);
    const confirm = confirmDialog();
    component.remove();
    confirm();
    expect(service.deleteLocation).toHaveBeenCalledWith(3);
    expect(component.selectedId).toBe(2);
  });

  it('never deletes the root', () => {
    component.select(component.rows[0]);
    component.remove();
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('creates a pen range only when it is valid and within the server limit', () => {
    component.select(component.rows[2]);
    component.penPrefix = 'Sti';
    component.penFrom = 312;
    component.penTo = 301;
    expect(component.penRangeValid).toBe(false);
    component.penFrom = 1;
    component.penTo = 501;
    expect(component.penRangeValid).toBe(false);
    component.penFrom = 301;
    component.penTo = 312;
    expect(component.penCount).toBe(12);
    component.createPens();
    expect(service.createPenRange).toHaveBeenCalledWith(3, 'Sti', 301, 312);
  });

  it('accepts today as the default valid-from day', () => {
    component.select(component.rows[2]);
    expect(component.pigCountValid).toBe(true);
  });

  it('saves a pig count from midnight UTC of the picked day, and refuses a future day', () => {
    component.select(component.rows[2]);
    component.pigCount = 380;
    component.validFrom = new Date(2026, 8, 15);
    component.savePigs();
    expect(service.setOccupancy).toHaveBeenCalledWith(3, 380, '2026-09-15T00:00:00Z');
    component.validFrom = new Date(Date.now() + 3 * 86400000);
    expect(component.pigCountValid).toBe(false);
  });

  it('checks and unchecks rows for QR printing', () => {
    component.setAllChecked(true);
    expect(component.allChecked).toBe(true);
    component.setChecked(2, false);
    expect(component.allChecked).toBe(false);
    expect(component.checked.size).toBe(2);
  });

  it('sends one request when rename is clicked twice while busy', () => {
    component.select(component.rows[2]);
    textDialog();
    component.rename();
    component.rename();
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(true);
  });

  it('frees the page when the rename dialog is cancelled', () => {
    component.select(component.rows[2]);
    const dialog = textDialog();
    component.rename();
    dialog.cancel();
    expect(service['renameLocation']).not.toHaveBeenCalled();
    expect(component.busy).toBe(false);
  });

  it('reloads the tree and frees the page when a delete fails with an error', () => {
    service['deleteLocation'].mockReturnValue(throwError(() => new Error('down')));
    component.select(component.rows[2]);
    const confirm = confirmDialog();
    component.remove();
    expect(() => confirm()).not.toThrow();
    expect(service.getTree).toHaveBeenCalledTimes(2);
    expect(component.busy).toBe(false);
  });

  it('reloads the tree and keeps the dialog open with the typed name when a rename is refused or fails', () => {
    service['renameLocation']
      .mockReturnValueOnce(of({success: false}))
      .mockReturnValueOnce(throwError(() => new Error('down')))
      .mockReturnValueOnce(of({success: true}));
    component.select(component.rows[2]);
    const dialog = textDialog();
    component.rename();
    dialog.save('Sektion 5');
    expect(service.getTree).toHaveBeenCalledTimes(2);
    expect(dialog.close).not.toHaveBeenCalled();
    expect(component.busy).toBe(true);
    dialog.save('Sektion 5');
    expect(service.getTree).toHaveBeenCalledTimes(3);
    expect(dialog.close).not.toHaveBeenCalled();
    dialog.save('Sektion 6');
    expect(service['renameLocation']).toHaveBeenLastCalledWith(3, 'Sektion 6');
    expect(dialog.close).toHaveBeenCalled();
    expect(service.getTree).toHaveBeenCalledTimes(4);
    expect(component.busy).toBe(false);
  });

  it('frees the page when a refused rename dialog is then cancelled', () => {
    service['renameLocation'].mockReturnValue(of({success: false}));
    component.select(component.rows[2]);
    const dialog = textDialog();
    component.rename();
    dialog.save('Sektion 5');
    dialog.cancel();
    expect(component.busy).toBe(false);
  });

  it('reloads the tree when a move fails with an error, and clears the move select', () => {
    service['moveLocation'].mockReturnValue(throwError(() => new Error('conflict')));
    component.select(component.rows[2]);
    component.moveTarget = 1;
    component.move(1);
    expect(component.moveTarget).toBeNull();
    expect(service.getTree).toHaveBeenCalledTimes(2);
    expect(component.busy).toBe(false);
  });

  describe('printing QR labels', () => {
    let clicked: string[];
    beforeEach(() => {
      clicked = [];
      (buildQrSheetPdf as jest.Mock).mockReset().mockResolvedValue(new Uint8Array([1]));
      (URL as unknown as Record<string, unknown>)['createObjectURL'] = jest.fn().mockReturnValue('blob:x');
      (URL as unknown as Record<string, unknown>)['revokeObjectURL'] = jest.fn();
      jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        clicked.push(this.download);
      });
    });
    afterEach(() => jest.restoreAllMocks());

    it('leaves removed nodes out and names the file after today', async () => {
      component.tree = {...tree, locations: tree.locations.map((l) => (l.id === 3 ? {...l, removed: true} : l))};
      component.rows = component.rows.map((r) => (r.node.id === 3 ? {...r, node: {...r.node, removed: true}} : r));
      component.setAllChecked(true);
      await component.printQr();
      const labels = (buildQrSheetPdf as jest.Mock).mock.calls[0][0] as {title: string}[];
      expect(labels.map((l) => l.title).join('|')).not.toContain('Sektion 4');
      expect(clicked[0]).toMatch(/^halebid-qr-\d{4}-\d{2}-\d{2}\.pdf$/);
      expect(component.busy).toBe(false);
    });

    it('shows an error toast and frees the page when the sheet cannot be built', async () => {
      (buildQrSheetPdf as jest.Mock).mockRejectedValue(new Error('boom'));
      component.setChecked(2, true);
      await component.printQr();
      expect(toastr.error).toHaveBeenCalledWith('Could not create the QR sheet');
      expect(component.busy).toBe(false);
    });

    it('does nothing while busy', async () => {
      component.setChecked(2, true);
      component.busy = true;
      await component.printQr();
      expect(buildQrSheetPdf).not.toHaveBeenCalled();
    });
  });

  it('clears rows and entered values at once when the property changes', () => {
    component.penFrom = 1;
    service['getTree'].mockReturnValue(NEVER);
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.rows).toEqual([]);
    expect(component.selectedId).toBeNull();
    expect(component.penFrom).toBeNull();
  });

  it('resets the valid-from day to today when another row is selected', () => {
    component.select(component.rows[2]);
    component.validFrom = new Date(2026, 0, 2);
    component.select(component.rows[1]);
    expect(component.validFrom.toDateString()).toBe(new Date().toDateString());
  });

  it('keeps the previous rows when a refresh has the rules call refused', () => {
    service['getRules'].mockReturnValueOnce(of({success: false, message: 'no'}));
    component.load();
    expect(component.rows.map((r) => r.node.name)).toEqual(['Ejendom', 'Stald A', 'Sektion 4']);
    expect(component.ruleLabel(component.rows[0])).toContain('Own rule');
  });

  it('keeps the previous rows when a refresh has the pig counts or the tree refused', () => {
    service['getOccupancy'].mockReturnValueOnce(of({success: false, message: 'no'}));
    component.load();
    service['getTree'].mockReturnValueOnce(of({success: false, message: 'no'}));
    component.load();
    expect(component.tree).not.toBeNull();
    expect(component.rows.length).toBe(3);
    expect(component.rows[2].pigs).toBe(360);
  });

  it('keeps the selection and the checked rows when a refresh fails with an error', () => {
    component.select(component.rows[1]);
    component.setChecked(3, true);
    service['getTree'].mockReturnValueOnce(throwError(() => new Error('500')));
    component.load();
    expect(component.rows.length).toBe(3);
    expect(component.selectedId).toBe(2);
    expect(component.isChecked(3)).toBe(true);
  });

  it('keeps the newest answer when an older load answers last', () => {
    const olderTree = new Subject<unknown>();
    service['getTree'].mockReturnValueOnce(olderTree);
    component.load();
    service['getTree'].mockReturnValueOnce(of({success: true, model: {...tree, locations: tree.locations.slice(0, 2)}}));
    component.load();
    expect(component.rows.length).toBe(2);
    olderTree.next({success: true, model: tree});
    olderTree.complete();
    expect(component.rows.length).toBe(2);
  });
});
