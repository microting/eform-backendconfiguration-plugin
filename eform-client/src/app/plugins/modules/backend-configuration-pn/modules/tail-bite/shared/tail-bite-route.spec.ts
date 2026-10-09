import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {BehaviorSubject} from 'rxjs';
import {parseTailBiteUrl, propertyIdParam} from './tail-bite-route';

describe('tail-bite routes', () => {
  it.each([
    ['/plugins/backend-configuration-pn/tail-bite/3/locations', 3, 'locations'],
    ['/plugins/backend-configuration-pn/tail-bite/3/outbreaks/12', 3, 'outbreaks'],
    ['/plugins/backend-configuration-pn/tail-bite/14/action-types?x=1', 14, 'action-types'],
    ['/plugins/backend-configuration-pn/tail-bite/14', 14, null],
    ['/plugins/backend-configuration-pn/tail-bite', null, null],
    ['/plugins/backend-configuration-pn/tail-bite/3/rulesfoo', 3, null],
    ['/plugins/backend-configuration-pn/tail-bite/3abc', null, null],
  ])('parses %s', (url, propertyId, tab) => {
    expect(parseTailBiteUrl(url)).toEqual({propertyId, tab});
  });

  it('emits each valid property id once', () => {
    const params = new BehaviorSubject(convertToParamMap({propertyId: '3'}));
    const seen: number[] = [];
    propertyIdParam({paramMap: params} as unknown as ActivatedRoute).subscribe((id) => seen.push(id));
    params.next(convertToParamMap({propertyId: '3'}));
    params.next(convertToParamMap({propertyId: 'x'}));
    params.next(convertToParamMap({propertyId: '4'}));
    expect(seen).toEqual([3, 4]);
  });
});
