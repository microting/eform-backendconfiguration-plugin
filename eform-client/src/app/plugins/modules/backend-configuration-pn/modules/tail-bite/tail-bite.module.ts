import {NgModule} from '@angular/core';
import {TailBiteRouting} from './tail-bite.routing';

/**
 * Tail biting (halebid) web admin (spec 2026-10-04-halebid-app-design, sub-project 3). Lazy child of the
 * plugin root at `tail-bite`; the pages are standalone components (see tail-bite.routing.ts). The dashboard and
 * reports are sub-project 5 and are not here.
 */
@NgModule({
  imports: [TailBiteRouting],
})
export class TailBiteModule {}
