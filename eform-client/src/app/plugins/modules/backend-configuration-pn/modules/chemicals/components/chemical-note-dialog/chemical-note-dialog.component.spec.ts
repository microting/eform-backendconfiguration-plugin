import {describe, it, expect, vi} from 'vitest';
import type {Mock} from 'vitest';
import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {discardConfirmHarness} from '../../helpers/chemical-dialogs.testing';
import {ChemicalNoteDialogComponent} from './chemical-note-dialog.component';

describe('ChemicalNoteDialogComponent', () => {
  let chemicals: {updatePlacementNote: Mock};
  let dialogRef: {close: Mock};
  let confirm: ReturnType<typeof discardConfirmHarness>;

  function build(): ChemicalNoteDialogComponent {
    chemicals = {updatePlacementNote: vi.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: vi.fn()};
    confirm = discardConfirmHarness();
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      ...confirm.providers,
      {provide: MAT_DIALOG_DATA, useValue: {row: {placement: {id: 7, placementNote: 'Skab'}, productName: 'Karate'}}},
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

  it('cannot save twice while the first save is in flight', () => {
    const component = build();
    chemicals.updatePlacementNote.mockReturnValue(new Subject());

    component.save();
    expect(component.canSave).toBe(false);
    component.save();

    expect(chemicals.updatePlacementNote).toHaveBeenCalledTimes(1);
  });

  it('stays open when the server refuses the note (the host toasts its message)', () => {
    const component = build();
    chemicals.updatePlacementNote.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel when the note is unchanged (spaces do not count)', () => {
    const component = build();
    component.note = ' Skab ';

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('asks before dropping an edited note (M5)', () => {
    const component = build();
    component.note = 'Aflåst skab';

    component.cancel();

    expect(confirm.confirmId()).toBe('chemicalNoteDiscardConfirm');
    expect(dialogRef.close).not.toHaveBeenCalled();
    confirm.discard();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('does nothing on cancel while the save is in flight (M2)', () => {
    const component = build();
    chemicals.updatePlacementNote.mockReturnValue(new Subject());
    component.note = 'Aflåst skab';
    component.save();

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
