import {of} from 'rxjs';
import {AdhocDeleteModalComponent} from './adhoc-delete-modal.component';

describe('AdhocDeleteModalComponent', () => {
  let dialogRefSpy: any;
  let adhocServiceSpy: any;
  let component: AdhocDeleteModalComponent;

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    adhocServiceSpy = {deleteTask: jest.fn()};
    component = new AdhocDeleteModalComponent(dialogRefSpy, {id: 7, title: 'Fix roof'}, adhocServiceSpy);
  });

  it('hide() closes with false', () => {
    component.hide();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });

  it('delete() calls deleteTask(id) and closes with true on success', () => {
    adhocServiceSpy.deleteTask.mockReturnValue(of({success: true}));
    component.delete();
    expect(adhocServiceSpy.deleteTask).toHaveBeenCalledWith(7);
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });

  it('delete() does not close on failure', () => {
    adhocServiceSpy.deleteTask.mockReturnValue(of({success: false}));
    component.delete();
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });
});
