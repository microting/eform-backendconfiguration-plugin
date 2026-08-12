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
    apiBaseServiceSpy = {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      postFormData: jest.fn(),
      getBlobData: jest.fn(),
    };
    apiBaseServiceSpy.get.mockReturnValue(of({success: true, model: []}));
    apiBaseServiceSpy.delete.mockReturnValue(of({success: true}));
    apiBaseServiceSpy.postFormData.mockReturnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.getBlobData.mockReturnValue(of(new Blob([])));
    service = new BackendConfigurationPnCalendarFilesService(apiBaseServiceSpy);
  });

  describe('uploadFile', () => {
    it('posts FormData with the file under the file key to the per-task files endpoint', () => {
      const file = new File(['hello'], 'hello.pdf', {type: 'application/pdf'});

      service.uploadFile(42, file).subscribe();

      expect(apiBaseServiceSpy.postFormData).toHaveBeenCalledTimes(1);
      const [url, body] = apiBaseServiceSpy.postFormData.mock.lastCall;
      expect(url).toBe(`${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/42/files`);
      expect(body).toEqual({file});
    });

    it('propagates upload error responses to subscribers', (done) => {
      apiBaseServiceSpy.postFormData.mockReturnValue(throwError(() => ({status: 400, error: {message: 'File too large'}})));
      const file = new File(['x'], 'big.pdf', {type: 'application/pdf'});

      service.uploadFile(7, file).subscribe({
        next: () => done(new Error('expected error')),
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

      expect(formData.has('File')).toBe(true);
      expect(formData.has('file')).toBe(false);
      const value = formData.get('File');
      expect(value instanceof File).toBe(true);
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
      const [url] = apiBaseServiceSpy.getBlobData.mock.lastCall;
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
