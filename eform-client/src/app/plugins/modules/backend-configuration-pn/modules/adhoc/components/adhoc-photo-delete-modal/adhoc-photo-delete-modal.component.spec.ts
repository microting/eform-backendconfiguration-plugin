import {AdhocPhotoDeleteModalComponent} from './adhoc-photo-delete-modal.component';

/**
 * Class-level unit test (repo convention: authored, not run locally - jest
 * runs from the host frontend). The modal is a pure confirm dialog (#1100):
 * it owns no service calls, it only reports the user's choice.
 */
describe('AdhocPhotoDeleteModalComponent', () => {
  let dialogRefSpy: any;
  let component: AdhocPhotoDeleteModalComponent;

  beforeEach(() => {
    dialogRefSpy = jasmine.createSpyObj('MatDialogRef', ['close']);
    component = new AdhocPhotoDeleteModalComponent(dialogRefSpy);
  });

  it('hide() closes with false', () => {
    component.hide();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });

  it('confirm() closes with true', () => {
    component.confirm();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });
});
