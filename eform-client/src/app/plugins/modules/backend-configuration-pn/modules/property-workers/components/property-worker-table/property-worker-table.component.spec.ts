import {describe, it, expect, beforeEach, vi} from 'vitest';
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
import {PropertyWorkersStateService} from '../store';
import {PropertyWorkerTableComponent} from './property-worker-table.component';

// "Customer no & OTP" have no function when the SDK setting skipCloudDeploy is
// on (no cloud pairing), so the column is hidden then, as on core's device-users
// and units pages (eform-angular-frontend #8193).
describe('PropertyWorkerTableComponent customer no & OTP column', () => {
  let getSkipCloudDeploy: Mock;
  let component: PropertyWorkerTableComponent;

  const create = (skipCloudDeploy: Observable<unknown>) => {
    getSkipCloudDeploy.mockReturnValue(skipCloudDeploy);
    TestBed.configureTestingModule({
      providers: [
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(of(false))}},
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
    component = TestBed.runInInjectionContext(() => new PropertyWorkerTableComponent());
    component.ngOnInit();
  };

  const fields = () => component.tableHeaders.map(h => h.field);

  beforeEach(() => {
    TestBed.resetTestingModule();
    getSkipCloudDeploy = vi.fn();
  });

  it('shows the column when cloud deploy is not skipped', () => {
    create(of({success: true, message: '', model: false}));
    expect(getSkipCloudDeploy).toHaveBeenCalledTimes(1);
    expect(fields()).toContain('customerOtp');
  });

  it('hides the column when cloud deploy is skipped', () => {
    create(of({success: true, message: '', model: true}));
    expect(fields()).not.toContain('customerOtp');
    expect(fields()).toContain('actions');
  });

  it('keeps the column hidden when the headers are rebuilt for resigned workers', () => {
    create(of({success: true, message: '', model: true}));
    component.showResigned = true;
    component.ngOnChanges({showResigned: new SimpleChange(false, true, false)});
    expect(fields()).toContain('resignedAtDate');
    expect(fields()).not.toContain('customerOtp');
  });

  it('shows the column when the setting cannot be read', () => {
    create(of({success: false, message: 'error', model: false}));
    expect(fields()).toContain('customerOtp');
  });
});
