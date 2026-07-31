import {of} from 'rxjs';
import {
  BackendConfigurationPnAdhocMethods,
  BackendConfigurationPnAdhocService,
} from './backend-configuration-pn-adhoc.service';
import {AdhocTaskFiltersModel, AdhocTaskStatusFilter} from '../models';

describe('BackendConfigurationPnAdhocService', () => {
  let service: BackendConfigurationPnAdhocService;
  let apiBaseServiceSpy: any;

  beforeEach(() => {
    apiBaseServiceSpy = jasmine.createSpyObj('ApiBaseService', [
      'get', 'post', 'put', 'delete', 'postFormData', 'getBlobData',
    ]);
    apiBaseServiceSpy.get.and.returnValue(of({success: true, model: []}));
    apiBaseServiceSpy.post.and.returnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.put.and.returnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.delete.and.returnValue(of({success: true}));
    apiBaseServiceSpy.postFormData.and.returnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.getBlobData.and.returnValue(of(new Blob([])));
    service = new BackendConfigurationPnAdhocService(apiBaseServiceSpy);
  });

  describe('getTasks', () => {
    it('POSTs the filters model to the index route', () => {
      const filters: AdhocTaskFiltersModel = {
        propertyId: 1,
        areaId: null,
        status: AdhocTaskStatusFilter.Open,
        tagIds: [3, 4],
        tagsMatchAll: false,
        searchText: 'leak',
        pageNumber: 1,
        pageSize: 25,
        sortColumn: 'CreatedAt',
        sortAscending: false,
      };

      service.getTasks(filters).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Index, filters);
    });
  });

  describe('getTask', () => {
    it('GETs Tasks/{id}', () => {
      service.getTask(7).subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tasks}7`);
    });
  });

  describe('createTask', () => {
    it('POSTs the create model to the base Tasks route', () => {
      const model: any = {title: 'Fix roof'};

      service.createTask(model).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Tasks, model);
    });
  });

  describe('updateTask', () => {
    it('PUTs the create model to Tasks/{id}', () => {
      const model: any = {title: 'Fix roof v2'};

      service.updateTask(9, model).subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tasks}9`, model);
    });
  });

  describe('setCompleted', () => {
    it('POSTs {completed, completedByWorkerId} to Tasks/{id}/completed', () => {
      service.setCompleted(9, true, 42).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Tasks}9/completed`,
        {completed: true, completedByWorkerId: 42}
      );
    });

    it('omits completedByWorkerId (undefined) when the caller does not pass one', () => {
      service.setCompleted(9, false).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Tasks}9/completed`,
        {completed: false, completedByWorkerId: undefined}
      );
    });
  });

  describe('archiveTask', () => {
    it('POSTs to Tasks/{id}/archive', () => {
      service.archiveTask(9).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tasks}9/archive`, {});
    });
  });

  describe('reopenTask', () => {
    it('POSTs to Tasks/{id}/reopen', () => {
      service.reopenTask(9).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tasks}9/reopen`, {});
    });
  });

  describe('deleteTask', () => {
    it('DELETEs Tasks/{id}', () => {
      service.deleteTask(9).subscribe();

      expect(apiBaseServiceSpy.delete).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tasks}9`);
    });
  });

  describe('copyTask', () => {
    it('POSTs {includeComments} to Tasks/{id}/copy', () => {
      service.copyTask(9, true).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Tasks}9/copy`,
        {includeComments: true}
      );
    });
  });

  describe('addComment', () => {
    it('POSTs {text} to Tasks/{id}/comments', () => {
      service.addComment(9, 'Looks fixed now').subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Tasks}9/comments`,
        {text: 'Looks fixed now'}
      );
    });
  });

  describe('getHistory', () => {
    it('POSTs the history filters model to the history/index route', () => {
      const filters: any = {dateFrom: '2026-01-01', dateTo: null, propertyId: null, areaId: null, tagIds: [], pageNumber: 1, pageSize: 25};

      service.getHistory(filters).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.HistoryIndex, filters);
    });
  });

  describe('getProperties', () => {
    it('GETs the properties route', () => {
      service.getProperties().subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Properties);
    });
  });

  describe('getAreas', () => {
    it('GETs the areas route with propertyId as a query param', () => {
      service.getAreas(3).subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Areas, {propertyId: 3});
    });
  });

  describe('createAreas', () => {
    it('POSTs {propertyId, names} to the areas route', () => {
      service.createAreas(3, ['Stald 1', 'Stald 2']).subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(
        BackendConfigurationPnAdhocMethods.Areas,
        {propertyId: 3, names: ['Stald 1', 'Stald 2']}
      );
    });
  });

  describe('renameArea', () => {
    it('PUTs {name} to areas/{id}', () => {
      service.renameArea(10, 'Renamed stald').subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Areas}/10`,
        {name: 'Renamed stald'}
      );
    });
  });

  describe('deleteArea', () => {
    it('DELETEs areas/{id}', () => {
      service.deleteArea(10).subscribe();

      expect(apiBaseServiceSpy.delete).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Areas}/10`);
    });
  });

  describe('getWorkers', () => {
    it('GETs the workers route with propertyId as a query param', () => {
      service.getWorkers(3).subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Workers, {propertyId: 3});
    });
  });

  describe('getTags', () => {
    it('GETs the tags route', () => {
      service.getTags().subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Tags);
    });
  });

  describe('createTag', () => {
    it('POSTs {name} to the tags route', () => {
      service.createTag('Urgent').subscribe();

      expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(BackendConfigurationPnAdhocMethods.Tags, {name: 'Urgent'});
    });
  });

  describe('renameTag', () => {
    it('PUTs {name} to tags/{id}', () => {
      service.renameTag(5, 'Renamed').subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        `${BackendConfigurationPnAdhocMethods.Tags}/5`,
        {name: 'Renamed'}
      );
    });
  });

  describe('deleteTag', () => {
    it('DELETEs tags/{id}', () => {
      service.deleteTag(5).subscribe();

      expect(apiBaseServiceSpy.delete).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Tags}/5`);
    });
  });

  describe('uploadPhoto', () => {
    it('posts FormData with the file under the file key to Tasks/{id}/photos', () => {
      const file = new File(['x'], 'photo.jpg', {type: 'image/jpeg'});

      service.uploadPhoto(9, file).subscribe();

      expect(apiBaseServiceSpy.postFormData).toHaveBeenCalledTimes(1);
      const [url, body] = apiBaseServiceSpy.postFormData.calls.mostRecent().args;
      expect(url).toBe(`${BackendConfigurationPnAdhocMethods.Tasks}9/photos`);
      expect(body).toEqual({file});
    });
  });

  describe('photoUrl', () => {
    it('returns the absolute backend path for a given photo id', () => {
      const url = service.photoUrl(17);
      expect(url).toBe(`/${BackendConfigurationPnAdhocMethods.Photos}/17`);
    });
  });

  describe('getPhotoBlob', () => {
    it('GETs the photo as a Blob (auth header attached by ApiBaseService.getBlobData)', () => {
      service.getPhotoBlob(17).subscribe();

      expect(apiBaseServiceSpy.getBlobData).toHaveBeenCalledWith(`${BackendConfigurationPnAdhocMethods.Photos}/17`);
    });
  });
});
