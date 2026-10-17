import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {ArchiveFolderPickerComponent} from './archive-folder-picker.component';

describe('ArchiveFolderPickerComponent', () => {
  let fixture: ComponentFixture<ArchiveFolderPickerComponent>;

  afterEach(() => fixture?.destroy());

  it('puts pickerId on the mtx-select element, where the e2e specs address it', () => {
    TestBed.configureTestingModule({
      declarations: [ArchiveFolderPickerComponent],
      imports: [FormsModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ArchiveFolderPickerComponent);
    fixture.componentInstance.pickerId = 'archiveFolderChooseSelect';
    fixture.componentInstance.options = [
      {id: 1, label: 'Arkiv', depth: 0, restricted: false, groupsLabel: '', isDefault: true},
    ];
    fixture.detectChanges();
    const el: HTMLElement | null = fixture.nativeElement.querySelector('#archiveFolderChooseSelect');
    expect(el).not.toBeNull();
    expect(el!.tagName.toLowerCase()).toBe('mtx-select');
  });
});
