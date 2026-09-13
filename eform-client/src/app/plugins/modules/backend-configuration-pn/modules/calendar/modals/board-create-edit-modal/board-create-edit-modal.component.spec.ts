import {FormBuilder} from '@angular/forms';
import {of, throwError} from 'rxjs';
import {CALENDAR_COLORS} from '../../../../models/calendar';
import {
  BoardCreateEditModalComponent,
  BoardCreateEditModalData,
} from './board-create-edit-modal.component';

// Class-level instantiation, matching the module's other specs: the assertions
// are on the form and the request the component makes, neither of which needs
// a TestBed. FormBuilder has no dependencies of its own.
function board(id: number, name: string, color = CALENDAR_COLORS[0]) {
  return {id, name, color, propertyId: 7} as any;
}

function make(data: Partial<BoardCreateEditModalData>, service: any) {
  const closed: unknown[] = [];
  const dialogRef = {close: (v: unknown) => closed.push(v)} as any;
  const component = new BoardCreateEditModalComponent(
    new FormBuilder(),
    dialogRef,
    {propertyId: 7, boards: [], ...data} as BoardCreateEditModalData,
    service,
  );
  component.ngOnInit();
  return {component, closed};
}

function serviceStub(overrides: Partial<Record<'createBoard' | 'updateBoard', any>> = {}) {
  return {
    createBoard: jest.fn().mockReturnValue(of({success: true, model: 1})),
    updateBoard: jest.fn().mockReturnValue(of({success: true, model: 1})),
    ...overrides,
  } as any;
}

describe('BoardCreateEditModalComponent', () => {
  describe('create mode', () => {
    it('starts empty on the first palette colour and titles itself "Create calendar"', () => {
      const {component} = make({}, serviceStub());

      expect(component.isEdit).toBe(false);
      expect(component.titleKey).toBe('Create calendar');
      expect(component.submitKey).toBe('Create');
      expect(component.form.value).toEqual({name: '', color: CALENDAR_COLORS[0]});
      // A blank name is invalid, so the primary button starts disabled.
      expect(component.form.invalid).toBe(true);
    });

    it('POSTs the trimmed name, the chosen colour and the property id', () => {
      const service = serviceStub();
      const {component, closed} = make({propertyId: 42}, service);

      component.form.patchValue({name: '  Drift  '});
      component.selectColor(CALENDAR_COLORS[3]);
      component.onSave();

      expect(service.createBoard).toHaveBeenCalledWith({
        name: 'Drift',
        color: CALENDAR_COLORS[3],
        propertyId: 42,
      });
      expect(service.updateBoard).not.toHaveBeenCalled();
      expect(closed).toEqual([true]);
    });

    // The picker writes the shipped palette values verbatim (#1210 keeps
    // CALENDAR_COLORS); nothing here may mint a new hex.
    it('only ever offers the shipped palette', () => {
      const {component} = make({}, serviceStub());

      expect(component.colors).toBe(CALENDAR_COLORS);
      expect(component.colors).toHaveLength(8);
    });
  });

  describe('edit mode', () => {
    it('pre-fills name and colour from the calendar and titles itself "Edit calendar"', () => {
      const existing = board(9, 'Drift', CALENDAR_COLORS[2]);
      const {component} = make({board: existing, boards: [existing]}, serviceStub());

      expect(component.isEdit).toBe(true);
      expect(component.titleKey).toBe('Edit calendar');
      expect(component.submitKey).toBe('Save');
      expect(component.form.value).toEqual({name: 'Drift', color: CALENDAR_COLORS[2]});
      // Its own unchanged name must not read as a duplicate of itself,
      // otherwise a pure recolour could never be saved.
      expect(component.form.valid).toBe(true);
      expect(component.duplicateName).toBe(false);
    });

    it('PUTs the calendar id with the edited values', () => {
      const existing = board(9, 'Drift', CALENDAR_COLORS[2]);
      const service = serviceStub();
      const {component, closed} = make({board: existing, boards: [existing]}, service);

      component.form.patchValue({name: 'Drift 2'});
      component.selectColor(CALENDAR_COLORS[5]);
      component.onSave();

      expect(service.updateBoard).toHaveBeenCalledWith({
        id: 9,
        name: 'Drift 2',
        color: CALENDAR_COLORS[5],
      });
      expect(service.createBoard).not.toHaveBeenCalled();
      expect(closed).toEqual([true]);
    });
  });

  describe('duplicate-name guard', () => {
    const boards = [board(1, 'Default'), board(2, 'Drift')];

    it('blocks a name another calendar of the property already has', () => {
      const {component} = make({boards}, serviceStub());

      component.form.patchValue({name: 'Drift'});

      expect(component.duplicateName).toBe(true);
      expect(component.form.invalid).toBe(true);
    });

    it('is case-insensitive', () => {
      const {component} = make({boards}, serviceStub());

      component.form.patchValue({name: 'dRiFt'});

      expect(component.duplicateName).toBe(true);
    });

    it('ignores leading and trailing whitespace', () => {
      const {component} = make({boards}, serviceStub());

      component.form.patchValue({name: '  Drift  '});

      expect(component.duplicateName).toBe(true);
    });

    it('refuses to send the request while the name collides', () => {
      const service = serviceStub();
      const {component, closed} = make({boards}, service);

      component.form.patchValue({name: 'Drift'});
      component.onSave();

      expect(service.createBoard).not.toHaveBeenCalled();
      expect(closed).toEqual([]);
    });

    it('clears once the name is changed to a free one', () => {
      const {component} = make({boards}, serviceStub());

      component.form.patchValue({name: 'Drift'});
      expect(component.duplicateName).toBe(true);

      component.form.patchValue({name: 'Rengøring'});
      expect(component.duplicateName).toBe(false);
      expect(component.form.valid).toBe(true);
    });

    it('blocks renaming one calendar onto another calendar of the property', () => {
      const service = serviceStub();
      const {component} = make({board: boards[1], boards}, service);

      component.form.patchValue({name: 'Default'});
      component.onSave();

      expect(component.duplicateName).toBe(true);
      expect(service.updateBoard).not.toHaveBeenCalled();
    });

    // The message belongs to the "already exists" error only; an empty field
    // is the required rule's business.
    it('does not report a blank name as a duplicate', () => {
      const {component} = make({boards}, serviceStub());

      component.form.patchValue({name: '   '});

      expect(component.duplicateName).toBe(false);
    });

    // Validators.required accepts "   ", and the name is trimmed on the way
    // out — so without the extra non-blank rule this POSTs a calendar with an
    // empty name, which the duplicate guard can never catch afterwards.
    it('rejects a whitespace-only name rather than posting an empty one', () => {
      const service = serviceStub();
      const {component, closed} = make({boards}, service);

      component.form.patchValue({name: '   '});

      expect(component.form.hasError('required', 'name')).toBe(true);
      component.onSave();
      expect(service.createBoard).not.toHaveBeenCalled();
      expect(closed).toEqual([]);
    });
  });

  describe('failure and double-submit', () => {
    it('stays open and re-arms the button when the server rejects the save', () => {
      const service = serviceStub({createBoard: jest.fn().mockReturnValue(of({success: false, message: 'nope'}))});
      const {component, closed} = make({}, service);

      component.form.patchValue({name: 'Drift'});
      component.onSave();

      expect(closed).toEqual([]);
      expect(component.saving).toBe(false);
    });

    it('stays open and re-arms the button when the request errors', () => {
      const service = serviceStub({createBoard: jest.fn().mockReturnValue(throwError(() => new Error('boom')))});
      const {component, closed} = make({}, service);

      component.form.patchValue({name: 'Drift'});
      component.onSave();

      expect(closed).toEqual([]);
      expect(component.saving).toBe(false);
    });

    // A second click before the first POST resolves would create a second
    // calendar, and the duplicate-name guard cannot catch it: the first one is
    // not in `data.boards` yet.
    it('ignores a second click while the first request is still in flight', () => {
      let emit: ((v: unknown) => void) | null = null;
      const service = serviceStub({
        createBoard: jest.fn().mockReturnValue({subscribe: (o: any) => {emit = o.next;}}),
      });
      const {component} = make({}, service);

      component.form.patchValue({name: 'Drift'});
      component.onSave();
      component.onSave();

      expect(service.createBoard).toHaveBeenCalledTimes(1);
      expect(emit).not.toBeNull();
    });
  });

  it('closes with null on cancel, sending nothing', () => {
    const service = serviceStub();
    const {component, closed} = make({}, service);

    component.form.patchValue({name: 'Drift'});
    component.onCancel();

    expect(closed).toEqual([null]);
    expect(service.createBoard).not.toHaveBeenCalled();
  });
});
