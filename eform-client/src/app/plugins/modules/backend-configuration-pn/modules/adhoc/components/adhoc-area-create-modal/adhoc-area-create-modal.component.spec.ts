import {of, throwError} from 'rxjs';
import {AdhocAreaCreateModalComponent} from './adhoc-area-create-modal.component';

describe('AdhocAreaCreateModalComponent', () => {
  let dialogRefSpy: any;
  let adhocStateServiceSpy: any;
  let component: AdhocAreaCreateModalComponent;

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    adhocStateServiceSpy = {createAreas: jest.fn()};
    component = new AdhocAreaCreateModalComponent(dialogRefSpy, {propertyId: 1, propertyName: 'Gård Nord'}, adhocStateServiceSpy);
  });

  it('parsedNames trims whitespace and drops empty lines', () => {
    component.namesText = '  Stald 1 \n\n Stald 2\n   \nStald 3';
    expect(component.parsedNames).toEqual(['Stald 1', 'Stald 2', 'Stald 3']);
  });

  it('hide() closes with false', () => {
    component.hide();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });

  it('save() is a no-op when parsedNames is empty', () => {
    component.namesText = '   \n  ';
    component.save();
    expect(adhocStateServiceSpy.createAreas).not.toHaveBeenCalled();
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });

  it('save() calls createAreas(propertyId, names) and closes with true on success', () => {
    adhocStateServiceSpy.createAreas.mockReturnValue(of([{id: 10, propertyId: 1, name: 'Stald 1'}]));
    component.namesText = 'Stald 1';
    component.save();
    expect(adhocStateServiceSpy.createAreas).toHaveBeenCalledWith(1, ['Stald 1']);
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
    // The component resets `saving` in `next` BEFORE closing, so after the
    // synchronous of(...) emission the flag is back to false — it must not
    // stay latched (a stuck-true flag would permanently disable save()).
    expect(component.saving).toBe(false);
  });

  it('save() resets saving on error, surfaces an errorKey, and does not close', () => {
    adhocStateServiceSpy.createAreas.mockReturnValue(throwError(() => new Error('fail')));
    component.namesText = 'Stald 1';
    component.save();
    expect(component.saving).toBe(false);
    expect(component.errorKey).toBe('Failed to create areas');
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });

  it('save() surfaces an errorKey and keeps the modal open when the create reports failure (empty result)', () => {
    adhocStateServiceSpy.createAreas.mockReturnValue(of([]));
    component.namesText = 'Stald 1';
    component.save();
    expect(component.saving).toBe(false);
    expect(component.errorKey).toBe('Failed to create areas');
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });

  it('save() does nothing while already saving', () => {
    component.namesText = 'Stald 1';
    component.saving = true;
    component.save();
    expect(adhocStateServiceSpy.createAreas).not.toHaveBeenCalled();
  });
});
