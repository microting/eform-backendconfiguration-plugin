import {of} from 'rxjs';
import {AdhocAreaAdminModalComponent} from './adhoc-area-admin-modal.component';

describe('AdhocAreaAdminModalComponent', () => {
  let dialogRefSpy: any;
  let adhocStateServiceSpy: any;
  let component: AdhocAreaAdminModalComponent;

  const areas = [
    {id: 10, propertyId: 1, name: 'Stald 1'},
    {id: 11, propertyId: 1, name: 'Stald 2'},
  ];

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    adhocStateServiceSpy = {
      getAreasForProperty: jest.fn(),
      getCachedAreas: jest.fn(),
      renameArea: jest.fn(),
      deleteArea: jest.fn(),
    };
    adhocStateServiceSpy.getAreasForProperty.mockReturnValue(of(areas));
    component = new AdhocAreaAdminModalComponent(dialogRefSpy, {propertyId: 1, propertyName: 'Gård Nord'}, adhocStateServiceSpy);
  });

  it('ngOnInit loads areas for the property (rows render from this list)', () => {
    component.ngOnInit();
    expect(adhocStateServiceSpy.getAreasForProperty).toHaveBeenCalledWith(1);
    expect(component.areas).toEqual(areas);
  });

  it('startRename seeds editingId/editName and clears any error', () => {
    component.errorKey = 'Area name is empty or already exists';
    component.startRename(areas[0]);
    expect(component.editingId).toBe(10);
    expect(component.editName).toBe('Stald 1');
    expect(component.errorKey).toBeNull();
  });

  it('saveRename calls renameArea and refreshes areas from the cache on success', () => {
    const refreshed = [{id: 10, propertyId: 1, name: 'Stald renamed'}, areas[1]];
    adhocStateServiceSpy.renameArea.mockReturnValue(of(true));
    adhocStateServiceSpy.getCachedAreas.mockReturnValue(refreshed);
    component.editingId = 10;
    component.editName = 'Stald renamed';
    component.saveRename(areas[0]);
    expect(adhocStateServiceSpy.renameArea).toHaveBeenCalledWith(1, 10, 'Stald renamed');
    expect(adhocStateServiceSpy.getCachedAreas).toHaveBeenCalledWith(1);
    expect(component.areas).toEqual(refreshed);
    expect(component.editingId).toBeNull();
    expect(component.changed).toBe(true);
    expect(component.busy).toBe(false);
  });

  it('saveRename surfaces an error key and leaves editingId set on failure', () => {
    adhocStateServiceSpy.renameArea.mockReturnValue(of(false));
    component.editingId = 10;
    component.editName = 'Stald 2';
    component.saveRename(areas[0]);
    expect(component.errorKey).toBe('Area name is empty or already exists');
    expect(component.editingId).toBe(10);
    expect(component.changed).toBe(false);
  });

  it('saveRename is a no-op for a blank trimmed name', () => {
    component.editName = '   ';
    component.saveRename(areas[0]);
    expect(adhocStateServiceSpy.renameArea).not.toHaveBeenCalled();
  });

  it('askDelete/cancelDelete toggle the inline confirm step', () => {
    component.askDelete(areas[0]);
    expect(component.confirmDeleteArea).toEqual(areas[0]);
    component.cancelDelete();
    expect(component.confirmDeleteArea).toBeNull();
  });

  it('confirmDelete calls deleteArea, refreshes from cache, and closes the confirm step on success', () => {
    const refreshed = [areas[1]];
    adhocStateServiceSpy.deleteArea.mockReturnValue(of(true));
    adhocStateServiceSpy.getCachedAreas.mockReturnValue(refreshed);
    component.confirmDeleteArea = areas[0];
    component.confirmDelete();
    expect(adhocStateServiceSpy.deleteArea).toHaveBeenCalledWith(1, 10);
    expect(component.areas).toEqual(refreshed);
    expect(component.confirmDeleteArea).toBeNull();
    expect(component.changed).toBe(true);
  });

  it('confirmDelete surfaces an error key and leaves the confirm step open on failure', () => {
    adhocStateServiceSpy.deleteArea.mockReturnValue(of(false));
    component.confirmDeleteArea = areas[0];
    component.confirmDelete();
    expect(component.errorKey).toBe('Failed to delete area');
    expect(component.confirmDeleteArea).toEqual(areas[0]);
    expect(component.changed).toBe(false);
    expect(component.busy).toBe(false);
  });

  it('confirmDelete does nothing without a pending confirm target', () => {
    component.confirmDeleteArea = null;
    component.confirmDelete();
    expect(adhocStateServiceSpy.deleteArea).not.toHaveBeenCalled();
  });

  it('close() closes the dialog with the changed flag', () => {
    component.changed = true;
    component.close();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });
});
