import {Component, OnDestroy, OnInit} from '@angular/core';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {
  NewReportEformPnModel,
  ReportPnGenerateModel,
} from '../../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {forkJoin, Observable, Subscription, asyncScheduler, of, take} from 'rxjs';
import {Router} from '@angular/router';
import {SharedTagModel,} from 'src/app/common/models';
import {TemplateFilesService} from 'src/app/common/services';
import {AuthStateService} from 'src/app/common/store';
import {ViewportScroller} from '@angular/common';
import {BackendConfigurationPnReportService} from '../../../../services';
import {catchError, tap} from 'rxjs/operators';
import {Store} from '@ngrx/store';
import {
  selectReportsV2ScrollPosition
} from '../../../../state/reports-v2/reports-v2.selector';
import {Gallery, GalleryItem, ImageItem} from 'ng-gallery';
import {Lightbox} from 'ng-gallery/lightbox';
import {ReportStateService} from '../store';
import {TranslateService} from '@ngx-translate/core';

@AutoUnsubscribe()
@Component({
  selector: 'app-backend-configuration-pn-report',
  templateUrl: './report-container.component.html',
  styleUrls: ['./report-container.component.scss'],
})
export class ReportContainerComponent implements OnInit, OnDestroy {
  reportsModel: NewReportEformPnModel[] = [];
  availableTags: SharedTagModel[] = [];
  currentUserFullName: string;
  images: { key: number, value: any }[] = [];
  galleryImages: GalleryItem[] = [];
  startWithParams = false;
  scrollPosition: [number, number] = [0, 0];
  private selectReportsV2ScrollPosition$ = this.store.select(selectReportsV2ScrollPosition);

  getTagsSub$: Subscription;
  generateReportSub$: Subscription;
  downloadReportSub$: Subscription;
  imageSub$: Subscription[] = [];

  constructor(
    private translateService: TranslateService,
    private store: Store,
    private reportService: BackendConfigurationPnReportService,
    private toastrService: ToastrService,
    private router: Router,
    public authStateService: AuthStateService,
    public gallery: Gallery,
    public lightbox: Lightbox,
    private imageService: TemplateFilesService,
    private viewportScroller: ViewportScroller,
    private reportStateService: ReportStateService,
  ) {
    this.selectReportsV2ScrollPosition$
      .pipe(take(1))
      .subscribe(scrollPosition => this.scrollPosition = scrollPosition);
  }

  ngOnInit() {
    let reportPnGenerateModel: ReportPnGenerateModel = {
      ...this.reportStateService.extractData(),
      type: '',
      version2: true
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
      .generateNewReport({
        ...model,
        type: '',
        version2: true
      })
      .subscribe((data) => {
        if (data && data.success) {
          this.reportsModel = data.model;
          if (this.startWithParams) {
            asyncScheduler.schedule(() => this.viewportScroller.scrollToPosition(this.scrollPosition), 1000);
            this.startWithParams = false;
          }
        } else {
          this.reportsModel = [];
        }
      });
  }

  onDownloadReport(model: ReportPnGenerateModel) {
    this.downloadReportSub$ = this.reportService
      .downloadFileReport(model)
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
      ...this.reportStateService.extractData()
    };
    if (model.dateFrom !== undefined) {
      this.onGenerateReport(model);
    }
  }

  getImages(/*reportEformPnModel: NewReportEformPnModel, */caseId: number) {
    this.images = [];
    const observables: Observable<any>[] = [];
    // Get an array of all image names associated with a specific caseId.
    const imageNamesByCaseId = this.reportsModel
      .map(x => x.groupEform) // Map over the reportsModel array to get an array of groupEform objects.
      .flat() // Flatten the array of groupEform objects into a single array.
      .map(x => x.imageNames) // Map over the flattened array of groupEform objects to get an array of imageNames objects.
      .flat() // Flatten the array of imageNames objects into a single array.
      .filter(x => x.caseId === caseId); // Filter the array of imageNames objects to include only those with a matching caseId.
    const length = imageNamesByCaseId.length;
    imageNamesByCaseId
      .forEach((imageValue) => {
        observables.push(this.imageService.getImage(imageValue.imageName));
        if (length === observables.length) {
          this.imageSub$.forEach(sub => sub.unsubscribe());
          this.imageSub$.push(forkJoin(observables).subscribe(blobArr => {
            if (length === blobArr.length) {
              blobArr.forEach((blob, index) => {
                const imageUrl = URL.createObjectURL(blob);
                const val = {
                  src: imageUrl,
                  thumbnail: imageUrl,
                  fileName: imageValue.imageName,
                  name: imageValue.label,
                  geoTag: imageValue.geoLink
                };
                this.images.push({key: Number(imageValue.caseId), value: val});
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

  onClickViewPicture(model: {/*reportIndex: number, */caseId: number }) {
    // const reportEformPnModel = this.reportsModel[model.reportIndex];
    this.getImages(/*reportEformPnModel, */model.caseId);
  }

  onClickEditCase(model: { microtingSdkCaseId: number, eFormId: number, id: number }) {
    this.reportStateService.updateScrollPosition(this.viewportScroller.getScrollPosition()); // TODO currently not work. return [0, 0]
    this.router.navigate([`/plugins/backend-configuration-pn/case/`, model.microtingSdkCaseId, model.eFormId, model.id],
      {queryParams: {reverseRoute: this.router.url}})
      .then();
  }

  ngOnDestroy(): void {
    this.imageSub$.forEach(sub => sub.unsubscribe());
  }
}
