import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {EMPTY, of} from 'rxjs';
import {ArchiveMoveFlowService} from './archive-move-flow.service';
import {ArchiveFolderModel} from '../../../models';
import {ArchiveMoveConfirmDialogComponent} from '../components/archive-move-confirm-dialog/archive-move-confirm-dialog.component';

const ledelse = {id: 5, name: 'Ledelse'};
const folders: ArchiveFolderModel[] = [
  {id: 1, name: 'Arkiv', parentId: null, isDefault: true, count: 2, readGroups: [], ownGroups: false, restricted: false},
  {id: 2, name: 'Miljø', parentId: null, isDefault: false, count: 0, readGroups: [ledelse], ownGroups: true, restricted: true},
  {id: 3, name: 'Drift', parentId: null, isDefault: false, count: 0, readGroups: [], ownGroups: false, restricted: false},
];
const openFile = {id: 10, folderId: 1, ownAccess: false, readGroups: []};

describe('ArchiveMoveFlowService', () => {
  let dialog: {open: Mock};
  let filesService: {moveFiles: Mock};
  let toastr: {success: Mock; info: Mock};
  let service: ArchiveMoveFlowService;

  function answerDialog(result: boolean): void {
    dialog.open.mockReturnValue({afterClosed: () => of(result)});
  }

  beforeEach(() => {
    dialog = {open: vi.fn()};
    answerDialog(true);
    filesService = {moveFiles: vi.fn().mockReturnValue(of({success: true, model: {moved: 1, accessChanged: 1}}))};
    toastr = {success: vi.fn(), info: vi.fn()};
    const translate = {instant: (key: string, params?: object) => (params ? `${key} ${JSON.stringify(params)}` : key)};
    const overlay = {scrollStrategies: {reposition: () => ({})}};
    service = new ArchiveMoveFlowService(dialog as any, overlay as any, filesService as any, translate as any, toastr as any);
  });

  it('moves without asking when read access does not change', () => new Promise<void>(done => {
    filesService.moveFiles.mockReturnValue(of({success: true, model: {moved: 1, accessChanged: 0}}));
    service.move([openFile], folders, 3, true).subscribe(moved => {
      expect(moved).toBe(true);
      expect(dialog.open).not.toHaveBeenCalled();
      expect(filesService.moveFiles).toHaveBeenCalledWith({fileIds: [10], folderId: 3});
      expect(toastr.success).toHaveBeenCalledWith('{{count}} document moved {"count":1}');
      expect(toastr.info).not.toHaveBeenCalled();
      done();
    });
  }));

  it('asks an admin first with the impact count and says who can read afterwards (D11)', () => new Promise<void>(done => {
    const second = {...openFile, id: 11};
    filesService.moveFiles.mockReturnValue(of({success: true, model: {moved: 2, accessChanged: 2}}));
    service.move([openFile, second], folders, 2, true).subscribe(moved => {
      expect(moved).toBe(true);
      expect(dialog.open.mock.calls[0][0]).toBe(ArchiveMoveConfirmDialogComponent);
      const data = dialog.open.mock.calls[0][1].data;
      expect(data.canConfirm).toBe(true);
      expect(data.text).toBe('{{count}} documents can now be read by {{who}} {"count":2,"who":"Ledelse"}');
      expect(filesService.moveFiles).toHaveBeenCalledWith({fileIds: [10, 11], folderId: 2});
      expect(toastr.info).toHaveBeenCalledTimes(1);
      done();
    });
  }));

  it('does not move when the admin cancels', () => new Promise<void>(done => {
    answerDialog(false);
    service.move([openFile], folders, 2, true).subscribe(moved => {
      expect(moved).toBe(false);
      expect(filesService.moveFiles).not.toHaveBeenCalled();
      done();
    });
  }));

  it('stops a non-admin before the API call when access would change (D17)', () => new Promise<void>(done => {
    answerDialog(false);
    service.move([openFile], folders, 2, false).subscribe(moved => {
      expect(moved).toBe(false);
      const data = dialog.open.mock.calls[0][1].data;
      expect(data.canConfirm).toBe(false);
      expect(data.text).toBe('{{count}} document can now be read by {{who}} {"count":1,"who":"Ledelse"}');
      expect(filesService.moveFiles).not.toHaveBeenCalled();
      done();
    });
  }));

  it('never moves for a non-admin even if the refusal dialog were to close with true (D17)', () => new Promise<void>(done => {
    answerDialog(true);
    service.move([openFile], folders, 2, false).subscribe(moved => {
      expect(moved).toBe(false);
      expect(filesService.moveFiles).not.toHaveBeenCalled();
      done();
    });
  }));

  it('lets a non-admin move when access does not change', () => new Promise<void>(done => {
    filesService.moveFiles.mockReturnValue(of({success: true, model: {moved: 1, accessChanged: 0}}));
    service.move([openFile], folders, 3, false).subscribe(moved => {
      expect(moved).toBe(true);
      expect(dialog.open).not.toHaveBeenCalled();
      done();
    });
  }));

  it('skips documents that already lie in the target and does nothing when none are left', () => new Promise<void>(done => {
    service.move([{...openFile, folderId: 3}], folders, 3, true).subscribe(moved => {
      expect(moved).toBe(false);
      expect(filesService.moveFiles).not.toHaveBeenCalled();
      done();
    });
  }));

  it('a refusal (success=false) is "not moved" and toasts nothing itself', () => new Promise<void>(done => {
    filesService.moveFiles.mockReturnValue(of({success: false, message: 'refused'}));
    service.move([openFile], folders, 3, true).subscribe(moved => {
      expect(moved).toBe(false);
      expect(toastr.success).not.toHaveBeenCalled();
      expect(toastr.info).not.toHaveBeenCalled();
      done();
    });
  }));

  it('a move the host interceptor completes empty (404) emits false once and toasts nothing (C4)', () => {
    filesService.moveFiles.mockReturnValue(EMPTY);
    const results: boolean[] = [];
    let completed = false;
    service.move([openFile], folders, 2, true).subscribe({next: r => results.push(r), complete: () => (completed = true)});
    expect(results).toEqual([false]);
    expect(completed).toBe(true);
    expect(toastr.success).not.toHaveBeenCalled();
    expect(toastr.info).not.toHaveBeenCalled();
  });

  it('an empty group list reads as "everyone"', () => {
    expect(service.accessChangedText(3, [])).toBe('{{count}} documents can now be read by {{who}} {"count":3,"who":"everyone"}');
  });
});
