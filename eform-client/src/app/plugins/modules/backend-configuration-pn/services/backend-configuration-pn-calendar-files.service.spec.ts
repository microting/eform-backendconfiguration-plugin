import {of, throwError} from 'rxjs';
import {ApiBaseService} from 'src/app/common/services';
import {
  BackendConfigurationPnCalendarFilesMethods,
  BackendConfigurationPnCalendarFilesService,
} from './backend-configuration-pn-calendar-files.service';

describe('BackendConfigurationPnCalendarFilesService', () => {
  let service: BackendConfigurationPnCalendarFilesService;
  let apiBaseServiceSpy: any;

  beforeEach(() => {
    apiBaseServiceSpy = jasmine.createSpyObj('ApiBaseService', [
      'get', 'post', 'put', 'delete', 'postFormData', 'getBlobData',
    ]);
    apiBaseServiceSpy.get.and.returnValue(of({success: true, model: []}));
    apiBaseServiceSpy.delete.and.returnValue(of({success: true}));
    apiBaseServiceSpy.postFormData.and.returnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.getBlobData.and.returnValue(of(new Blob([])));
    service = new BackendConfigurationPnCalendarFilesService(apiBaseServiceSpy);
  });

  describe('uploadFile', () => {
    it('posts FormData with the file under the file key to the per-task files endpoint', () => {
      const file = new File(['hello'], 'hello.pdf', {type: 'application/pdf'});

      service.uploadFile(42, file).subscribe();

      expect(apiBaseServiceSpy.postFormData).toHaveBeenCalledTimes(1);
      const [url, body] = apiBaseServiceSpy.postFormData.calls.mostRecent().args;
      expect(url).toBe(`${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/42/files`);
      expect(body).toEqual({file});
    });

    it('propagates upload error responses to subscribers', (done) => {
      apiBaseServiceSpy.postFormData.and.returnValue(throwError(() => ({status: 400, error: {message: 'File too large'}})));
      const file = new File(['x'], 'big.pdf', {type: 'application/pdf'});

      service.uploadFile(7, file).subscribe({
        next: () => fail('expected error'),
        error: err => {
          expect(err.status).toBe(400);
          expect(err.error.message).toBe('File too large');
          done();
        },
      });
    });

    // The service hands `{file}` (lowercase) to postFormData, which calls
    // objectToFormData with needPascalStyle=true. The wire payload therefore
    // has the key `File` (Pascal-case) — the C# binder requires this casing.
    // This test guards against silent drift if postFormData's defaults change.
    it('produces a FormData with key "File" (Pascal-case) when bound via objectToFormData', () => {
      const file = new File(['hello'], 'hello.pdf', {type: 'application/pdf'});

      const formData = ApiBaseService.objectToFormData({file}, true);

      expect(formData.has('File')).toBeTrue();
      expect(formData.has('file')).toBeFalse();
      const value = formData.get('File');
      expect(value instanceof File).toBeTrue();
      expect((value as File).name).toBe('hello.pdf');
    });
  });

  describe('listFiles', () => {
    it('GETs the per-task files endpoint', () => {
      service.listFiles(99).subscribe();

      expect(apiBaseServiceSpy.get).toHaveBeenCalledWith(
        `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/99/files`
      );
    });
  });

  describe('downloadUrl', () => {
    it('returns the absolute backend path for a given task + file', () => {
      const url = service.downloadUrl(42, 17);
      expect(url).toBe(`/${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/42/files/17`);
    });
  });

  describe('getFileBlob', () => {
    it('GETs the per-file endpoint as a Blob (auth header attached by ApiBaseService.getBlobData)', () => {
      service.getFileBlob(42, 17).subscribe();

      expect(apiBaseServiceSpy.getBlobData).toHaveBeenCalledTimes(1);
      const [url] = apiBaseServiceSpy.getBlobData.calls.mostRecent().args;
      expect(url).toBe(`${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/42/files/17`);
    });
  });

  describe('deleteFile', () => {
    it('DELETEs the per-file endpoint', () => {
      service.deleteFile(42, 17).subscribe();

      expect(apiBaseServiceSpy.delete).toHaveBeenCalledWith(
        `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/42/files/17`
      );
    });
  });
});
