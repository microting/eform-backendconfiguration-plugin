/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data
{
    using System.Collections.Generic;
    using Enums;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

    public static class BackendConfigurationSeedAreaRules
    {
        public static IEnumerable<AreaRules> AreaRulesSeed => new[]
        {
            // Type 1
            new AreaRules
            {
                Id = 1,
                EformId = 0, // TODO: CHANGE
                AreaId = 1,
                EformName = "01. Vandforbrug",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 1, LanguageId = 1, Name = "Water consumption" }
                },
                PlanningId = null,
                FolderId = 5,
            },
            new AreaRules
            {
                Id = 2,
                EformId = 0, // TODO: CHANGE
                AreaId = 1,
                EformName = "01. Elforbrug",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 2, LanguageId = 1, Name = "Electricity consumption" }
                },
                PlanningId = null,
                FolderId = 5,
            },
            new AreaRules
            {
                Id = 3,
                EformId = 0, // TODO: CHANGE
                AreaId = 2,
                EformName = "02. Brandustyr",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 3, LanguageId = 1, Name = "Fire equipment" }
                },
                PlanningId = null,
                FolderId = 7,
            },
            new AreaRules
            {
                Id = 4,
                EformId = 0, // TODO: CHANGE
                AreaId = 2,
                EformName = "02. Sikkerhedsudstyr/værnemidler",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 4, LanguageId = 1, Name = "Safety equipment" }
                },
                PlanningId = null,
                FolderId = 7,
            },
            new AreaRules
            {
                Id = 5,
                EformId = 0, // TODO: CHANGE
                AreaId = 2,
                EformName = "02. Førstehjælp",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 5, LanguageId = 1, Name = "First aid" }
                },
                PlanningId = null,
                FolderId = 7,
            },
            new AreaRules
            {
                Id = 6,
                EformId = 0, // TODO: CHANGE
                AreaId = 7,
                EformName = "07. Rotter",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 6, LanguageId = 1, Name = "Rats" }
                },
                PlanningId = null,
                FolderId = 32,
            },
            new AreaRules
            {
                Id = 7,
                EformId = 0, // TODO: CHANGE
                AreaId = 7,
                EformName = "07. Fluer",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 7, LanguageId = 1, Name = "Flies" }
                },
                PlanningId = null,
                FolderId = 32,
            },
            new AreaRules
            {
                Id = 8,
                EformId = 0, // TODO: CHANGE
                AreaId = 8,
                EformName = "08. Luftrensning timer",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 8, LanguageId = 1, Name = "Flies" }
                },
                PlanningId = null,
                FolderId = 33,
            },
            new AreaRules
            {
                Id = 9,
                EformId = 0, // TODO: CHANGE
                AreaId = 8,
                EformName = "08. Luftrensning serviceaftale",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 9, LanguageId = 1, Name = "Air cleaning service agreement" }
                },
                PlanningId = null,
                FolderId = 35,
            },
            new AreaRules
            {
                Id = 10,
                EformId = 0, // TODO: CHANGE
                AreaId = 8,
                EformName = "08. Luftrensning driftsstop",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 10, LanguageId = 1, Name = "Air cleaning downtime" }
                },
                PlanningId = null,
                FolderId = 35,
            },
            new AreaRules
            {
                Id = 11,
                EformId = 0, // TODO: CHANGE
                AreaId = 9,
                EformName = "09. Forsuring pH værdi",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 11, LanguageId = 1, Name = "Acidification pH value" }
                },
                PlanningId = null,
                FolderId = 36,
            },
            new AreaRules
            {
                Id = 12,
                EformId = 0, // TODO: CHANGE
                AreaId = 9,
                EformName = "09. Forsuring serviceaftale",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 12, LanguageId = 1, Name = "Acidification service agreement" }
                },
                PlanningId = null,
                FolderId = 36,
            },
            new AreaRules
            {
                Id = 13,
                EformId = 0, // TODO: CHANGE
                AreaId = 9,
                EformName = "09. Forsuring driftsstop",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 13, LanguageId = 1, Name = "Acidification downtime" }
                },
                PlanningId = null,
                FolderId = 36,
            },
            new AreaRules
            {
                Id = 13,
                EformId = 0, // TODO: CHANGE
                AreaId = 9,
                EformName = "09. Forsuring driftsstop",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 13, LanguageId = 1, Name = "Acidification downtime" }
                },
                PlanningId = null,
                FolderId = 34,
            },
            new AreaRules
            {
                Id = 14,
                EformId = 0, // TODO: CHANGE
                AreaId = 10,
                EformName = "10. Varmepumpe timer og energi",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 14, LanguageId = 1, Name = "Heat pumps hours and energy" }
                },
                PlanningId = null,
                FolderId = 35,
            },
            new AreaRules
            {
                Id = 15,
                EformId = 0, // TODO: CHANGE
                AreaId = 10,
                EformName = "10. Varmepumpe serviceaftale",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 15, LanguageId = 1, Name = "Heat pump service agreement" }
                },
                PlanningId = null,
                FolderId = 37,
            },
            new AreaRules
            {
                Id = 16,
                EformId = 0, // TODO: CHANGE
                AreaId = 10,
                EformName = "10. Varmepumpe driftsstop",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 16, LanguageId = 1, Name = "Heat pump downtime" }
                },
                PlanningId = null,
                FolderId = 37,
            },
            new AreaRules
            {
                Id = 17,
                EformId = 0, // TODO: CHANGE
                AreaId = 11,
                EformName = "11. Pillefyr",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 17, LanguageId = 1, Name = "Pellet stove" }
                },
                PlanningId = null,
                FolderId = 37,
            },
            new AreaRules
            {
                Id = 18,
                EformId = 0, // TODO: CHANGE
                AreaId = 12,
                EformName = "12. Dieseltank",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 18, LanguageId = 1, Name = "Diesel tank" }
                },
                PlanningId = null,
                FolderId = 38,
            },
            new AreaRules
            {
                Id = 19,
                EformId = 0, // TODO: CHANGE
                AreaId = 12,
                EformName = "12. Motor- og spildolie",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 19, LanguageId = 1, Name = "Motor oil and waste oil" }
                },
                PlanningId = null,
                FolderId = 38,
            },
            new AreaRules
            {
                Id = 20,
                EformId = 0, // TODO: CHANGE
                AreaId = 12,
                EformName = "12. Kemi",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 20, LanguageId = 1, Name = "Chemistry" }
                },
                PlanningId = null,
                FolderId = 38,
            },
            new AreaRules
            {
                Id = 20,
                EformId = 0, // TODO: CHANGE
                AreaId = 12,
                EformName = "12. Affald og farligt affald",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { AreaRuleId = 20, LanguageId = 1, Name = "Waste and hazardous waste" }
                },
                PlanningId = null,
                FolderId = 38,
            },
            //new AreaRules
            //{
            //    Id = 21,
            //    EformId = 0, // TODO: CHANGE
            //    AreaId = 13,
            //    EformName = "13. APV Medarbejder",
            //    AreaRuleTranslations = new List<AreaRuleTranslation>
            //    {
            //        new() { AreaRuleId = 21, LanguageId = 1, Name = "WPA Worker" }
            //    },
            //    PlanningId = null,
            //    FolderId = 39,
            //},
        };
    }
}
