import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {discardConfirmHarness} from '../../helpers/chemical-dialogs.testing';
import {ChemicalLocationDialogComponent} from './chemical-location-dialog.component';

describe('ChemicalLocationDialogComponent', () => {
  let chemicals: Record<string, jest.Mock>;
  let dialogRef: {close: jest.Mock};
  let confirm: ReturnType<typeof discardConfirmHarness>;

  const existing = {location: {id: 2, name: 'Kemirum', description: 'Reoler'}, openCount: 2, actionCount: 0};

  function build(row: unknown): ChemicalLocationDialogComponent {
    chemicals = {
      createLocation: jest.fn().mockReturnValue(of({success: true, model: {id: 44}})),
      updateLocation: jest.fn().mockReturnValue(of({success: true, model: {id: 2}})),
      uploadLocationPhoto: jest.fn().mockReturnValue(of({success: true, model: {id: 44}})),
    };
    dialogRef = {close: jest.fn()};
    confirm = discardConfirmHarness();
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      ...confirm.providers,
      {provide: MAT_DIALOG_DATA, useValue: {propertyId: 3, row, photoUrl: null}},
    ]});
    return runInInjectionContext(injector, () => new ChemicalLocationDialogComponent());
  }

  it('creates a location, then uploads the chosen photo to it', () => {
    const component = build(null);
    component.name = ' Maskinhus ';
    const file = new File(['x'], 'm.jpg', {type: 'image/jpeg'});
    component.onFile(file);

    component.save();

    expect(chemicals.createLocation).toHaveBeenCalledWith({propertyId: 3, name: 'Maskinhus', description: '', sortOrder: null});
    expect(chemicals.uploadLocationPhoto).toHaveBeenCalledWith(44, file);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('updates name and description of an existing location without touching the order', () => {
    const component = build(existing);
    component.name = 'Kemirum 2';

    component.save();

    expect(chemicals.updateLocation).toHaveBeenCalledWith(2, {name: 'Kemirum 2', description: 'Reoler', sortOrder: null});
    expect(chemicals.uploadLocationPhoto).not.toHaveBeenCalled();
    expect(component.openCount).toBe(2);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('refuses an empty name and an invalid photo', () => {
    const component = build(null);
    component.name = '   ';
    expect(component.canSave).toBe(false);

    component.name = 'X';
    component.onFile(new File(['x'], 'a.gif', {type: 'image/gif'}));
    expect(component.photoError).toBe('type');
    expect(component.canSave).toBe(false);

    component.onFile(new File(['x'], 'a.png', {type: 'image/png'}));
    expect(component.photoError).toBeNull();
    expect(component.canSave).toBe(true);
  });

  it('uploads no photo and stays open when the server refuses the location (the host toasts its message)', () => {
    const component = build(null);
    chemicals.createLocation.mockReturnValue(of({success: false, message: 'Navnet findes allerede'}));
    component.name = 'Lade';
    component.onFile(new File(['x'], 'm.jpg', {type: 'image/jpeg'}));

    component.save();

    expect(chemicals.uploadLocationPhoto).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('stays open when the photo is refused, and a retry updates the created location instead of creating it again', () => {
    const component = build(null);
    chemicals.uploadLocationPhoto.mockReturnValueOnce(of({success: false, message: 'Fotoet kunne ikke gemmes'}));
    component.name = 'Maskinhus';
    const file = new File(['x'], 'm.jpg', {type: 'image/jpeg'});
    component.onFile(file);

    component.save();
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);

    component.save();

    expect(chemicals.createLocation).toHaveBeenCalledTimes(1);
    expect(chemicals.updateLocation).toHaveBeenCalledWith(44, {name: 'Maskinhus', description: '', sortOrder: null});
    expect(chemicals.uploadLocationPhoto).toHaveBeenLastCalledWith(44, file);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('cannot save twice while the first save is in flight', () => {
    const component = build(null);
    chemicals.createLocation.mockReturnValue(new Subject());
    component.name = 'Lade';

    component.save();
    expect(component.canSave).toBe(false);
    component.save();

    expect(chemicals.createLocation).toHaveBeenCalledTimes(1);
  });

  it('closes at once on cancel when nothing was typed', () => {
    const component = build(existing);

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('asks before dropping a typed name, description or chosen photo', () => {
    const component = build(existing);
    component.description = 'Reoler og skab';

    component.cancel();
    expect(confirm.confirmId()).toBe('chemicalLocationDiscardConfirm');
    confirm.keepEditing();
    expect(dialogRef.close).not.toHaveBeenCalled();

    component.cancel();
    confirm.discard();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('counts a chosen photo as a change', () => {
    const component = build(existing);
    expect(component.dirty).toBe(false);

    component.onFile(new File(['x'], 'm.jpg', {type: 'image/jpeg'}));

    expect(component.dirty).toBe(true);
  });

  it('does nothing on cancel while a save is in flight', () => {
    const component = build(null);
    chemicals.createLocation.mockReturnValue(new Subject());
    component.name = 'Lade';
    component.save();

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('tells the page to reload when it is closed after the location was created but the photo was refused', () => {
    const component = build(null);
    chemicals.uploadLocationPhoto.mockReturnValue(of({success: false, message: 'Fotoet kunne ikke gemmes'}));
    component.name = 'Maskinhus';
    component.onFile(new File(['x'], 'm.jpg', {type: 'image/jpeg'}));
    component.save();

    component.onFile(null);
    component.cancel();
    confirm.discard();

    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
