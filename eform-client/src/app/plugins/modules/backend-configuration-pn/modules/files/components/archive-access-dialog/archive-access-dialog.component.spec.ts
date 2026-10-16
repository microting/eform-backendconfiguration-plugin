import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MatRadioModule} from '@angular/material/radio';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {ArchiveAccessDialogComponent, ArchiveAccessDialogData} from './archive-access-dialog.component';

const ledelse = {id: 5, name: 'Ledelse', description: ''};
const bogholderi = {id: 6, name: 'Bogholderi', description: ''};

const bulk = (partial: Partial<ArchiveAccessDialogData> = {}): ArchiveAccessDialogData => ({
  mode: 'files', title: 'Read access for {{count}} documents', titleParams: {count: 3},
  workerTags: [ledelse, bogholderi], currentGroups: [], ownAccess: false, ...partial,
});

describe('ArchiveAccessDialogComponent', () => {
  let fixture: ComponentFixture<ArchiveAccessDialogComponent>;
  let close: jest.Mock;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(data: ArchiveAccessDialogData): ArchiveAccessDialogComponent {
    close = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      declarations: [ArchiveAccessDialogComponent],
      imports: [FormsModule, MatRadioModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [{provide: MAT_DIALOG_DATA, useValue: data}, {provide: MatDialogRef, useValue: {close}}],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ArchiveAccessDialogComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => fixture?.destroy());

  it('keeps a current group without live members, marked "no members", and keeps it selected', () => {
    const c = render({
      mode: 'folder', title: 'Read access for {{folder}}', titleParams: {folder: 'Miljø'},
      workerTags: [ledelse], currentGroups: [{id: 9, name: 'Vagt'}], ownAccess: true,
    });
    expect(c.items).toEqual([ledelse, {id: 9, name: 'Vagt (no members)'}]);
    expect(c.tagIds).toEqual([9]);
    c.save();
    expect(close).toHaveBeenCalledWith({ownAccess: true, tagIds: [9]});
  });

  it('folder mode always edits own groups and can save at once', () => {
    const c = render({
      mode: 'folder', title: 'Read access for {{folder}}', workerTags: [ledelse], currentGroups: [ledelse], ownAccess: false,
    });
    expect(c.ownAccess).toBe(true);
    expect(c.canSave).toBe(true);
  });

  describe('bulk (several documents, C5)', () => {
    it('starts from "use the folder\'s access" and one click on Save sends nothing', () => {
      const c = render(bulk());
      expect(c.ownAccess).toBe(false);
      expect(c.canSave).toBe(false);
      c.save();
      expect(close).not.toHaveBeenCalled();
    });

    it('confirming the preselected "inherit" is a choice that enables Save', () => {
      const c = render(bulk());
      c.touch();
      c.save();
      expect(close).toHaveBeenCalledWith({ownAccess: false, tagIds: []});
    });

    it('own access sends the chosen groups once touched', () => {
      const c = render(bulk());
      c.ownAccess = true;
      c.tagIds = [5];
      c.touch();
      c.save();
      expect(close).toHaveBeenCalledWith({ownAccess: true, tagIds: [5]});
    });

    it('renders Save disabled until the admin picks a radio', () => {
      render(bulk());
      const saveBtn = (): HTMLButtonElement => find('#archiveAccessSaveBtn') as HTMLButtonElement;
      expect(saveBtn().disabled).toBe(true);
      find('#archiveAccessOwn')!.click();
      fixture.detectChanges();
      expect(saveBtn().disabled).toBe(false);
    });
  });

  it('cancel closes with nothing', () => {
    render(bulk()).cancel();
    expect(close).toHaveBeenCalledWith();
  });
});
