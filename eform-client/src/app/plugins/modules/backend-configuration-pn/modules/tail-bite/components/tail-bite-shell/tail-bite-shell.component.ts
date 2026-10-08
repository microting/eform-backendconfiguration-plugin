import {NgFor, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatTabsModule} from '@angular/material/tabs';
import {NavigationEnd, Router, RouterModule} from '@angular/router';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {Subscription, filter} from 'rxjs';
import {TailBitePropertyStatus} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TAIL_BITE_BASE, parseTailBiteUrl} from '../../shared/tail-bite-route';

/**
 * The tail-bite area: property picker, the four tabs and the page below them. The property lives in the URL
 * (`tail-bite/<propertyId>/<tab>`); the picker navigates, the pages read the route. The bare `tail-bite` URL
 * opens the first property with tail biting enabled.
 */
/** An outbreak page URL (…/outbreaks/<id>), whose property the page itself owns. */
const OUTBREAK_DETAIL = /\/outbreaks\/\d+(?=[/?#;]|$)/;

@Component({
  selector: 'app-tail-bite-shell',
  templateUrl: './tail-bite-shell.component.html',
  imports: [NgFor, NgIf, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatTabsModule, MtxSelectModule, TranslateModule],
})
export class TailBiteShellComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private router = inject(Router);

  readonly base = TAIL_BITE_BASE;
  readonly links = [
    {path: 'outbreaks', id: 'tailBiteTabOutbreaks', label: 'Outbreaks'},
    {path: 'locations', id: 'tailBiteTabLocations', label: 'Locations and QR'},
    {path: 'rules', id: 'tailBiteTabRules', label: 'Outbreak rules'},
    {path: 'action-types', id: 'tailBiteTabActionTypes', label: 'Action types'},
  ];
  /** null until loaded; then the properties with tail biting enabled, possibly none. */
  properties: TailBitePropertyStatus[] | null = null;
  propertyId: number | null = null;
  private sub?: Subscription;

  ngOnInit(): void {
    this.sub = this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.syncFromUrl());
    this.syncFromUrl();
    this.service.getProperties().subscribe({
      next: (res) => {
        this.properties = res?.success ? res.model.filter((p) => p.enabled) : [];
        this.syncFromUrl();
      },
      // A failed call shows the empty state instead of a blank page.
      error: () => {
        this.properties = [];
        this.syncFromUrl();
      },
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  select(propertyId: number | null): void {
    if (propertyId === null || propertyId === this.propertyId) {
      return;
    }
    // A detail page (outbreaks/:id) belongs to its property: switching goes to the new property's list.
    this.router.navigate([this.base, propertyId, parseTailBiteUrl(this.router.url).tab ?? 'outbreaks']);
  }

  private syncFromUrl(): void {
    const fromUrl = parseTailBiteUrl(this.router.url);
    this.propertyId = fromUrl.propertyId;
    if (!this.properties) {
      return;
    }
    if (this.properties.length === 0) {
      // Nothing to show: no tabs and no page, only the empty state.
      this.propertyId = null;
    } else if (this.propertyId === null) {
      // Bare URL: open the first property on the outbreaks tab.
      this.router.navigate([this.base, this.properties[0].propertyId, 'outbreaks'], {replaceUrl: true});
    } else if (!this.properties.some((p) => p.propertyId === this.propertyId) && !OUTBREAK_DETAIL.test(this.router.url)) {
      // A property that is not offered (disabled, unknown): swap only the property segment, so the tab, the query and the
      // fragment survive. An outbreak page is left alone: it corrects the URL to the outbreak's own property itself, and
      // two corrections would chase each other when that property is not offered here.
      const first = this.properties[0].propertyId;
      this.router.navigateByUrl(this.router.url.replace(/\/tail-bite\/\d+/, `/tail-bite/${first}`), {replaceUrl: true});
    }
  }
}
