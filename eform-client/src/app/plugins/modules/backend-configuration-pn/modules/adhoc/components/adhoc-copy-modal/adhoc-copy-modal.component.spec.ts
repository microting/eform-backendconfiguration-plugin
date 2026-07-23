import {of} from 'rxjs';
import {AdhocCopyModalComponent} from './adhoc-copy-modal.component';

describe('AdhocCopyModalComponent', () => {
  let dialogRefSpy: any;
  let adhocServiceSpy: any;
  let component: AdhocCopyModalComponent;

  beforeEach(() => {
    dialogRefSpy = jasmine.createSpyObj('MatDialogRef', ['close']);
    adhocServiceSpy = jasmine.createSpyObj('BackendConfigurationPnAdhocService', ['copyTask']);
    component = new AdhocCopyModalComponent(dialogRefSpy, {id: 7, title: 'Fix roof'}, adhocServiceSpy);
  });

  it('cancel() closes with false', () => {
    component.cancel();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });

  it('copy(false) calls copyTask(id, false) and closes with the returned copy', () => {
    const copiedTask: any = {id: 99, title: 'Fix roof (copy)'};
    adhocServiceSpy.copyTask.and.returnValue(of({success: true, model: copiedTask}));
    component.copy(false);
    expect(adhocServiceSpy.copyTask).toHaveBeenCalledWith(7, false);
    expect(dialogRefSpy.close).toHaveBeenCalledWith(copiedTask);
  });

  it('copy(true) calls copyTask(id, true)', () => {
    adhocServiceSpy.copyTask.and.returnValue(of({success: true, model: {id: 100}}));
    component.copy(true);
    expect(adhocServiceSpy.copyTask).toHaveBeenCalledWith(7, true);
  });

  it('does not close on failure', () => {
    adhocServiceSpy.copyTask.and.returnValue(of({success: false}));
    component.copy(false);
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });
});
