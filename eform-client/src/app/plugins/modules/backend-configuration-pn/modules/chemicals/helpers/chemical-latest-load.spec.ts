import {EMPTY, Subject, of, throwError} from 'rxjs';
import {ChemicalLatestLoad} from './chemical-latest-load';

describe('ChemicalLatestLoad', () => {
  it('is loading until the answer arrives, then hands over the model', () => {
    const load = new ChemicalLatestLoad();
    const answer = new Subject<{success: boolean; model: number}>();
    const loaded = jest.fn();

    load.run(answer, loaded);
    expect(load.loading).toBe(true);
    expect(load.failed).toBe(false);

    answer.next({success: true, model: 7});

    expect(load.loading).toBe(false);
    expect(load.failed).toBe(false);
    expect(loaded).toHaveBeenCalledWith(7);
  });

  it.each([
    ['a refused answer', () => of({success: false, model: 0})],
    ['a null answer', () => of(null)],
    ['a failed request', () => throwError(() => new Error('500'))],
    // The host interceptor ends a 5xx or a network failure with EMPTY: never stuck on "loading".
    ['no answer at all', () => EMPTY],
  ])('fails on %s', (_name, request) => {
    const load = new ChemicalLatestLoad();
    const loaded = jest.fn();

    load.run(request(), loaded);

    expect(load.loading).toBe(false);
    expect(load.failed).toBe(true);
    expect(loaded).not.toHaveBeenCalled();
  });

  it('lets only the newest request deliver, whatever the older one does', () => {
    const load = new ChemicalLatestLoad();
    const older = new Subject<{success: boolean; model: string}>();
    const loaded = jest.fn();
    load.run(older, loaded);
    load.run(of({success: true, model: 'new'}), loaded);

    older.next({success: true, model: 'old'});
    older.error(new Error('late'));

    expect(loaded).toHaveBeenCalledTimes(1);
    expect(loaded).toHaveBeenCalledWith('new');
    expect(load.failed).toBe(false);
  });

  it('ignores an older request that completes without an answer', () => {
    const load = new ChemicalLatestLoad();
    const older = new Subject<{success: boolean; model: string}>();
    const newer = new Subject<{success: boolean; model: string}>();
    load.run(older, jest.fn());
    load.run(newer, jest.fn());

    older.complete();

    expect(load.loading).toBe(true);
    expect(load.failed).toBe(false);
  });

  it('forgets the request in flight on reset', () => {
    const load = new ChemicalLatestLoad();
    const answer = new Subject<{success: boolean; model: number}>();
    const loaded = jest.fn();
    load.run(answer, loaded);

    load.reset();
    answer.next({success: true, model: 1});

    expect(load.loading).toBe(false);
    expect(load.failed).toBe(false);
    expect(loaded).not.toHaveBeenCalled();
  });
});
