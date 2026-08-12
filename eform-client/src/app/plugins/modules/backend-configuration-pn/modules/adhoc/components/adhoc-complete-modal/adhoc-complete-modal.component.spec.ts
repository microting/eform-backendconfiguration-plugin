import {of} from 'rxjs';
import {AdhocCompleteModalComponent} from './adhoc-complete-modal.component';

describe('AdhocCompleteModalComponent', () => {
  let dialogRefSpy: any;
  let adhocServiceSpy: any;
  let adhocStateServiceSpy: any;
  let toastrSpy: any;
  let translateSpy: any;
  let component: AdhocCompleteModalComponent;

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    adhocServiceSpy = {setCompleted: jest.fn()};
    adhocStateServiceSpy = {
      getWorkersForProperty: jest.fn().mockReturnValue(
        of([{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}])
      ),
    };
    toastrSpy = {success: jest.fn()};
    translateSpy = {instant: (key: string) => key};

    component = new AdhocCompleteModalComponent(
      dialogRefSpy,
      {id: 7, propertyId: 1, title: 'Fix roof'},
      adhocServiceSpy,
      adhocStateServiceSpy,
      toastrSpy,
      translateSpy,
    );
    component.ngOnInit();
  });

  it('loads the property workers on init', () => {
    expect(adhocStateServiceSpy.getWorkersForProperty).toHaveBeenCalledWith(1);
    expect(component.workers).toEqual([{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}]);
  });

  it('cancel() closes with false', () => {
    component.cancel();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });

  it('complete() calls setCompleted(id, true, completedByWorkerId), toasts, and closes with true', () => {
    adhocServiceSpy.setCompleted.mockReturnValue(of({success: true}));
    component.completedByWorkerId = 100;
    component.complete();
    expect(adhocServiceSpy.setCompleted).toHaveBeenCalledWith(7, true, 100);
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });

  it('does not close on failure', () => {
    adhocServiceSpy.setCompleted.mockReturnValue(of({success: false}));
    component.complete();
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });
});
