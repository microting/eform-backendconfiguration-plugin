import {ActivatedRoute} from '@angular/router';
import {Observable, distinctUntilChanged, filter, map} from 'rxjs';

/** The base of every tail-bite page; a page URL is `${TAIL_BITE_BASE}/<propertyId>/<tab>[/...]`. */
export const TAIL_BITE_BASE = '/plugins/backend-configuration-pn/tail-bite';

export type TailBiteTab = 'outbreaks' | 'locations' | 'rules' | 'action-types';

/** The property and tab in a tail-bite URL; nulls when the URL names none (the bare `tail-bite` route). */
export function parseTailBiteUrl(url: string): {propertyId: number | null; tab: TailBiteTab | null} {
  const match = /\/tail-bite\/(\d+)(?=[/?#;]|$)(?:\/(outbreaks|locations|rules|action-types)(?=[/?#;]|$))?/.exec(url.split(/[?#]/)[0]);
  return {
    propertyId: match ? Number(match[1]) : null,
    tab: (match?.[2] as TailBiteTab | undefined) ?? null,
  };
}

/**
 * The `:propertyId` of the current page. The `:propertyId` route is componentless, so its parameter is inherited
 * by the page's own route (the router's default "emptyOnly" inheritance).
 */
export function propertyIdParam(route: ActivatedRoute): Observable<number> {
  return route.paramMap.pipe(
    map((p) => Number(p.get('propertyId'))),
    filter((id) => Number.isInteger(id) && id > 0),
    distinctUntilChanged(),
  );
}
