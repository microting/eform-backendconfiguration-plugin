import {describe, it, expect, beforeEach, vi} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {selectAuthIsAdmin} from 'src/app/state';

// The plugin components barrel re-exports BackendConfigurationCaseModule, which pulls in the host's
// eform-cases components (element-picture -> ng-gallery). ng-gallery's partially compiled bundle throws
// "Cannot access 'GalleryItemComponent' before initialization" when the Angular test builder loads it
// unlinked, and the gallery is not under test, so stub it (vi.mock on package specifiers works here).
vi.mock('ng-gallery', () => ({Gallery: class {}, GalleryModule: class {}, ImageItem: class {}, GALLERY_CONFIG: 'GALLERY_CONFIG'}));
vi.mock('ng-gallery/lightbox', () => ({Lightbox: class {}, LightboxModule: class {}}));

import {ReportTableComponent} from './report-table.component';

/**
 * Instance-based spec (repo convention — see
 * documents-folder-create.component.spec.ts): the component's mtx-grid/
 * mat-menu template is not compiled; the class is constructed through
 * TestBed.runInInjectionContext so its inject()-based dependencies resolve
 * against the stubbed providers.
 */
describe('ReportTableComponent', () => {
  let component: ReportTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // A signed-in non-admin: every selector is truthy except selectAuthIsAdmin,
        // which drives isAdmin (take(1) in the constructor).
        {provide: Store, useValue: {select: vi.fn((selector: unknown) => of(selector !== selectAuthIsAdmin))}},
        {provide: TranslateService, useValue: {stream: (key: string) => of(key)}},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: vi.fn().mockReturnValue({})}}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new ReportTableComponent());
    component.ngOnInit();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('ngOnChanges merges the non-admin base headers with one column per item header', () => {
    component.itemHeaders = [{key: 'number', value: 'Weight'}];
    component.ngOnChanges({
      itemHeaders: {
        currentValue: component.itemHeaders,
        previousValue: null,
        firstChange: true,
        isFirstChange: () => true,
      },
    } as any);

    // 6 non-admin base columns (isAdmin=false via the Store stub) + 1 item column.
    expect(component.mergedTableHeaders.length).toBe(7);
    expect(component.mergedTableHeaders[0].field).toBe('microtingSdkCaseId');
    expect(component.mergedTableHeaders[component.mergedTableHeaders.length - 1].field).toBe('Weight');
  });

  it('the generated item-column formatter renders "checked" as the done icon', () => {
    component.itemHeaders = [{key: 'checkbox', value: 'Done?'}];
    component.ngOnChanges({
      itemHeaders: {
        currentValue: component.itemHeaders,
        previousValue: null,
        firstChange: true,
        isFirstChange: () => true,
      },
    } as any);

    const itemColumn = component.mergedTableHeaders[component.mergedTableHeaders.length - 1];
    // MtxGridColumn.formatter is (rowData) => any; feed it a minimal record.
    const rendered = itemColumn.formatter!({
      caseFields: [{key: 'checkbox', value: 'checked'}],
    });
    expect(rendered).toContain('done');
  });
});
