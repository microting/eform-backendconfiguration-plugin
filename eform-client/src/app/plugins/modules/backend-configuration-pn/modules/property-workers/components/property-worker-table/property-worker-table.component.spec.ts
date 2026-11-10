import {describe, it, expect, vi} from 'vitest';
import type {Mock} from 'vitest';
import {ElementRef, SimpleChange} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {Observable, of} from 'rxjs';
import {AuthService, DeviceUserService} from 'src/app/common/services';
import {AuthStateService} from 'src/app/common/store';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {PropertyWorkersStateService} from '../store';
import {PropertyWorkerTableComponent} from './property-worker-table.component';

const notSkipped = of({success: true, message: '', model: false});

function createComponent(
  {skipCloudDeploy = notSkipped, isFirstUser = false}: {skipCloudDeploy?: Observable<unknown>; isFirstUser?: boolean} = {},
): {component: PropertyWorkerTableComponent; getSkipCloudDeploy: Mock} {
  TestBed.resetTestingModule();
  const getSkipCloudDeploy = vi.fn().mockReturnValue(skipCloudDeploy);
  TestBed.configureTestingModule({
    providers: [
      {
        provide: Store,
        useValue: {select: vi.fn((selector: unknown) => of(selector === selectCurrentUserIsFirstUser ? isFirstUser : false))},
      },
      {provide: TranslateService, useValue: {stream: vi.fn((key: string) => of(key)), instant: vi.fn((key: string) => key)}},
      {provide: AuthStateService, useValue: {}},
      {provide: PropertyWorkersStateService, useValue: {updateNameFilter: vi.fn()}},
      {provide: AuthService, useValue: {}},
      {provide: DeviceUserService, useValue: {getSkipCloudDeploy}},
      {provide: MatDialog, useValue: {open: vi.fn()}},
      {provide: Overlay, useValue: {}},
      {provide: ElementRef, useValue: new ElementRef(document.createElement('div'))},
    ],
  });
  const component = TestBed.runInInjectionContext(() => new PropertyWorkerTableComponent());
  component.ngOnInit();
  return {component, getSkipCloudDeploy};
}

function fields(component: PropertyWorkerTableComponent): string[] {
  return component.tableHeaders.map(h => h.field);
}

// "Customer no & OTP" have no function when the SDK setting skipCloudDeploy is
// on (no cloud pairing), so the column is hidden then, as on core's device-users
// and units pages (eform-angular-frontend #8193).
describe('PropertyWorkerTableComponent customer no & OTP column', () => {
  it('shows the column when cloud deploy is not skipped', () => {
    const {component, getSkipCloudDeploy} = createComponent();
    expect(getSkipCloudDeploy).toHaveBeenCalledTimes(1);
    expect(fields(component)).toContain('customerOtp');
  });

  it('hides the column when cloud deploy is skipped', () => {
    const {component} = createComponent({skipCloudDeploy: of({success: true, message: '', model: true})});
    expect(fields(component)).not.toContain('customerOtp');
    expect(fields(component)).toContain('actions');
  });

  it('keeps the column hidden when the headers are rebuilt for resigned workers', () => {
    const {component} = createComponent({skipCloudDeploy: of({success: true, message: '', model: true})});
    component.showResigned = true;
    component.ngOnChanges({showResigned: new SimpleChange(false, true, false)});
    expect(fields(component)).toContain('resignedAtDate');
    expect(fields(component)).not.toContain('customerOtp');
  });

  it('shows the column when the setting cannot be read', () => {
    const {component} = createComponent({skipCloudDeploy: of({success: false, message: 'error', model: false})});
    expect(fields(component)).toContain('customerOtp');
  });
});

// The Chemistry and Halebid app columns are shown to the first user only (#1335),
// using core's isFirstUser (selectCurrentUserIsFirstUser).
describe('PropertyWorkerTableComponent Chemistry and Halebid columns', () => {
  it('shows both columns to the first user', () => {
    const {component} = createComponent({isFirstUser: true});
    expect(fields(component)).toEqual(expect.arrayContaining(['complianceApp', 'chemistryApp', 'halebidApp']));
  });

  it('hides both columns from every other user, keeping the other app columns', () => {
    const {component} = createComponent({isFirstUser: false});
    expect(fields(component)).not.toContain('chemistryApp');
    expect(fields(component)).not.toContain('halebidApp');
    expect(fields(component)).toEqual(expect.arrayContaining(['complianceApp', 'adHocApp', 'timeApp', 'archiveApp']));
  });

  it('keeps them hidden when the headers are rebuilt for resigned workers', () => {
    const {component} = createComponent({isFirstUser: false});
    component.showResigned = true;
    component.ngOnChanges({showResigned: new SimpleChange(false, true, false)});
    expect(fields(component)).toContain('resignedAtDate');
    expect(fields(component)).not.toContain('chemistryApp');
  });
});
