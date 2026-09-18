import {Store} from '@ngrx/store';
import {of} from 'rxjs';
import {calendarUpdateFilters} from '../../../../state';
import {CalendarStateService} from './calendar.service';

describe('CalendarStateService', () => {
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let service: CalendarStateService;

  beforeEach(() => {
    store = {select: jest.fn().mockReturnValue(of({})), dispatch: jest.fn()};
    service = new CalendarStateService(store as unknown as Store);
  });

  // #1292: the restore path must not wipe the selections the way a property
  // switch does, or re-entering the calendar loses the chosen calendars.
  it('restoreFilters writes the property and its selections without clearing anything', () => {
    service.restoreFilters({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5]});

    expect(store.dispatch).toHaveBeenCalledTimes(1);
    expect(store.dispatch).toHaveBeenCalledWith(
      calendarUpdateFilters({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5]}));
  });

  it('updatePropertyId still clears the selections (a user picking another property)', () => {
    service.updatePropertyId(2);

    expect(store.dispatch).toHaveBeenCalledWith(calendarUpdateFilters({
      propertyId: 2, activeBoardIds: [], activeSiteIds: [], activeTeamIds: [], activeTagNames: [],
    }));
  });
});
