import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import tagsModalPage, { TagRowObject } from '../../Page objects/TagsModal.page';

const expect = require('chai').expect;

const tagName = 'Test tag';
const updatedTagName = 'Test tag 2';

describe('Items planning - Tags', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();
    itemsPlanningPlanningPage.goToPlanningsPage();
    itemsPlanningPlanningPage.planningManageTagsBtn.click();
  });
  it('should create tag', function () {
    const tagsRowsBeforeCreate = tagsModalPage.rowNum;
    tagsModalPage.createTag(tagName);
    const tagsRowsAfterCreate = tagsModalPage.rowNum;
    const tagRowObject = new TagRowObject(tagsRowsAfterCreate);
    expect(
      tagsRowsAfterCreate,
      "Number of rows hasn't changed after creating tag"
    ).equal(tagsRowsBeforeCreate + 1);
    expect(tagRowObject.name, 'Saved Name is incorrect').equal(tagName);
  });
  it('should not create tag', function () {
    const tagsRowsBeforeCreate = tagsModalPage.rowNum;
    tagsModalPage.cancelCreateTag(tagName);
    const tagsRowsAfterCreate = tagsModalPage.rowNum;
    expect(
      tagsRowsAfterCreate,
      'Number of rows changed after not creatings tag'
    ).equal(tagsRowsBeforeCreate);
  });
  it('should update tag', function () {
    const rowNum = tagsModalPage.rowNum;
    tagsModalPage.editTag(rowNum, updatedTagName);
    const tagRowObjectAfterEdit = new TagRowObject(rowNum);
    expect(tagRowObjectAfterEdit.name, 'Updated tag name is incorrect').equal(
      updatedTagName
    );
  });
  it('should not update tag', function () {
    const rowNum = tagsModalPage.rowNum;
    tagsModalPage.cancelEditTag(rowNum, updatedTagName);
    const tagRowObjectAfterCancelEdit = new TagRowObject(rowNum);
    expect(
      tagRowObjectAfterCancelEdit.name,
      'Updated tag name is incorrect'
    ).equal(updatedTagName);
  });
  it('should not delete tag', function () {
    const tagsRowsBeforeDelete = tagsModalPage.rowNum;
    tagsModalPage.getTagByName(updatedTagName).deleteTag(true);
    const tagsRowsAfterCancelDelete = tagsModalPage.rowNum;
    expect(
      tagsRowsAfterCancelDelete,
      'Number of rows changed after cancel delete tag'
    ).equal(tagsRowsBeforeDelete);
  });
  it('should delete tag', function () {
    const tagsRowsBeforeDelete = tagsModalPage.rowNum;
    tagsModalPage.getTagByName(updatedTagName).deleteTag();
    browser.pause(500);
    const tagsRowsAfterDelete = tagsModalPage.rowNum;
    expect(
      tagsRowsAfterDelete,
      `Number of rows hasn't changed after delete tag`
    ).equal(tagsRowsBeforeDelete - 1);
  });
});
