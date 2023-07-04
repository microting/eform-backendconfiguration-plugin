import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {tap} from 'rxjs/operators';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel, FolderDto} from 'src/app/common/models';
import {FoldersService, SitesService} from 'src/app/common/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-page',
  templateUrl: './task-wizard-page.component.html',
  styleUrls: ['./task-wizard-page.component.scss'],
})
export class TaskWizardPageComponent implements OnInit, OnDestroy {
  properties: CommonDictionaryModel[] = [];
  folders: FolderDto[] = [];
  sites: CommonDictionaryModel[] = [];

  getPropertiesSub$: Subscription;
  getFoldersSub$: Subscription;
  getSitesSub$: Subscription;

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    private folderService: FoldersService,
    private sitesService: SitesService,
  ) {
  }

  ngOnInit(): void {
    this.getProperties();
    this.getFolders();
    this.getSites();
  }

  getProperties() {
    this.getPropertiesSub$ = this.propertyService.getAllPropertiesDictionary()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.properties = data.model;
        }
      }))
      .subscribe();
  }

  getFolders() {
    this.getFoldersSub$ = this.folderService.getAllFoldersList()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.folders = data.model;
        }
      }))
      .subscribe();
  }

  getSites() {
    this.getSitesSub$ = this.sitesService.getAllSitesDictionary()
      .pipe(tap(result => {
        if (result && result.success && result.success) {
          this.sites = result.model;
        }
      }))
      .subscribe();
  }

  ngOnDestroy(): void {
  }
}
