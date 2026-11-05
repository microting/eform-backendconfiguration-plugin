import {SharedTagModel} from 'src/app/common/models';

/** D-L11: the shared tags dialog hides rename/delete on locked rows, so non-managers get every row locked. */
export function fileTagsForDialog(tags: SharedTagModel[], canManage: boolean): SharedTagModel[] {
  return canManage ? tags : tags.map(tag => ({...tag, isLocked: true}));
}
