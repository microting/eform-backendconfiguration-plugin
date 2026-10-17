import {of} from 'rxjs';
import {FilesStateService} from './files-state.service';
import {DocumentDateState} from '../../../models';
import {filesInitialState, FilesFiltrationModel, selectFilesFilters} from '../../../state';

describe('FilesStateService — folder and date filters', () => {
  let api: {getAllFiles: jest.Mock};

  function create(filters: Partial<FilesFiltrationModel>): FilesStateService {
    const allFilters = {...filesInitialState.filters, ...filters};
    const store = {
      select: jest.fn(selector => of(selector === selectFilesFilters ? allFilters : filesInitialState.pagination)),
      dispatch: jest.fn(),
    };
    api = {getAllFiles: jest.fn().mockReturnValue(of({success: true, model: {total: 4, entities: []}}))};
    return new FilesStateService(store as any, api as any);
  }

  it('lists the selected folder', () => {
    create({folderId: 4}).getFiles().subscribe();
    expect(api.getAllFiles.mock.lastCall[0]).toEqual(expect.objectContaining({folderId: 4, dateState: null}));
  });

  it('a search spans all folders', () => {
    create({folderId: 4, nameFilter: 'faktura'}).getFiles().subscribe();
    expect(api.getAllFiles.mock.lastCall[0]).toEqual(expect.objectContaining({folderId: null, nameFilter: 'faktura'}));
  });

  it('"Dato skal tjekkes" spans all folders and asks for Suggested dates', () => {
    create({folderId: 4, needsDateCheck: true}).getFiles().subscribe();
    expect(api.getAllFiles.mock.lastCall[0]).toEqual(expect.objectContaining({folderId: null, dateState: DocumentDateState.Suggested}));
  });

  it('counts the Suggested documents of the selected properties for the chip', done => {
    create({folderId: 4, propertyIds: [3], nameFilter: 'x', tagIds: [9]}).getDateCheckCount().subscribe(count => {
      expect(count).toBe(4);
      expect(api.getAllFiles.mock.lastCall[0]).toEqual(expect.objectContaining({
        propertyIds: [3], nameFilter: '', tagIds: [], folderId: null, dateState: DocumentDateState.Suggested,
      }));
      done();
    });
  });
});
