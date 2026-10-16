import {filesInitialState, filesReducer} from './files.reducer';
import {filesSelectFolder, filesToggleDateCheck, filesUpdateFilters} from './files.actions';

describe('filesReducer — archive folders', () => {
  it('starts with no folder and the date check off', () => {
    expect(filesInitialState.filters.folderId).toBeNull();
    expect(filesInitialState.filters.needsDateCheck).toBe(false);
  });

  it('selecting a folder clears the search and the date check (both span all folders)', () => {
    const searching = filesReducer(filesInitialState, filesUpdateFilters({...filesInitialState.filters, nameFilter: 'faktura'}));
    const checking = filesReducer(searching, filesToggleDateCheck(true));
    const state = filesReducer(checking, filesSelectFolder(4));
    expect(state.filters).toEqual({...filesInitialState.filters, folderId: 4});
  });

  it('the date check keeps the folder and the search', () => {
    const inFolder = filesReducer(filesInitialState, filesSelectFolder(4));
    const searching = filesReducer(inFolder, filesUpdateFilters({...inFolder.filters, nameFilter: 'faktura'}));
    const state = filesReducer(searching, filesToggleDateCheck(true));
    expect(state.filters.folderId).toBe(4);
    expect(state.filters.nameFilter).toBe('faktura');
    expect(state.filters.needsDateCheck).toBe(true);
    expect(filesReducer(state, filesToggleDateCheck(false)).filters.needsDateCheck).toBe(false);
  });
});
