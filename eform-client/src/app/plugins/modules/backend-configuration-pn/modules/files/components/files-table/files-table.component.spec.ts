import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {TranslateService} from '@ngx-translate/core';
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
