import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {Paged} from 'src/app/common/models';
import {DocumentDateState, FilesModel} from '../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../services';
import {FilesTableComponent} from './files-table.component';

const row = (id: number, fileName: string): FilesModel => ({
  id, fileName, fileExtension: 'pdf', createDate: new Date(Date.UTC(2026, 9, 1)), properties: ['Example Farm'], tags: [],
  folderId: 1, folderPath: ['Drift'], documentDate: new Date(Date.UTC(2026, 9, 3)),
  documentDateState: DocumentDateState.Suggested, readGroups: [], ownAccess: false,
});

describe('FilesTableComponent', () => {
  let table: FilesTableComponent;
  let opened: Mock;
  let filesService: {getPdfFile: Mock};

  beforeEach(() => {
    filesService = {getPdfFile: vi.fn().mockReturnValue(of(new Blob(['%PDF']))) };
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnFilesService, useValue: filesService},
        {provide: TranslateService, useValue: {instant: (key: string) => key}},
      ],
    });
    table = TestBed.runInInjectionContext(() => new FilesTableComponent());
    const files = new Paged<FilesModel>();
    files.entities = [row(7, 'Invoice 118'), row(8, 'Invoice 119')];
    table.files = files;
    table.ngOnChanges({files: {} as any});
    opened = vi.fn();
    table.openFile.subscribe(opened);
  });

  describe('keyboard', () => {
    const keyOnRow = (key: string) => {
      const tr = document.createElement('tr');
      const event = {key, target: tr, currentTarget: tr, preventDefault: vi.fn()} as unknown as Event;
      return event;
    };

    it('opens the focused row on Enter and on Space, like a click', () => {
      const enter = keyOnRow('Enter');
      table.onRowKey(enter, table.rows[1]);
      expect(opened).toHaveBeenCalledWith(table.rows[1]);
      expect(enter.preventDefault).toHaveBeenCalled();

      const space = keyOnRow(' ');
      table.onRowKey(space, table.rows[0]);
      expect(opened).toHaveBeenLastCalledWith(table.rows[0]);
      // Space would otherwise scroll the page.
      expect(space.preventDefault).toHaveBeenCalled();
    });

    it('leaves keys pressed on the checkbox or the menu button to them', () => {
      const tr = document.createElement('tr');
      const checkbox = document.createElement('input');
      const event = {key: ' ', target: checkbox, currentTarget: tr, preventDefault: vi.fn()} as unknown as Event;
      table.onRowKey(event, table.rows[0]);
      expect(opened).not.toHaveBeenCalled();
      expect(event.preventDefault).not.toHaveBeenCalled();
    });
  });

  it.each([
    ['pdf', 'picture_as_pdf', 'View PDF', 'Delete file'],
    ['jpg', 'image', 'View image', 'Delete image'],
    ['JPEG', 'image', 'View image', 'Delete image'],
    ['png', 'image', 'View image', 'Delete image'],
    ['docx', 'description', 'View file', 'Delete this file'],
  ])('a .%s row shows %s, %s and %s', (fileExtension, icon, viewKey, deleteKey) => {
    expect(table.presentationOf({...table.rows[0], fileExtension})).toEqual({icon, viewKey, deleteKey});
  });

  describe('Mappe column', () => {
    const at = (folderPath: string[]) => ({...table.rows[0], folderPath});

    it('a top-level folder shows only its name, no parent', () => {
      const r = at(['Økonomi']);
      expect(table.parentLabel(r)).toBe('');
      expect(table.folderLeaf(r)).toBe('Økonomi');
      expect(table.fullPath(r)).toBe('Økonomi');
    });

    it('a subfolder shows "Parent ›" and the leaf', () => {
      const r = at(['Økonomi', 'Fakturaer']);
      expect(table.parentLabel(r)).toBe('Økonomi ›');
      expect(table.folderLeaf(r)).toBe('Fakturaer');
      expect(table.fullPath(r)).toBe('Økonomi › Fakturaer');
    });

    it('a deeper path folds the levels above the parent into "…", the tooltip keeps them all', () => {
      const r = at(['Drift', 'Økonomi', 'Fakturaer', '2026']);
      expect(table.parentLabel(r)).toBe('… › Fakturaer ›');
      expect(table.folderLeaf(r)).toBe('2026');
      expect(table.fullPath(r)).toBe('Drift › Økonomi › Fakturaer › 2026');
    });

    it('no path shows nothing', () => {
      for (const r of [at([]), {...table.rows[0], folderPath: undefined as unknown as string[]}]) {
        expect(table.parentLabel(r)).toBe('');
        expect(table.folderLeaf(r)).toBe('');
        expect(table.fullPath(r)).toBe('');
      }
    });
  });

  it('tracks rows by file id', () => {
    expect(table.trackById(0, table.rows[1])).toBe(8);
  });

  it('emits the selection in table order', () => {
    const changed = vi.fn();
    table.selectedIdsChange.subscribe(changed);
    table.selectedIds = [8];
    table.toggle(7);
    expect(changed).toHaveBeenCalledWith([7, 8]);
  });

  it('reads the interceptor Date (midnight UTC) as the server calendar day', () => {
    expect(table.documentDay(table.rows[0])).toBe('2026-10-03');
  });

  it('revokes the previous PDF URL when the next opens, and the last one on destroy', () => {
    const create = vi.fn().mockReturnValueOnce('blob:first').mockReturnValueOnce('blob:second');
    const revoke = vi.fn();
    Object.assign(URL, {createObjectURL: create, revokeObjectURL: revoke});
    const open = vi.spyOn(window, 'open').mockReturnValue(null);

    table.onOpenView(table.rows[0]);
    expect(open).toHaveBeenCalledWith('blob:first');
    expect(revoke).not.toHaveBeenCalled();

    table.onOpenView(table.rows[1]);
    expect(revoke).toHaveBeenCalledWith('blob:first');

    table.ngOnDestroy();
    expect(revoke).toHaveBeenLastCalledWith('blob:second');
    open.mockRestore();
  });
});

describe('FilesTableComponent rights (D-L4)', () => {
  let fixture: ComponentFixture<FilesTableComponent>;

  function render(canManage: boolean, canDelete: boolean): void {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [FilesTableComponent, TranslateModule.forRoot()],
      providers: [{provide: BackendConfigurationPnFilesService, useValue: {getPdfFile: vi.fn()}}],
    });
    fixture = TestBed.createComponent(FilesTableComponent);
    const files = new Paged<FilesModel>();
    files.entities = [row(7, 'Invoice 118')];
    fixture.componentInstance.files = files;
    fixture.componentInstance.canManage = canManage;
    fixture.componentInstance.canDelete = canDelete;
    fixture.componentInstance.ngOnChanges({files: {} as any});
    fixture.detectChanges();
  }

  const openMenu = (): void => {
    fixture.nativeElement.querySelector('#actionMenu0').click();
    fixture.detectChanges();
  };
  const inOverlay = (selector: string): HTMLElement | null => document.querySelector(`.cdk-overlay-container ${selector}`);

  afterEach(() => {
    fixture?.destroy();
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  it('offers Slet only when canDelete is true', () => {
    render(true, false);
    openMenu();
    expect(inOverlay('#viewPdfBtn-0')).not.toBeNull();
    expect(inOverlay('#deleteFileBtn-0')).toBeNull();
    fixture.destroy();
    document.querySelector('.cdk-overlay-container')?.replaceChildren();

    render(false, true);
    openMenu();
    expect(inOverlay('#deleteFileBtn-0')).not.toBeNull();
  });

  it('shows the Læseadgang column only when canManage is true', () => {
    render(true, true);
    expect(fixture.nativeElement.querySelector('#archiveAccessHeader')).not.toBeNull();
    render(false, true);
    expect(fixture.nativeElement.querySelector('#archiveAccessHeader')).toBeNull();
  });
});
