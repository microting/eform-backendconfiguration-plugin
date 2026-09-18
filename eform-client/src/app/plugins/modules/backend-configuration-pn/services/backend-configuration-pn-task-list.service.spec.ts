import {of} from 'rxjs';
import {BackendConfigurationPnTaskListService} from './backend-configuration-pn-task-list.service';

/**
 * #1297 — pins the route and payload of the "Flyt til kalender" batch call.
 * The toasting `post` (not `postNoToast`) is deliberate: like every other batch
 * action, the server's message is the user's only feedback on partial failures.
 */
describe('BackendConfigurationPnTaskListService', () => {
  let service: BackendConfigurationPnTaskListService;
  let apiBaseServiceSpy: {post: jest.Mock; postNoToast: jest.Mock};

  beforeEach(() => {
    apiBaseServiceSpy = {
      post: jest.fn().mockReturnValue(of({success: true})),
      postNoToast: jest.fn(),
    };
    service = new BackendConfigurationPnTaskListService(apiBaseServiceSpy as any);
  });

  it('moveToBoard posts task ids and board id to task-list/move-to-board', () => {
    service.moveToBoard({taskIds: [1, 2], boardId: 12}).subscribe();

    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
      'api/backend-configuration-pn/task-list/move-to-board',
      {taskIds: [1, 2], boardId: 12},
    );
    expect(apiBaseServiceSpy.postNoToast).not.toHaveBeenCalled();
  });
});
