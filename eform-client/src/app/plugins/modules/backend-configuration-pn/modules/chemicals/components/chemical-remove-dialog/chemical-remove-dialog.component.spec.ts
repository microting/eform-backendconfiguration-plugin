import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
import {ChemicalRemovalReason} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalRemoveDialogComponent} from './chemical-remove-dialog.component';

describe('ChemicalRemoveDialogComponent', () => {
  let chemicals: {removePlacement: jest.Mock};
  let dialogRef: {close: jest.Mock};

  function build(useDeadline: string | null): ChemicalRemoveDialogComponent {
    chemicals = {removePlacement: jest.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: MAT_DIALOG_DATA, useValue: {
        today: '2026-09-29',
        row: {placement: {id: 7}, productName: 'Stalddesinfektion', entry: {useAndPossessionDeadline: useDeadline}},
      }},
    ]});
    return runInInjectionContext(injector, () => new ChemicalRemoveDialogComponent());
  }

  it('requires a reason (spec: Opbrugt or Bortskaffet)', () => {
    const component = build(null);

    expect(component.canSave).toBe(false);
    component.reason = ChemicalRemovalReason.Disposed;
    component.note = ' farligt affald ';
    component.save();

    expect(chemicals.removePlacement).toHaveBeenCalledWith(7, {reason: ChemicalRemovalReason.Disposed, removedAt: null, note: 'farligt affald'});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('never accepts Moved as a chosen reason (only a move sets it)', () => {
    const component = build(null);
    component.reason = ChemicalRemovalReason.Moved;

    component.save();

    expect(component.canSave).toBe(false);
    expect(chemicals.removePlacement).not.toHaveBeenCalled();
  });

  it('warns about hazardous waste once the possession deadline has passed', () => {
    expect(build('2026-07-01T00:00:00').hazardousWaste).toBe(true);
    expect(build('2026-09-29T00:00:00').hazardousWaste).toBe(false);
    expect(build(null).hazardousWaste).toBe(false);
  });

  it('stays open when the server refuses the removal (the host toasts its message)', () => {
    const component = build(null);
    chemicals.removePlacement.mockReturnValue(of({success: false, message: 'Ingen adgang'}));
    component.reason = ChemicalRemovalReason.Used;

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel', () => {
    const component = build(null);

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
