import {AppInstallModel} from '../../../../models';
import {appInstallState} from './app-install-state';

function app(hasAccess: boolean, version: string | null): AppInstallModel {
  return {hasAccess, version, model: null, manufacturer: null, osVersion: null};
}

// #1335: the three line-1 states of a Medarbejdere app cell. The rule that
// matters most is that missing data is never red - only a missing permission is.
describe('appInstallState', () => {
  it('is "installed" when the app has reported a version', () => {
    expect(appInstallState(app(true, '4.0.36'))).toBe('installed');
  });

  it('is "installed" when a version was reported before access was taken away', () => {
    expect(appInstallState(app(false, '1.4.0'))).toBe('installed');
  });

  it('is "not-registered" when the worker has access but nothing is reported', () => {
    expect(appInstallState(app(true, null))).toBe('not-registered');
  });

  it('treats an empty version as not reported', () => {
    expect(appInstallState(app(true, ''))).toBe('not-registered');
  });

  it('is "no-access" when the worker has no access and nothing is reported', () => {
    expect(appInstallState(app(false, null))).toBe('no-access');
  });

  it('is "not-registered", never red, when the server sent no app data at all', () => {
    expect(appInstallState(undefined)).toBe('not-registered');
  });
});
