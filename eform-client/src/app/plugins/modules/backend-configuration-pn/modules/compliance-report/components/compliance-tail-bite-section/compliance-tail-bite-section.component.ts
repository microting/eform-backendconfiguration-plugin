import {Overlay} from '@angular/cdk/overlay';
import {DatePipe, NgFor, NgIf} from '@angular/common';
import {Component, Input, OnDestroy, OnInit, inject} from '@angular/core';
import {MatIconButton} from '@angular/material/button';
import {MatDialog} from '@angular/material/dialog';
import {MatIcon} from '@angular/material/icon';
import {MatTooltip} from '@angular/material/tooltip';
import {TranslatePipe} from '@ngx-translate/core';
import {Observable, Subject, of} from 'rxjs';
import {catchError, defaultIfEmpty, map, switchMap, takeUntil} from 'rxjs/operators';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {TAIL_BITE_FACTORS, TailBiteComplianceReport, TailBiteReportOutbreak, TailBiteReportTotals} from '../../../../models';
import {BackendConfigurationPnComplianceReportService, BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteGalleryItem} from '../../../tail-bite/components/tail-bite-outbreak-detail/tail-bite-outbreak-view';
import {TailBitePhotoThumbComponent} from '../../../tail-bite/components/tail-bite-photo-thumb/tail-bite-photo-thumb.component';
import {
  TailBitePhotoViewerComponent,
  TailBitePhotoViewerData,
} from '../../../tail-bite/components/tail-bite-photo-viewer/tail-bite-photo-viewer.component';
import {dateOnlyToLocal, parseServerUtc} from '../../../tail-bite/shared/tail-bite-dates';
import {TailBitePhotoBlobs} from '../../../tail-bite/shared/tail-bite-photo-blobs';
import {ComplianceReportStateService} from '../../store';

/** One factor of the risk assessment as the expanded row shows it; `label` is a translation key. */
export interface ComplianceTailBiteFactorView {
  label: string;
  yes: boolean;
}

export interface ComplianceTailBiteActionView {
  /** Translation key of the action's factor. */
  factorLabel: string;
  description: string;
  responsibleName: string;
  followUpDate: Date;
  doneAt: Date | null;
  doneByName: string | null;
}

/** One table row, plus what its expanded detail row shows. Dates are local Dates for the date pipe. */
export interface ComplianceTailBiteRow {
  id: number;
  propertyName: string;
  locationPath: string;
  openedAt: Date | null;
  assessedAt: Date | null;
  closedAt: Date | null;
  bittenPigs: number;
  severePigs: number;
  assessed: boolean;
  /** Every factor with its answer, in display order; empty when not assessed. */
  factors: ComplianceTailBiteFactorView[];
  /** Translation keys of the factors answered yes, in display order. */
  yesFactors: string[];
  actions: ComplianceTailBiteActionView[];
  actionsDone: number;
  /** The outbreak's photos in registration order: each carries its registration's time, pens and registrant. */
  photos: TailBiteGalleryItem[];
}

export interface ComplianceTailBiteView {
  totals: TailBiteReportTotals;
  rows: ComplianceTailBiteRow[];
  /** More than one property in the section: each row then names its property under the location. */
  showProperty: boolean;
}

const FACTOR_LABEL = new Map(TAIL_BITE_FACTORS.map((f) => [f.factor, f.label]));

function buildRow(o: TailBiteReportOutbreak): ComplianceTailBiteRow {
  const factors = o.answers
    ? TAIL_BITE_FACTORS.map(({answerKey, label}) => ({label, yes: o.answers![answerKey]}))
    : [];
  const actions = o.actions.map((a): ComplianceTailBiteActionView => ({
    factorLabel: FACTOR_LABEL.get(a.factor) ?? '',
    description: a.description,
    responsibleName: a.responsibleName,
    followUpDate: dateOnlyToLocal(a.followUpDate),
    doneAt: parseServerUtc(a.doneAt),
    doneByName: a.doneByName,
  }));
  return {
    id: o.id,
    propertyName: o.propertyName,
    locationPath: o.locationPath,
    openedAt: parseServerUtc(o.openedAt),
    assessedAt: parseServerUtc(o.assessedAt),
    closedAt: parseServerUtc(o.closedAt),
    bittenPigs: o.bittenPigs,
    severePigs: o.severePigs,
    assessed: o.answers !== null,
    factors,
    yesFactors: factors.filter((f) => f.yes).map((f) => f.label),
    actions,
    actionsDone: actions.filter((a) => a.doneAt !== null).length,
    photos: o.registrations.flatMap((r) =>
      r.photos.map((p): TailBiteGalleryItem => ({
        uuid: p.photoUuid,
        registrationId: r.id,
        effectiveAt: parseServerUtc(r.effectiveAt),
        pens: r.pens,
        siteName: r.registeredByName,
      })),
    ),
  };
}

/** What the section shows for a response model: null (the caller sees no Halebid section) shows nothing. */
export function buildComplianceTailBiteView(report: TailBiteComplianceReport | null): ComplianceTailBiteView | null {
  if (!report) {
    return null;
  }
  return {
    totals: report.totals,
    rows: report.outbreaks.map(buildRow),
    showProperty: new Set(report.outbreaks.map((o) => o.propertyId)).size > 1,
  };
}

/**
 * The Halebid section of Rapport (#1517): the outbreaks open at some point in the report period, on the properties
 * where the caller is a tail-bite manager. It fetches on the Rapport view's own trigger with the same filter set, but
 * on its own: a failure of the eForm fetch does not hide it, and a failure of its own (or a null model, i.e. the caller
 * sees no Halebid section) renders nothing at all, with no toast.
 *
 * The caption and heading are projected by the Rapport view, so they wear the view's own section classes.
 */
@Component({
  selector: 'app-compliance-tail-bite-section',
  imports: [DatePipe, NgFor, NgIf, MatIcon, MatIconButton, MatTooltip, TranslatePipe, TailBitePhotoThumbComponent],
  templateUrl: './compliance-tail-bite-section.component.html',
  styleUrls: ['./compliance-tail-bite-section.component.scss'],
})
export class ComplianceTailBiteSectionComponent implements OnInit, OnDestroy {
  /** Emits whenever the Rapport view fetches; the section then fetches with the state's current filter set. */
  @Input({required: true}) trigger!: Observable<unknown>;

  private state = inject(ComplianceReportStateService);
  private reportService = inject(BackendConfigurationPnComplianceReportService);
  private tailBiteService = inject(BackendConfigurationPnTailBiteService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private destroy$ = new Subject<void>();

  view: ComplianceTailBiteView | null = null;
  /** Ids of the outbreaks whose detail row is open; kept over a re-fetch. */
  readonly expanded = new Set<number>();
  /** The section's fetched photos, shared by its thumbnails and the viewer. */
  readonly photoBlobs = new TailBitePhotoBlobs((uuid) => this.tailBiteService.getPhotoBlob(uuid));

  ngOnInit(): void {
    this.trigger
      .pipe(
        // switchMap: a newer trigger cancels a request still in flight.
        switchMap(() =>
          this.reportService.tailBite(this.state.requestModel).pipe(
            map((res) => (res?.success ? res.model ?? null : null)),
            // The host's error interceptor may complete a failed call without a value; that is a failure too.
            defaultIfEmpty(null),
            catchError(() => of(null)),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe((report) => (this.view = buildComplianceTailBiteView(report)));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  isExpanded(row: ComplianceTailBiteRow): boolean {
    return this.expanded.has(row.id);
  }

  toggle(row: ComplianceTailBiteRow): void {
    if (!this.expanded.delete(row.id)) {
      this.expanded.add(row.id);
    }
  }

  trackByRow(_: number, row: ComplianceTailBiteRow): number {
    return row.id;
  }

  /** Opens the viewer on one of the outbreak's photos; previous/next walk that outbreak's photos. */
  openPhoto(row: ComplianceTailBiteRow, index: number): void {
    this.dialog.open<TailBitePhotoViewerComponent, TailBitePhotoViewerData>(TailBitePhotoViewerComponent, {
      ...dialogConfigHelper(this.overlay, {items: row.photos, index, blobs: this.photoBlobs}),
      disableClose: false,
      width: '880px',
      maxWidth: '95vw',
    });
  }
}
