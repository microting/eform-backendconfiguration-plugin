import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {BehaviorSubject, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteOutbreaksPageComponent} from './tail-bite-outbreaks-page.component';

describe('TailBiteOutbreaksPageComponent', () => {
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [], locations: [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'a', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'b', removed: false},
  ]};
  const outbreak = {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: false, openActions: 0, closed: false};
  let service: {getOutbreaks: jest.Mock; getTree: jest.Mock};
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let component: TailBiteOutbreaksPageComponent;

  beforeEach(() => {
    service = {
      getOutbreaks: jest.fn().mockReturnValue(of({success: true, model: [outbreak]})),
      getTree: jest.fn().mockReturnValue(of({success: true, model: tree})),
    };
    params = new BehaviorSubject(convertToParamMap({propertyId: '3'}));
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteOutbreaksPageComponent());
    component.ngOnInit();
  });

  afterEach(() => component.ngOnDestroy());

  it('loads open outbreaks of the property in the URL and titles them from the tree', () => {
    expect(service.getOutbreaks).toHaveBeenCalledWith(3, true);
    expect(component.rows).toEqual([expect.objectContaining({id: 5, title: 'Stald A', status: 'needsAssessment'})]);
    expect(component.loaded).toBe(true);
  });

  it('reloads with closed outbreaks when asked', () => {
    component.setShowClosed(true);
    expect(service.getOutbreaks).toHaveBeenLastCalledWith(3, false);
  });

  it('reloads when the property in the URL changes', () => {
    params.next(convertToParamMap({propertyId: '4'}));
    expect(service.getOutbreaks).toHaveBeenLastCalledWith(4, true);
  });

  it('clears the page at once when the property changes, before the next answer', () => {
    service.getOutbreaks.mockReturnValue(new Subject());
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.rows).toEqual([]);
    expect(component.loaded).toBe(false);
  });

  it('keeps the rows while the toggle\'s list loads, then shows the new list', () => {
    const pending = new Subject<unknown>();
    service.getOutbreaks.mockReturnValue(pending);
    component.setShowClosed(true);
    expect(component.showClosed).toBe(true);
    expect(component.rows).toHaveLength(1);
    pending.next({success: true, model: []});
    pending.complete();
    expect(component.rows).toEqual([]);
    expect(component.loaded).toBe(true);
  });

  it('turns the toggle back to the value the shown rows were loaded with when the second of two quick flips fails', () => {
    // Rows loaded with the toggle off; on (pending), off again (refused): the toggle must match the rows (off).
    const first = new Subject<unknown>();
    service.getOutbreaks.mockReturnValueOnce(first).mockReturnValueOnce(of({success: false, message: 'no'}));
    component.setShowClosed(true);
    component.setShowClosed(false);
    expect(component.showClosed).toBe(false);
    expect(component.rows).toHaveLength(1);
    first.next({success: true, model: []});
    first.complete();
    expect(component.rows).toHaveLength(1);
    expect(component.showClosed).toBe(false);
  });

  it('does the same the other way round: rows loaded with the toggle on, off (pending), on again (fails)', () => {
    service.getOutbreaks.mockReturnValueOnce(of({success: true, model: []}));
    component.setShowClosed(true);
    expect(component.rows).toEqual([]);
    const pending = new Subject<unknown>();
    service.getOutbreaks.mockReturnValueOnce(pending).mockReturnValueOnce(throwError(() => new Error('offline')));
    component.setShowClosed(false);
    component.setShowClosed(true);
    expect(component.showClosed).toBe(true);
    expect(component.rows).toEqual([]);
  });

  it('keeps the rows and turns the toggle back when the toggle\'s list is refused or fails', () => {
    service.getOutbreaks.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
    component.setShowClosed(true);
    expect(component.rows).toHaveLength(1);
    expect(component.showClosed).toBe(false);
    service.getOutbreaks.mockReturnValue(throwError(() => new Error('offline')));
    component.setShowClosed(true);
    expect(component.rows).toHaveLength(1);
    expect(component.showClosed).toBe(false);
  });

  it('shows nothing, and no "no outbreaks" claim, when the first load is refused', () => {
    service.getOutbreaks.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.rows).toEqual([]);
    expect(component.loaded).toBe(false);
  });

  it('keeps the list when a refresh is refused', () => {
    service.getOutbreaks.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
    component.load();
    expect(component.rows).toHaveLength(1);
  });

  it('keeps the list when the tree is refused on a refresh', () => {
    service.getTree.mockReturnValue(of({success: false, message: 'no'}));
    component.load();
    expect(component.rows).toEqual([expect.objectContaining({id: 5, title: 'Stald A'})]);
  });

  it('keeps the list when a refresh errors, and does not throw', () => {
    service.getOutbreaks.mockReturnValue(throwError(() => new Error('offline')));
    expect(() => component.load()).not.toThrow();
    expect(component.rows).toHaveLength(1);
  });

  it('stays empty and not loaded when the first load errors', () => {
    service.getOutbreaks.mockReturnValue(throwError(() => new Error('offline')));
    params.next(convertToParamMap({propertyId: '4'}));
    expect(component.rows).toEqual([]);
    expect(component.loaded).toBe(false);
  });

  it('drops a late answer for the property the user has left', () => {
    const late = new Subject<unknown>();
    service.getOutbreaks.mockReturnValue(late);
    params.next(convertToParamMap({propertyId: '4'}));
    service.getOutbreaks.mockReturnValue(of({success: true, model: []}));
    params.next(convertToParamMap({propertyId: '5'}));
    late.next({success: true, model: [outbreak]});
    late.complete();
    expect(component.rows).toEqual([]);
    expect(component.loaded).toBe(true);
  });

  it('drops a late answer for the old toggle value', () => {
    const late = new Subject<unknown>();
    service.getOutbreaks.mockReturnValue(late);
    component.setShowClosed(true);
    service.getOutbreaks.mockReturnValue(of({success: true, model: []}));
    component.setShowClosed(false);
    late.next({success: true, model: [outbreak]});
    late.complete();
    expect(component.rows).toEqual([]);
  });
});
