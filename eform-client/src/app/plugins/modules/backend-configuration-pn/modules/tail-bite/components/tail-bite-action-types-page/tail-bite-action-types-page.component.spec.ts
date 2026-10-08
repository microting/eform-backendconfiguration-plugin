import {Overlay} from '@angular/cdk/overlay';
import {EventEmitter} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, NEVER, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteActionTypesPageComponent} from './tail-bite-action-types-page.component';

describe('TailBiteActionTypesPageComponent', () => {
  let service: Record<string, jest.Mock>;
  let dialogOpen: jest.Mock;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let component: TailBiteActionTypesPageComponent;

  const listOf = (...names: string[]) => ({success: true, model: names.map((name, i) => ({id: 6 + i, code: `C${i}`, name, sortOrder: i}))});

  beforeEach(() => {
    service = {
      getActionTypes: jest.fn().mockReturnValue(of(listOf('Halm'))),
      createActionType: jest.fn().mockReturnValue(of({success: true, model: 7})),
      renameActionType: jest.fn().mockReturnValue(of({success: true})),
      deleteActionType: jest.fn().mockReturnValue(of({success: true})),
    };
    dialogOpen = jest.fn();
    params = new BehaviorSubject(convertToParamMap({propertyId: '3'}));
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (k: string) => k}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteActionTypesPageComponent());
    component.ngOnInit();
  });

  afterEach(() => component.ngOnDestroy());

  const openDeleteDialog = () => {
    const confirmed = new EventEmitter<unknown>();
    const close = jest.fn();
    dialogOpen.mockReturnValueOnce({componentInstance: {delete: confirmed}, close, afterClosed: () => NEVER});
    component.remove(component.actionTypes[0]);
    return {confirmed, close};
  };

  it('loads the action types of the property in the URL', () => {
    expect(service.getActionTypes).toHaveBeenCalledWith(3);
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Halm']);
  });

  it('keeps the previous list when a refresh is refused or fails', () => {
    service.getActionTypes.mockReturnValueOnce(of({success: false, message: 'no'}));
    component.load();
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Halm']);
    service.getActionTypes.mockReturnValueOnce(throwError(() => new Error('boom')));
    component.load();
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Halm']);
  });

  it('clears the page at once on a property switch and drops late answers for the old property', () => {
    const late = new Subject<unknown>();
    service.getActionTypes.mockReturnValueOnce(late).mockReturnValueOnce(of(listOf('Reb')));
    component.load();
    component.newName = 'typed';
    params.next(convertToParamMap({propertyId: '4'}));
    expect(service.getActionTypes).toHaveBeenLastCalledWith(4);
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Reb']);
    expect(component.newName).toBe('');
    late.next(listOf('Old'));
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Reb']);
  });

  it('empties the list immediately while the next property loads', () => {
    service.getActionTypes.mockReturnValueOnce(NEVER);
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.actionTypes).toEqual([]);
  });

  it('adds a trimmed name, clears the field and reloads; ignores a blank name', () => {
    component.newName = '   ';
    component.add();
    expect(service.createActionType).not.toHaveBeenCalled();
    component.newName = ' Ekstra rodemateriale ';
    component.add();
    expect(service.createActionType).toHaveBeenCalledWith(3, 'Ekstra rodemateriale');
    expect(component.newName).toBe('');
    expect(component.busy).toBe(false);
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
  });

  it('keeps the typed name and reloads when the server refuses it', () => {
    service.createActionType.mockReturnValue(of({success: false, message: "The action 'Halm' already exists."}));
    component.newName = 'Halm';
    component.add();
    expect(component.newName).toBe('Halm');
    expect(component.busy).toBe(false);
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
  });

  it('keeps the typed name, reloads and resets busy when the add errors', () => {
    service.createActionType.mockReturnValue(throwError(() => new Error('boom')));
    component.newName = 'Halm';
    component.add();
    expect(component.newName).toBe('Halm');
    expect(component.busy).toBe(false);
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
  });

  it('is single-flight: a second add while one is running is ignored', () => {
    service.createActionType.mockReturnValue(NEVER);
    component.newName = 'A';
    component.add();
    expect(component.busy).toBe(true);
    component.add();
    expect(service.createActionType).toHaveBeenCalledTimes(1);
  });

  /** The next text dialog: `save(text)` presses Save with that text, `cancel()` closes it; `close` is the ref's close. */
  const textDialog = () => {
    const saved = new Subject<string>();
    const closed = new Subject<void>();
    const close = jest.fn(() => closed.next());
    dialogOpen.mockReturnValueOnce({componentInstance: {saved}, close, afterClosed: () => closed});
    return {save: (text: string) => saved.next(text), cancel: () => closed.next(), close};
  };

  it('renames through the text dialog, closes it and reloads', () => {
    const dialog = textDialog();
    component.rename(component.actionTypes[0]);
    dialog.save('Halm i hækken');
    expect(service.renameActionType).toHaveBeenCalledWith(6, 'Halm i hækken');
    expect(dialog.close).toHaveBeenCalled();
    expect(component.busy).toBe(false);
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
  });

  it('a cancelled rename dialog calls nothing and resets busy', () => {
    const dialog = textDialog();
    component.rename(component.actionTypes[0]);
    dialog.cancel();
    expect(service.renameActionType).not.toHaveBeenCalled();
    expect(component.busy).toBe(false);
  });

  it('a refused or failed rename reloads and keeps the dialog open for a retry; closing it resets busy', () => {
    const dialog = textDialog();
    service.renameActionType.mockReturnValueOnce(of({success: false, message: 'exists'}));
    component.rename(component.actionTypes[0]);
    dialog.save('Reb');
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
    expect(dialog.close).not.toHaveBeenCalled();
    expect(component.busy).toBe(true);
    service.renameActionType.mockReturnValueOnce(throwError(() => new Error('boom')));
    dialog.save('Reb');
    expect(service.getActionTypes).toHaveBeenCalledTimes(3);
    expect(dialog.close).not.toHaveBeenCalled();
    dialog.cancel();
    expect(component.busy).toBe(false);
  });

  it('deletes after confirming in the platform delete modal', () => {
    const {confirmed, close} = openDeleteDialog();
    expect(service.deleteActionType).not.toHaveBeenCalled();
    confirmed.emit({});
    expect(service.deleteActionType).toHaveBeenCalledWith(6);
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
    expect(close).toHaveBeenCalled();
    expect(component.busy).toBe(false);
  });

  it('a refused delete reloads, keeps the dialog open and resets busy', () => {
    service.deleteActionType.mockReturnValue(of({success: false, message: 'in use'}));
    const {confirmed, close} = openDeleteDialog();
    confirmed.emit({});
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
    expect(close).not.toHaveBeenCalled();
    expect(component.busy).toBe(false);
  });

  it('a failed delete reloads, resets busy and raises no unhandled error', () => {
    service.deleteActionType.mockReturnValue(throwError(() => new Error('boom')));
    const {confirmed} = openDeleteDialog();
    expect(() => confirmed.emit({})).not.toThrow();
    expect(service.getActionTypes).toHaveBeenCalledTimes(2);
    expect(component.busy).toBe(false);
  });

  it('keeps the newest answer when an older load answers last', () => {
    const older = new Subject<unknown>();
    service.getActionTypes.mockReturnValueOnce(older).mockReturnValueOnce(of(listOf('Reb')));
    component.load();
    component.load();
    older.next(listOf('Old'));
    expect(component.actionTypes.map((a) => a.name)).toEqual(['Reb']);
  });
});
