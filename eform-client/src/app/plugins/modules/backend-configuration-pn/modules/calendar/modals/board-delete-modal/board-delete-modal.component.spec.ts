import {of, throwError} from 'rxjs';
import {BoardDeleteModalComponent, BoardDeleteModalData} from './board-delete-modal.component';

function board(id = 9, name = 'Drift') {
  return {id, name, color: '#d04d4d', propertyId: 7} as any;
}

function make(data: Partial<BoardDeleteModalData>, service: any) {
  const closed: unknown[] = [];
  const dialogRef = {close: (v: unknown) => closed.push(v)} as any;
  const component = new BoardDeleteModalComponent(
    dialogRef,
    {board: board(), boardCount: 3, ...data} as BoardDeleteModalData,
    service,
  );
  return {component, closed};
}

function serviceStub(overrides: Record<string, any> = {}) {
  return {
    getBoardEventCount: jest.fn().mockReturnValue(of({success: true, model: 12})),
    deleteBoard: jest.fn().mockReturnValue(of({success: true, model: 1})),
    ...overrides,
  } as any;
}

describe('BoardDeleteModalComponent', () => {
  describe('event count', () => {
    it('reads the count from boards/{id}/event-count and enables Delete', () => {
      const service = serviceStub();
      const {component} = make({}, service);

      // Before the load the destructive button is disabled: confirming a
      // delete without the number it is meant to inform you of is worse than
      // a short wait.
      expect(component.countLoaded).toBe(false);

      component.ngOnInit();

      expect(service.getBoardEventCount).toHaveBeenCalledWith(9);
      expect(component.eventCount).toBe(12);
      expect(component.countLoaded).toBe(true);
    });

    it('still enables Delete when the count call fails — it only annotates the dialog', () => {
      const service = serviceStub({
        getBoardEventCount: jest.fn().mockReturnValue(throwError(() => new Error('boom'))),
      });
      const {component} = make({}, service);

      component.ngOnInit();

      expect(component.eventCount).toBe(0);
      expect(component.countLoaded).toBe(true);
    });

    it('leaves the count at zero when the server answers unsuccessfully', () => {
      const service = serviceStub({
        getBoardEventCount: jest.fn().mockReturnValue(of({success: false, message: 'nope'})),
      });
      const {component} = make({}, service);

      component.ngOnInit();

      expect(component.eventCount).toBe(0);
      expect(component.countLoaded).toBe(true);
    });
  });

  // GetBoards auto-creates a "Default" calendar for a property that has none,
  // so deleting the last one does not leave the property empty. The dialog says
  // so rather than letting the user find out.
  describe('last-calendar hint', () => {
    it('shows for the property\'s only calendar', () => {
      const {component} = make({boardCount: 1}, serviceStub());

      expect(component.isLastBoard).toBe(true);
    });

    it('stays hidden while other calendars remain', () => {
      const {component} = make({boardCount: 2}, serviceStub());

      expect(component.isLastBoard).toBe(false);
    });
  });

  describe('confirming', () => {
    it('closes truthy once the server confirms the delete', () => {
      const service = serviceStub();
      const {component, closed} = make({}, service);
      component.ngOnInit();

      component.onConfirm();

      expect(service.deleteBoard).toHaveBeenCalledWith(9);
      expect(closed).toEqual([true]);
      expect(component.failed).toBe(false);
    });
  });

  // DeleteBoard cascades DeleteEntireSeries over every series on the board and
  // aborts with the board INTACT if any one of them fails. So an unsuccessful
  // response must not close the dialog: the row is still there, and removing it
  // optimistically would show the user a calendar that still exists as gone.
  describe('when the cascade aborts', () => {
    it('keeps the dialog open, surfaces the failure and re-arms the button', () => {
      const service = serviceStub({
        deleteBoard: jest.fn().mockReturnValue(of({success: false, message: 'series delete failed'})),
      });
      const {component, closed} = make({}, service);
      component.ngOnInit();

      component.onConfirm();

      expect(closed).toEqual([]);
      expect(component.failed).toBe(true);
      expect(component.deleting).toBe(false);
    });

    it('treats a transport error the same way', () => {
      const service = serviceStub({
        deleteBoard: jest.fn().mockReturnValue(throwError(() => new Error('boom'))),
      });
      const {component, closed} = make({}, service);
      component.ngOnInit();

      component.onConfirm();

      expect(closed).toEqual([]);
      expect(component.failed).toBe(true);
      expect(component.deleting).toBe(false);
    });

    it('lets the user retry, and clears the failure notice while the retry runs', () => {
      const deleteBoard = jest.fn()
        .mockReturnValueOnce(of({success: false, message: 'series delete failed'}))
        .mockReturnValueOnce(of({success: true, model: 1}));
      const {component, closed} = make({}, serviceStub({deleteBoard}));
      component.ngOnInit();

      component.onConfirm();
      expect(component.failed).toBe(true);

      component.onConfirm();

      expect(deleteBoard).toHaveBeenCalledTimes(2);
      expect(component.failed).toBe(false);
      expect(closed).toEqual([true]);
    });

    // The cascade is synchronous and can be slow; a second click would fire a
    // second DELETE against a board the first call is still working through.
    it('ignores a second click while the delete is still in flight', () => {
      const deleteBoard = jest.fn().mockReturnValue({subscribe: () => undefined});
      const {component} = make({}, serviceStub({deleteBoard}));
      component.ngOnInit();

      component.onConfirm();
      component.onConfirm();

      expect(deleteBoard).toHaveBeenCalledTimes(1);
    });
  });

  it('closes with null on cancel, deleting nothing', () => {
    const service = serviceStub();
    const {component, closed} = make({}, service);
    component.ngOnInit();

    component.onCancel();

    expect(closed).toEqual([null]);
    expect(service.deleteBoard).not.toHaveBeenCalled();
  });
});
