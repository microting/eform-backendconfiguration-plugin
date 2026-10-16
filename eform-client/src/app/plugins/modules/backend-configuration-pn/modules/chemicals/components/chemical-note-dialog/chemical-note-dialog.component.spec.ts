import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalNoteDialogComponent} from './chemical-note-dialog.component';

describe('ChemicalNoteDialogComponent', () => {
  let chemicals: {updatePlacementNote: jest.Mock};
  let dialogRef: {close: jest.Mock};

  function build(): ChemicalNoteDialogComponent {
    chemicals = {updatePlacementNote: jest.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: MAT_DIALOG_DATA, useValue: {row: {placement: {id: 7, placementNote: 'Skab'}}}},
    ]});
    return runInInjectionContext(injector, () => new ChemicalNoteDialogComponent());
  }

  it('starts from the current note and saves the trimmed one', () => {
    const component = build();
    expect(component.note).toBe('Skab');

    component.note = ' Aflåst skab ';
    component.save();

    expect(chemicals.updatePlacementNote).toHaveBeenCalledWith(7, {placementNote: 'Aflåst skab'});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('stays open when the server refuses the note (the host toasts its message)', () => {
    const component = build();
    chemicals.updatePlacementNote.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel', () => {
    const component = build();

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
