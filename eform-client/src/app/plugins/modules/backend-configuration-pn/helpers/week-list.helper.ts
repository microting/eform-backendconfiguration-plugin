import {TranslateService} from '@ngx-translate/core';

/**
 * Generates a list of weeks up to a specified limit.
 *
 * @param {TranslateService} translate - The translation service used to translate week names.
 * @param {number} limit - The number of weeks to generate in the list.
 * @returns {Array} An array of week objects, each with an id and a translated name.
 */
export function generateWeeksList(translate: TranslateService, limit: number): any[] {
  const weeksList = [];
  for (let i = 1; i <= limit; i++) {
    let suffix = 'th';
    if(i === 1) {
      weeksList.push({
        id: i,
        name: translate.instant(`Weekly`)
      });
      continue;
    }
    if (i % 10 === 1 && i !== 11) {
      suffix = 'st';
    } else if (i % 10 === 2 && i !== 12) {
      suffix = 'nd';
    } else if (i % 10 === 3 && i !== 13) {
      suffix = 'rd';
    }
    weeksList.push({
      id: i,
      name: translate.instant(`${i}${suffix} week`)
    });
  }
  return weeksList;
}
