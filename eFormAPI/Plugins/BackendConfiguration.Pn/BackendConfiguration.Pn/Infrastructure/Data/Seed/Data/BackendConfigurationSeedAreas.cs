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
    using System.Linq;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public static class BackendConfigurationSeedAreas
    {
        private const int DanishId = 1;
        private const int EnglishId = 2; 
        public static List<Area> AreasSeed => new()
        {
            new Area
            {
                Id = 1,
                Name = "01. Environmental Management (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
                Type = AreaTypesEnum.Type1,
                AreaRules =new List<AreaRule>
                {
                    new()
                    {
                        Id = 1,
                        EformName = "01. Vandforbrug",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Vandforbrug" },
                            new() { LanguageId = EnglishId, Name = "Water consumption" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // one
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 2,
                        EformName = "01. Elforbrug",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Elforbrug" },
                            new() { LanguageId = EnglishId, Name = "Electricity consumption" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // one
                            RepeatType = 3, // month
                        },
                    },
                } 
            },
            new Area
            {
                Id = 2,
                Name = "02. Contingency",
                Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 3,
                        EformName = "02. Brandudstyr",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Brandudstyr" },
                            new() { LanguageId = EnglishId, Name = "Fire equipment" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 4,
                        EformName = "02. Sikkerhedsudstyr/værnemidler",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Sikkerhedsudstyr" },
                            new() { LanguageId = EnglishId, Name = "Safety equipment" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 5,
                        EformName = "02. Førstehjælp",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Førstehjælp" },
                            new() { LanguageId = EnglishId, Name = "First aid" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                }
            },
            new Area
            {
                Id = 3,
                Name = "03. Slurry tanks",
                Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                Type = AreaTypesEnum.Type2,
                AreaInitialField = new AreaInitialField
                {
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    EformName = "03. Kontrol konstruktion",
                    Alarm = AreaRuleT2AlarmsEnum.No,
                    Type = AreaRuleT2TypesEnum.Closed,
                },
            },
            new Area
            {
                Id = 4,
                Name = "04. Feeding documentation (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/04-foderdokumentation",
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "04. Foderindlægssedler",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 1, // day
                },
            },
            new Area
            {
                Id = 5,
                Name = "05. Stable preparations and tail bite documentation",
                Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
                Type = AreaTypesEnum.Type3,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "05. Stald_klargøring",
                },
            },
            new Area
            {
                Id = 6,
                Name = "06. Silos",
                Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "06. Siloer",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3 // month
                }
            },
            new Area
            {
                Id = 7,
                Name = "07. Pest control",
                Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 6,
                        EformName = "07. Rotter",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Rotter" },
                            new() { LanguageId = EnglishId, Name = "Rats" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                    new()
                    {
                        Id = 7,
                        EformName = "07. Fluer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Fluer" },
                            new() { LanguageId = EnglishId, Name = "Flies" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                }
            },
            new Area
            {
                Id = 8,
                Name = "08. Aircleaning",
                Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 8,
                        EformName = "08. Luftrensning timer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Luftrensning: hus" },
                            new() { LanguageId = EnglishId, Name = "Air cleaning: House" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 9,
                        EformName = "08. Luftrensning serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Luftrensning serviceaftale" },
                            new() { LanguageId = EnglishId, Name = "Air cleaning: Service" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 10,
                        EformName = "08. Luftrensning driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Luftrensning driftsstop" },
                            new() { LanguageId = EnglishId, Name = "Air cleaning: Logbook" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                }
            },
            new Area
            {
                Id = 9,
                Name = "09. Acidification",
                Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 11,
                        EformName = "09. Forsuring pH værdi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Forsuring pH-værdi" },
                            new() { LanguageId = EnglishId, Name = "Acidification pH value" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 12,
                        EformName = "09. Forsuring serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Forsuring serviceaftale" },
                            new() { LanguageId = EnglishId, Name = "Acidification service agreement" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 13,
                        EformName = "09. Forsuring driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Forsuring driftsstop" },
                            new() { LanguageId = EnglishId, Name = "Acidification downtime" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false
                        },
                    },
                }
            },
            new Area
            {
                Id = 10,
                Name = "10. Heat pumps",
                Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 14,
                        EformName = "10. Varmepumpe timer og energi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Varmepumpe timer og energi" },
                            new() { LanguageId = EnglishId, Name = "Heat pumps hours and energy" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 15,
                        EformName = "10. Varmepumpe serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Varmepumpe serviceaftale" },
                            new() { LanguageId = EnglishId, Name = "Heat pump service agreement" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 16,
                        EformName = "10. Varmepumpe driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Varmepumpe driftsstop" },
                            new() { LanguageId = EnglishId, Name = "Heat pump downtime" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                }
            },
            new Area
            {
                Id = 11,
                Name = "11. Pellot stoves",
                Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "11. Pillefyr",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                }
            },
            new Area
            {
                Id = 12,
                Name = "12. Environmentally hazardous substances",
                Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 18,
                        EformName = "12. Dieseltank",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Dieseltank" },
                            new() { LanguageId = EnglishId, Name = "Diesel tank" },
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 19,
                        EformName = "12. Motor- og spildolie",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Motor- og spildolie" },
                            new() { LanguageId = EnglishId, Name = "Motor oil and waste oil" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 20,
                        EformName = "12. Kemi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Kemi" },
                            new() { LanguageId = EnglishId, Name = "Chemistry" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 21,
                        EformName = "12. Affald og farligt affald",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "Affald og farligt affald" },
                            new() { LanguageId = EnglishId, Name = "Trash" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false
                        },
                    },
                }
            },
            new Area
            {
                Id = 13,
                Name = "13. Work Place Assesment",
                Description = @"https://www.microting.dk/eform/landbrug/13-apv",
                Type = AreaTypesEnum.Type4,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 22,
                        AreaId = 13,
                        EformName = "13. APV Medarbejer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = " APV Medarbejer" },
                            new() { LanguageId = EnglishId, Name = "WPA Agriculture" },
                        },
                    },
                },
            },
            new Area
            {
                Id = 14,
                Name = "14. Machines",
                Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 15,
                Name = "15. Inspection of power tools",
                Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 16,
                Name = "16. Inspection of wagons",
                Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "16. Stiger",
                    Notifications = true,
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 17,
                Name = "17. Inspection of ladders",
                Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "17. Vogne",
                    Notifications = true,
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 18,
                Name = "18. Alarm",
                Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 19,
                Name = "19. Ventilation",
                Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 20,
                Name = "20. Recurring tasks (mon-sun)",
                Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
                Type = AreaTypesEnum.Type5,
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "20. Arbejdsopgave udført",
                    RepeatEvery = 7, // 7
                    RepeatType = 1, // days
                }
            },
            new Area
            {
                Id = 21,
                Name = "21. DANISH Standard",
                Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
                Type = AreaTypesEnum.Type4,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 23,
                        AreaId = 13,
                        EformName = "21. DANISH Produktstandard v_1_01",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = DanishId, Name = "DANISH Produktstandard v. 1.01" },
                            new() { LanguageId = EnglishId, Name = "DANISH Standard v. 1.01" },
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true
                        },
                    },
                },
            },
            new Area
            {
                Id = 22,
                Name = "22. Sieve test",
                Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
            },
            new Area
            {
                Id = 23,
                Name = "23. Water consumption",
                Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
            },
            new Area
            {
                Id = 24,
                Name = "24. Electricity consumption",
                Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
            },
            new Area
            {
                Id = 25,
                Name = "25. Field irrigation consumption",
                Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
            },
            new Area
            {
                Id = 26,
                Name = "100. Miscellaneous",
                Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
            },
        };

        public static int LastIndexAreaRules => AreasSeed.SelectMany(x => x.AreaRules).Count();
    }
}
