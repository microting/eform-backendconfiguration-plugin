import {describe, it, expect} from 'vitest';
import {SharedTagModel} from 'src/app/common/models';
import {fileTagsForDialog} from './file-tags-access';

const tags: SharedTagModel[] = [
  {id: 1, name: 'Forsyning', description: '', isLocked: false},
  {id: 2, name: 'Miljø', description: '', isLocked: true},
];

describe('fileTagsForDialog (D-L11)', () => {
  it('an admin or Leder gets the tags as they are', () => {
    expect(fileTagsForDialog(tags, true)).toEqual(tags);
  });

  it('everyone else gets every tag locked, so the shared dialog hides rename and delete', () => {
    expect(fileTagsForDialog(tags, false).map(t => t.isLocked)).toEqual([true, true]);
  });

  it('does not change the list it was given', () => {
    fileTagsForDialog(tags, false);
    expect(tags[0].isLocked).toBe(false);
  });
});
