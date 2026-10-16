import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {ArchiveMoveConfirmData, ArchiveMoveConfirmDialogComponent} from './archive-move-confirm-dialog.component';

describe('ArchiveMoveConfirmDialogComponent', () => {
  let fixture: ComponentFixture<ArchiveMoveConfirmDialogComponent>;
  let close: jest.Mock;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(data: ArchiveMoveConfirmData): void {
    close = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      declarations: [ArchiveMoveConfirmDialogComponent],
      imports: [TranslateModule.forRoot()],
      providers: [{provide: MAT_DIALOG_DATA, useValue: data}, {provide: MatDialogRef, useValue: {close}}],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ArchiveMoveConfirmDialogComponent);
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('an admin sees the impact and can confirm (D11)', () => {
    render({text: '2 dokumenter kan nu læses af Ledelse', canConfirm: true});
    expect(find('#archiveMoveImpact')?.textContent).toContain('2 dokumenter kan nu læses af Ledelse');
    expect(find('#archiveMoveAdminOnly')).toBeNull();
    find('#archiveMoveConfirmBtn')!.click();
    expect(close).toHaveBeenCalledWith(true);
  });

  it('a non-admin is told only an administrator can do it and has no confirm button (D17)', () => {
    render({text: '1 dokument kan nu læses af Ledelse', canConfirm: false});
    expect(find('#archiveMoveAdminOnly')?.textContent)
      .toContain('The move changes who can read the documents. Only an administrator can do that.');
    expect(find('#archiveMoveConfirmBtn')).toBeNull();
    find('#archiveMoveCancelBtn')!.click();
    expect(close).toHaveBeenCalledWith(false);
  });
});
