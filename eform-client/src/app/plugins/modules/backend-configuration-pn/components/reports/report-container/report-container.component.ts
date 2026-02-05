import {Component, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {
  ReportEformPnModel,
  ReportPnGenerateModel,
} from '../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {forkJoin, Observable, Subscription, asyncScheduler, of, take} from 'rxjs';
import {
  SharedTagModel,
} from 'src/app/common/models';
import {TemplateFilesService} from 'src/app/common/services';
import {ViewportScroller} from '@angular/common';
import {BackendConfigurationPnReportService} from '../../../services';
import {catchError, tap} from 'rxjs/operators';
import {Store} from '@ngrx/store';
import {
  selectReportsV1ScrollPosition
} from '../../../state/reports-v1/reports-v1.selector';
import {Gallery, GalleryItem, ImageItem} from 'ng-gallery';
import {Lightbox} from 'ng-gallery/lightbox';
import {ReportStateService} from '../store';
import {Router} from '@angular/router';
import {TranslateService} from "@ngx-translate/core";

@AutoUnsubscribe()
@Component({
    selector: 'app-backend-configuration-pn-report',
    templateUrl: './report-container.component.html',
    styleUrls: ['./report-container.component.scss'],
    standalone: false
})
export class ReportContainerComponent implements OnInit, OnDestroy {
  private translateService = inject(TranslateService);
  private reportService = inject(BackendConfigurationPnReportService);
  private toastrService = inject(ToastrService);
  public gallery = inject(Gallery);
  public lightbox = inject(Lightbox);
  private imageService = inject(TemplateFilesService);
  private viewportScroller = inject(ViewportScroller);
  private store = inject(Store);
  private reportStateService = inject(ReportStateService);
  private router = inject(Router);

  reportsModel: ReportEformPnModel[] = [];
  availableTags: SharedTagModel[] = [];
  currentUserFullName: string;
  images: { key: number, value: any }[] = [];
  galleryImages: GalleryItem[] = [];
  isDescriptionBlockCollapsed = new Array<boolean>();
  scrollPosition: [number, number] = [0, 0];
  startWithParams: boolean = false;
  private selectReportsV1ScrollPosition$ = this.store.select(selectReportsV1ScrollPosition);

  getTagsSub$: Subscription;
  imageSub$: Subscription[] = [];
  generateReportSub$: Subscription;
  downloadReportSub$: Subscription;



  ngOnInit() {
    this.selectReportsV1ScrollPosition$
      .pipe(take(1))
      .subscribe(scrollPosition => this.scrollPosition = scrollPosition);

    let reportPnGenerateModel: ReportPnGenerateModel = {
      ...this.reportStateService.extractData(),
      type: '',
      version2: false
  };
    if (reportPnGenerateModel.dateFrom !== null) {
      this.startWithParams = true;
      this.onGenerateReport(reportPnGenerateModel);
    }
    this.getTags();
  }

  getTags() {
    this.getTagsSub$ = this.reportService.getPlanningsTags().subscribe((data) => {
      if (data && data.success) {
        this.availableTags = data.model;
      }
    });
  }

  onGenerateReport(model: ReportPnGenerateModel) {
    this.generateReportSub$ = this.reportService
      .generateReport({
        ...model,
        type: '',
        version2: false
      })
      .subscribe((data) => {
        if (data && data.success) {
          this.reportsModel = data.model;
          this.isDescriptionBlockCollapsed = this.reportsModel.map(() => true);
          if (this.startWithParams) {
            asyncScheduler.schedule(() => this.viewportScroller.scrollToPosition(this.scrollPosition), 1000);
            this.startWithParams = false;
          }
        }
      });
  }

  onDownloadReport(model: ReportPnGenerateModel) {
    this.downloadReportSub$ = this.reportService
      .downloadFileReport({...model, version2: false,})
      .pipe(
        tap((data) => saveAs(data, `${model.dateFrom}_${model.dateTo}_report.${model.type}`)),
        catchError((_) => {
          this.toastrService.info(this.translateService.instant('No data in selected period'));
          return of(null);
        })
      )
      .subscribe();
  }

  onPlanningCaseDeleted() {
    const model = {
      ...this.reportStateService.extractData(),
      type: '',
      version2: false
    };
    if (model.dateFrom !== undefined) {
      this.onGenerateReport(model);
    }
  }

  getImages(reportEformPnModel: ReportEformPnModel, caseId: number) {
    this.images = [];
    const observables: Observable<any>[] = [];
    const length = reportEformPnModel.imageNames.filter(x => x.key[0] === caseId.toString()).length;
    reportEformPnModel.imageNames.filter(x => x.key[0] === caseId.toString())
      .forEach((imageValue) => {
        observables.push(this.imageService.getImage(imageValue.value[0]));
        if (length === observables.length) {
          this.imageSub$.push(forkJoin(observables).subscribe(blobArr => {
            if (length === blobArr.length) {
              blobArr.forEach((blob, index) => {
                const imageUrl = URL.createObjectURL(blob);
                const val = {
                  src: imageUrl,
                  thumbnail: imageUrl,
                  fileName: imageValue.value[0],
                  name: imageValue.key[1],
                  geoTag: imageValue.value[1]
                };
                this.images.push({key: Number(imageValue.key[0]), value: val});
                this.images.sort((a, b) => a.key < b.key ? -1 : a.key > b.key ? 1 : 0);
                if (index + 1 === blobArr.length) {
                  this.updateGallery();
                  this.openPicture(0);
                }
              });
            }
          }));
        }
      });
  }

  updateGallery() {
    this.galleryImages = this.images.map(value => new ImageItem({src: value.value.src, thumb: value.value.thumbnail}));
  }

  openPicture(i: any) {
    if (this.galleryImages.length > 1) {
      this.gallery.ref('lightbox', {counterPosition: 'bottom'}).load(this.galleryImages);
      this.lightbox.open(i);
    } else {
      this.gallery.ref('lightbox', {counter: false}).load(this.galleryImages);
      this.lightbox.open(i);
    }
  }

  onClickViewPicture(model: { reportIndex: number, caseId: number }) {
    const reportEformPnModel = this.reportsModel[model.reportIndex];
    this.getImages(reportEformPnModel, model.caseId);
  }

  toggleCollapse(i: number) {
    this.isDescriptionBlockCollapsed[i] = !this.isDescriptionBlockCollapsed[i];
  }

  ngOnDestroy(): void {
    this.imageSub$.forEach(sub => sub.unsubscribe());
  }

  onClickEditCase(model: { microtingSdkCaseId: number, eFormId: number, id: number }) {
    this.reportStateService.updateScrollPosition(this.viewportScroller.getScrollPosition()); // TODO currently not work. return [0, 0]
    this.router.navigate([`/plugins/backend-configuration-pn/case/`, model.microtingSdkCaseId, model.eFormId, model.id],
      {queryParams: {reverseRoute: this.router.url}})
      .then();
  }
}
