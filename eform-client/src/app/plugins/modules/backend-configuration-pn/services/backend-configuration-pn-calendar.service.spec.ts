import {BackendConfigurationPnCalendarService, BackendConfigurationPnCalendarMethods} from './backend-configuration-pn-calendar.service';
import {of} from 'rxjs';

describe('BackendConfigurationPnCalendarService', () => {
  let service: BackendConfigurationPnCalendarService;
  let apiBaseServiceSpy: any;

  beforeEach(() => {
    apiBaseServiceSpy = jasmine.createSpyObj('ApiBaseService', ['get', 'post', 'put', 'delete']);
    apiBaseServiceSpy.put.and.returnValue(of({success: true}));
    service = new BackendConfigurationPnCalendarService(apiBaseServiceSpy);
  });

  describe('moveTaskWithScope', () => {
    it('calls put with MoveTask URL and body including scope and originalDate', () => {
      service.moveTaskWithScope(42, '2027-03-24', 14.0, 'this', '2027-03-23').subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        BackendConfigurationPnCalendarMethods.MoveTask,
        {id: 42, newDate: '2027-03-24', newStartHour: 14.0, scope: 'this', originalDate: '2027-03-23'}
      );
    });

    it('passes scope=all correctly', () => {
      service.moveTaskWithScope(42, '2027-03-24', 14.0, 'all', '2027-03-23').subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        BackendConfigurationPnCalendarMethods.MoveTask,
        jasmine.objectContaining({scope: 'all'})
      );
    });
  });

  describe('deleteTask', () => {
    it('calls put to tasks/delete with id, scope, and originalDate', () => {
      service.deleteTask(42, 'this' as any, '2027-03-23').subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        `${BackendConfigurationPnCalendarMethods.Tasks}/delete`,
        {id: 42, scope: 'this', originalDate: '2027-03-23'}
      );
    });
  });

  describe('moveTask (legacy)', () => {
    it('calls put with MoveTask URL without scope or originalDate', () => {
      service.moveTask(42, '2027-03-24', 14.0).subscribe();

      expect(apiBaseServiceSpy.put).toHaveBeenCalledWith(
        BackendConfigurationPnCalendarMethods.MoveTask,
        {id: 42, newDate: '2027-03-24', newStartHour: 14.0}
      );
    });
  });
});
